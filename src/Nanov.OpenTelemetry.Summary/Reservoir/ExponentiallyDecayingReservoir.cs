namespace Nanov.OpenTelemetry.Summary.Reservoir;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Exponentially decaying reservoir (Cormode et al., forward decay): a fixed-size sample biased toward recent
/// values, from which weighted quantiles are read. The sample is a min-heap on priority, so a full reservoir
/// evicts its lowest-priority value in O(log n) instead of shifting an array. It grows with the values it
/// receives, up to its size, so a quiet series never holds a full sample's memory. Count, sum, min and max
/// cover every recorded value, not only the sampled ones. Not thread-safe: the owning child serializes access.
/// </summary>
internal sealed class ExponentiallyDecayingReservoir {
	private const int InitialCapacity = 16;

	private readonly double _alpha;
	private readonly long _rescaleSeconds;
	private readonly int _sampleSize;

	private Sample[] _heap = [];
	private int _length;

	private long _startTime;
	private long _nextRescale;
	private long _weightTick = -1;
	private double _weight;
	private ulong _rng;

	private long _count;
	private double _sum;
	private double _min = double.PositiveInfinity;
	private double _max = double.NegativeInfinity;

	public ExponentiallyDecayingReservoir(int sampleSize = 1028, double alpha = 0.015, TimeSpan? rescaleInterval = null) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleSize);
		_sampleSize = sampleSize;
		_alpha = alpha;
		_rescaleSeconds = (long)(rescaleInterval ?? TimeSpan.FromHours(1)).TotalSeconds;
		_rng = (ulong)Random.Shared.NextInt64();
		_startTime = GetTickSeconds();
		_nextRescale = _startTime + _rescaleSeconds;
	}

	internal int SampledCount => _length;
	internal int Capacity => _heap.Length;

	public void Record(double value)
		=> Record(value, GetTickSeconds());

	internal void Record(double value, long now) {
		if (now >= _nextRescale)
			Rescale(now);

		// The weight only changes once per second: skip the Exp for every other value.
		if (now != _weightTick) {
			_weightTick = now;
			_weight = Math.Exp(_alpha * (now - _startTime));
		}

		var weight = _weight;
		var priority = weight / NextUnit();

		_count++;
		_sum += value;
		if (value < _min) _min = value;
		if (value > _max) _max = value;

		if (_length < _sampleSize) {
			if (_length == _heap.Length)
				Grow();
			SiftUp(_length++, new Sample(priority, value, weight));
		}
		else if (priority > _heap[0].Priority) {
			SiftDown(new Sample(priority, value, weight));
		}
	}

	/// <summary>
	/// Writes the weighted quantile for each of <paramref name="quantiles"/> into <paramref name="quantileValues"/>
	/// (zeros when empty), then starts a new window.
	/// </summary>
	public SnapshotResult SnapshotAndReset(ReadOnlySpan<double> quantiles, Span<double> quantileValues) {
		SnapshotResult result;
		if (_length == 0) {
			quantileValues[..quantiles.Length].Clear();
			result = SnapshotResult.Empty;
		}
		else {
			// The window ends here, so the heap's own buffer is sorted in place: no second copy.
			var sorted = _heap.AsSpan(0, _length);
			// A static lambda is cached once; the struct-comparer overload boxes it on every call.
			sorted.Sort(static (x, y) => x.Value.CompareTo(y.Value));

			var totalWeight = 0.0;
			foreach (ref readonly var sample in sorted)
				totalWeight += sample.Weight;

			for (var q = 0; q < quantiles.Length; q++)
				quantileValues[q] = ComputeQuantile(sorted, totalWeight, quantiles[q]);

			result = new SnapshotResult(_max, _min, _sum, _count);
		}

		_length = 0;
		_count = 0;
		_sum = 0;
		_min = double.PositiveInfinity;
		_max = double.NegativeInfinity;
		_startTime = GetTickSeconds();
		_nextRescale = _startTime + _rescaleSeconds;
		_weightTick = -1;
		return result;
	}

	private static double ComputeQuantile(ReadOnlySpan<Sample> sorted, double totalWeight, double q) {
		if (q <= 0)
			return sorted[0].Value;
		if (q >= 1)
			return sorted[^1].Value;

		var target = q * totalWeight;
		var cumulative = 0.0;
		foreach (ref readonly var sample in sorted) {
			cumulative += sample.Weight;
			if (cumulative >= target)
				return sample.Value;
		}

		return sorted[^1].Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Grow() {
		// Doubling, but never past the sample size: a full reservoir holds exactly that many slots.
		var next = Math.Min(Math.Max(_heap.Length * 2, InitialCapacity), _sampleSize);
		Array.Resize(ref _heap, next);
	}

	private void SiftUp(int index, Sample sample) {
		ref var heap = ref MemoryMarshal.GetArrayDataReference(_heap);
		while (index > 0) {
			var parent = (index - 1) >> 1;
			ref var p = ref Unsafe.Add(ref heap, parent);
			if (p.Priority <= sample.Priority)
				break;
			Unsafe.Add(ref heap, index) = p;
			index = parent;
		}
		Unsafe.Add(ref heap, index) = sample;
	}

	/// <summary>Replaces the root (the lowest priority) with <paramref name="sample"/> and restores the heap.</summary>
	private void SiftDown(Sample sample) {
		ref var heap = ref MemoryMarshal.GetArrayDataReference(_heap);
		var length = _length;
		var index = 0;
		while (true) {
			var child = (index << 1) + 1;
			if (child >= length)
				break;
			var right = child + 1;
			if (right < length && Unsafe.Add(ref heap, right).Priority < Unsafe.Add(ref heap, child).Priority)
				child = right;
			ref var c = ref Unsafe.Add(ref heap, child);
			if (sample.Priority <= c.Priority)
				break;
			Unsafe.Add(ref heap, index) = c;
			index = child;
		}
		Unsafe.Add(ref heap, index) = sample;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Rescale(long now) {
		// Scaling every priority by the same positive factor keeps the heap order, so this is in place.
		var factor = Math.Exp(-_alpha * (now - _startTime));
		foreach (ref var sample in _heap.AsSpan(0, _length))
			sample = new Sample(sample.Priority * factor, sample.Value, sample.Weight * factor);

		_startTime = now;
		_nextRescale = now + _rescaleSeconds;
		_weightTick = -1;
	}

	/// <summary>Uniform in (0, 1]: never zero, so the priority division is finite. SplitMix64.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private double NextUnit() {
		var z = _rng += 0x9E3779B97F4A7C15UL;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		z ^= z >> 31;
		return ((z >> 11) + 1) * (1.0 / (1UL << 53));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static long GetTickSeconds()
		=> Environment.TickCount64 / 1000;

	private readonly record struct Sample(double Priority, double Value, double Weight);
}
