namespace Nanov.OpenTelemetry.Summary;

using System.Diagnostics.Metrics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Reservoir;

/// <summary>
/// One tag set of a summary: lock-free double buffers in front of a reservoir.
/// </summary>
/// <remarks>
/// The buffer is split into stripes, and each thread writes to its own (threads are numbered on first use,
/// round robin), so concurrent writers do not share a counter or a cache line. <see cref="Record"/> reserves a
/// slot with one atomic add and stores the value's bits. When a stripe's active bucket reaches the high-water
/// mark, the child queues itself on the thread pool, which flips every stripe's buckets and feeds the sealed
/// ones into the reservoir. Export drains whatever is left.
/// <para>
/// A writer stores into its slot a few instructions after reserving it, and the drain waits for a reserved slot
/// that is still empty, so no reserved value is skipped; writers never wait. While the drain takes a bucket's
/// slots the bucket is closed: a writer that still sees it as active gets an index past the end and takes the
/// drop path, so it cannot overwrite a slot the drain has not taken yet. A value that finds both buckets full
/// misses the sample but is still counted: the drop path adds it to its stripe's dropped count, sum and max, so
/// <c>.count</c>, <c>.sum</c> and <c>.max</c> stay exact and <c>.dropped</c> says how many values the quantiles
/// did not see.
/// </para>
/// <para>
/// A stripe's slots are allocated by the first value recorded into it, so a tag set that only a few threads
/// touch holds only their slots.
/// </para>
/// </remarks>
internal sealed class SummaryChild : IThreadPoolWorkItem {
	/// <summary>A sealed bucket's length while the drain takes its slots: far past any capacity.</summary>
	private const int Closed = int.MaxValue / 2;

	/// <summary>A slot no value has been written to. A NaN pattern, and NaN is never recorded.</summary>
	private const long Empty = -1L;

	private static readonly int StripeCount = (int)Math.Min(BitOperations.RoundUpToPowerOf2((uint)Environment.ProcessorCount), 64u);
	private static int s_nextThread;

	[ThreadStatic]
	private static int t_thread;

	private readonly Stripe[] _stripes;
	private readonly int _perStripe;
	private readonly int _highWater;
	private readonly Instrument _gate;
	private int _queued;

	private readonly Lock _lock = new();
	private readonly ExponentiallyDecayingReservoir _reservoir;

	// Guarded by _lock. Read by the export callbacks.
	internal readonly double[] QuantileValues;
	internal SnapshotResult Last;
	internal long TotalCount;
	internal double TotalSum;
	internal long TotalDropped;

	/// <summary>The recorded tags, for <c>.max</c>, <c>.count</c>, <c>.sum</c> and <c>.dropped</c>.</summary>
	public readonly KeyValuePair<string, object?>[] BaseTags;

	/// <summary>The recorded tags plus <c>quantile</c>, one set per quantile, built once.</summary>
	public readonly KeyValuePair<string, object?>[][] QuantileTagArrays;

	public SummaryChild(SummaryOptions options, Instrument gate, ReadOnlySpan<KeyValuePair<string, object?>> tags)
		: this(options, gate, tags, StripeCount) { }

	internal SummaryChild(SummaryOptions options, Instrument gate, ReadOnlySpan<KeyValuePair<string, object?>> tags, int stripes) {
		_gate = gate;
		_stripes = new Stripe[stripes];
		for (var i = 0; i < _stripes.Length; i++)
			_stripes[i].DroppedMax = double.NegativeInfinity;

		_perStripe = Math.Max((options.BufferCapacity + stripes - 1) / stripes, 64);
		_highWater = Math.Max(_perStripe * 3 / 4, 1);
		_reservoir = new ExponentiallyDecayingReservoir(options.SampleSize, options.Alpha);
		QuantileValues = new double[options.Quantiles.Length];

		BaseTags = tags.ToArray();
		QuantileTagArrays = new KeyValuePair<string, object?>[options.Quantiles.Length][];
		for (var i = 0; i < QuantileTagArrays.Length; i++) {
			var arr = new KeyValuePair<string, object?>[tags.Length + 1];
			tags.CopyTo(arr);
			arr[tags.Length] = new KeyValuePair<string, object?>("quantile", options.QuantileLabels[i]);
			QuantileTagArrays[i] = arr;
		}
	}

	public bool IsEnabled => _gate.Enabled;

	internal Lock SyncRoot => _lock;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Record(double value) {
		if (double.IsNaN(value) || !_gate.Enabled)
			return;

		ref var stripe = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_stripes), ThreadNumber() & (_stripes.Length - 1));
		// Before the reservation, so nothing slow sits between reserving a slot and storing into it: the drain
		// waits for that store.
		var slots = Volatile.Read(ref stripe.Slots) ?? Allocate(ref stripe);
		var active = Volatile.Read(ref stripe.Active) & 1;
		var index = Interlocked.Increment(ref Unsafe.Add(ref stripe.Len0, active)) - 1;
		if ((uint)index >= (uint)_perStripe) {
			Drop(ref stripe, value);
			Schedule();
			return;
		}

		Volatile.Write(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(slots), active * _perStripe + index), BitConverter.DoubleToInt64Bits(value));
		if (index + 1 == _highWater)
			Schedule();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int ThreadNumber() {
		var n = t_thread;
		return n != 0 ? n : AssignThreadNumber();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int AssignThreadNumber()
		=> t_thread = Interlocked.Increment(ref s_nextThread);

	/// <summary>The stripe's slots, both buckets, allocated by its first value. Out of line, so <see cref="Record"/> stays small.</summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private long[] Allocate(ref Stripe stripe) {
		var slots = new long[2 * _perStripe];
		slots.AsSpan().Fill(Empty);
		return Interlocked.CompareExchange(ref stripe.Slots, slots, null) ?? slots;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Drop(ref Stripe stripe, double value) {
		AddDouble(ref stripe.DroppedSum, value);
		// Most dropped values are not a new maximum: a read settles those.
		if (value > Volatile.Read(ref stripe.DroppedMax))
			MaxDouble(ref stripe.DroppedMax, value);
		// Last, so a drain that sees the count also sees the value in the sum and max.
		Interlocked.Increment(ref stripe.DroppedCount);
	}

	/// <summary>Queue the child for aggregation once; while it is queued, writers only read the flag.</summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Schedule() {
		if (Volatile.Read(ref _queued) != 0 || Interlocked.Exchange(ref _queued, 1) != 0)
			return;
		ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
	}

	void IThreadPoolWorkItem.Execute() {
		Volatile.Write(ref _queued, 0);
		lock (_lock)
			Drain();
	}

	/// <summary>
	/// Seal every stripe's active bucket and feed it to the reservoir. The caller holds the lock, so there is one
	/// drainer at a time; writers move to the other bucket, which the previous drain emptied.
	/// </summary>
	private void Drain() {
		for (var s = 0; s < _stripes.Length; s++) {
			ref var stripe = ref _stripes[s];
			if (Volatile.Read(ref stripe.Len0) == 0 && Volatile.Read(ref stripe.Len1) == 0)
				continue;

			// Only the drainer writes Active, under the lock.
			var sealedBucket = stripe.Active & 1;
			Volatile.Write(ref stripe.Active, sealedBucket ^ 1);

			ref var len = ref Unsafe.Add(ref stripe.Len0, sealedBucket);
			var count = Math.Min(Interlocked.Exchange(ref len, Closed), _perStripe);
			// A writer allocates before it reserves, so a reservation seen here comes with the slots.
			var slots = Volatile.Read(ref stripe.Slots);
			if (slots is not null) {
				foreach (ref var slot in slots.AsSpan(sealedBucket * _perStripe, count))
					_reservoir.Record(BitConverter.Int64BitsToDouble(Take(ref slot)));
			}

			// Reopened empty. A writer that still sees this bucket as active now stores at the front, and the
			// value is drained the next time the bucket is sealed.
			Volatile.Write(ref len, 0);
		}
	}

	/// <summary>
	/// Drain, close the window and keep its results. Returns whether the sample had values, that is whether the
	/// quantiles are worth reporting. The caller holds <see cref="SyncRoot"/>.
	/// </summary>
	internal bool Collect(ReadOnlySpan<double> quantiles) {
		Drain();
		var snapshot = _reservoir.SnapshotAndReset(quantiles, QuantileValues);
		var sampled = snapshot.Count > 0;

		long dropped = 0;
		double droppedSum = 0, droppedMax = double.NegativeInfinity;
		for (var s = 0; s < _stripes.Length; s++) {
			ref var stripe = ref _stripes[s];
			var count = Interlocked.Exchange(ref stripe.DroppedCount, 0);
			if (count == 0)
				continue;
			dropped += count;
			droppedSum += Interlocked.Exchange(ref stripe.DroppedSum, 0);
			droppedMax = Math.Max(droppedMax, Interlocked.Exchange(ref stripe.DroppedMax, double.NegativeInfinity));
		}

		if (dropped > 0) {
			snapshot = new SnapshotResult(
				max: sampled ? Math.Max(snapshot.Max, droppedMax) : droppedMax,
				min: snapshot.Min,
				sum: snapshot.Sum + droppedSum,
				count: snapshot.Count + dropped);
			TotalDropped += dropped;
		}

		Last = snapshot;
		TotalCount += snapshot.Count;
		TotalSum += snapshot.Sum;
		return sampled;
	}

	/// <summary>
	/// Take a reserved slot's value, waiting for a writer that has reserved it but not stored yet. That writer is
	/// a few instructions from its store, so this rarely spins.
	/// </summary>
	private static long Take(ref long slot) {
		var spin = new SpinWait();
		while (true) {
			var bits = Interlocked.Exchange(ref slot, Empty);
			if (bits != Empty)
				return bits;
			spin.SpinOnce(sleep1Threshold: -1);
		}
	}

	private static void AddDouble(ref double location, double value) {
		var current = Volatile.Read(ref location);
		while (true) {
			var seen = Interlocked.CompareExchange(ref location, current + value, current);
			if (seen.Equals(current))
				return;
			current = seen;
		}
	}

	private static void MaxDouble(ref double location, double value) {
		var current = Volatile.Read(ref location);
		while (value > current) {
			var seen = Interlocked.CompareExchange(ref location, value, current);
			if (seen.Equals(current))
				return;
			current = seen;
		}
	}

	internal int AllocatedStripes {
		get {
			var n = 0;
			foreach (ref readonly var stripe in _stripes.AsSpan())
				if (stripe.Slots is not null)
					n++;
			return n;
		}
	}

	/// <summary>
	/// A double buffer and its drops. The writers' fields and the drop path's fields sit 128 bytes apart, and each
	/// stripe spans 256 bytes, so neighbouring stripes do not share cache lines (128-byte lines on Apple silicon).
	/// Bucket <c>b</c> is <c>Slots[b * perStripe..][..perStripe]</c>.
	/// </summary>
	[StructLayout(LayoutKind.Explicit, Size = 256)]
	private struct Stripe {
		[FieldOffset(0)] public long[]? Slots;
		[FieldOffset(8)] public int Active;
		[FieldOffset(12)] public int Len0;
		[FieldOffset(16)] public int Len1;

		[FieldOffset(128)] public long DroppedCount;
		[FieldOffset(136)] public double DroppedSum;
		[FieldOffset(144)] public double DroppedMax;
	}
}
