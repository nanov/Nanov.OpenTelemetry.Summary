namespace Nanov.OpenTelemetry.Summary;

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Internal;

public sealed class Summary {
	private readonly SummaryOptions _options;
	private readonly ObservableGauge<double> _quantileGauge;
	private readonly SummaryChild _untaggedChild;

	// Lookups are lock-free: the alternate lookup hashes the caller's tags in place. Only a new tag set takes
	// _insertLock, once.
	private readonly ConcurrentDictionary<KeyValuePair<string, object?>[], SummaryChild> _children = new(TagSetComparer.Instance);
	private readonly ConcurrentDictionary<KeyValuePair<string, object?>[], SummaryChild>.AlternateLookup<ReadOnlySpan<KeyValuePair<string, object?>>> _lookup;
	private readonly Lock _insertLock = new();

	// Copy-on-write list of every child, untagged first, so the export callbacks iterate without allocating.
	private SummaryChild[] _all;

	private readonly List<Measurement<double>> _quantileMeasurements = [];
	private readonly List<Measurement<double>> _maxMeasurements = [];
	private readonly List<Measurement<double>> _sumMeasurements = [];
	private readonly List<Measurement<long>> _countMeasurements = [];
	private readonly List<Measurement<long>> _droppedMeasurements = [];

	internal Summary(Meter meter, string name, string? unit, string? description, SummaryOptions options) {
		_options = options;
		_lookup = _children.GetAlternateLookup<ReadOnlySpan<KeyValuePair<string, object?>>>();

		// Registration order is callback order: the quantile gauge closes the window first, so max, count, sum
		// and dropped report from the same one.
		_quantileGauge = meter.CreateObservableGauge(name, CollectQuantiles, unit, description);
		_untaggedChild = new SummaryChild(options, _quantileGauge, default);
		_all = [_untaggedChild];

		if (options.ReportMax)
			meter.CreateObservableGauge(name + ".max", CollectMax, unit);

		if (options.ReportCount)
			meter.CreateObservableCounter(name + ".count", CollectCount);

		if (options.ReportSum)
			meter.CreateObservableCounter(name + ".sum", CollectSum, unit);

		meter.CreateObservableCounter(name + ".dropped", CollectDropped, description: "Values recorded but left out of the quantiles' sample");
	}

	public bool IsEnabled => _quantileGauge.Enabled;

	public void Record(double value)
		=> _untaggedChild.Record(value);

	public void Record(double value, in TagList tags) {
		if (!_quantileGauge.Enabled) return;
		GetOrCreateChild(in tags).Record(value);
	}

	public void Record(double value, params ReadOnlySpan<KeyValuePair<string, object?>> tags) {
		if (!_quantileGauge.Enabled) return;
		GetOrCreateChild(tags).Record(value);
	}

	/// <summary>
	/// A handle for one tag set, which records without looking the tags up: bind once, record many times.
	/// </summary>
	public SummaryHandle Bind(in TagList tags)
		=> new(GetOrCreateChild(in tags));

	/// <inheritdoc cref="Bind(in TagList)"/>
	public SummaryHandle Bind(params ReadOnlySpan<KeyValuePair<string, object?>> tags)
		=> new(GetOrCreateChild(tags));

	public SummaryTimer Time() => new(this, _untaggedChild);

	// Resolved at start, like a bound handle; skipped while disabled so an unobserved summary creates no series.
	public SummaryTimer Time(in TagList tags)
		=> new(this, _quantileGauge.Enabled ? GetOrCreateChild(in tags) : null);

	public SummaryTimer Duration() => Time();
	public SummaryTimer Duration(in TagList tags) => Time(in tags);

	// TagList exposes its tags only by copying them out. Up to 8 (TagList's own inline size) go to a stack buffer;
	// more go to a pooled array. Public API only: TagList's internals differ between DiagnosticSource versions.
	private SummaryChild GetOrCreateChild(in TagList tags) {
		var count = tags.Count;
		if (count == 0)
			return _untaggedChild;
		if (count > TagBuffer.Length)
			return GetOrCreateChildPooled(in tags);

		var buffer = new TagBuffer();
		Span<KeyValuePair<string, object?>> span = buffer;
		span = span[..count];
		tags.CopyTo(span);
		return GetOrCreateChild(span);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private SummaryChild GetOrCreateChildPooled(in TagList tags) {
		var array = ArrayPool<KeyValuePair<string, object?>>.Shared.Rent(tags.Count);
		try {
			var span = array.AsSpan(0, tags.Count);
			tags.CopyTo(span);
			return GetOrCreateChild(span);
		}
		finally {
			ArrayPool<KeyValuePair<string, object?>>.Shared.Return(array, clearArray: true);
		}
	}

	[InlineArray(Length)]
	private struct TagBuffer {
		public const int Length = 8;
		private KeyValuePair<string, object?> _element;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private SummaryChild GetOrCreateChild(ReadOnlySpan<KeyValuePair<string, object?>> tags) {
		if (tags.IsEmpty)
			return _untaggedChild;
		return _lookup.TryGetValue(tags, out var child) ? child : AddChild(tags);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private SummaryChild AddChild(ReadOnlySpan<KeyValuePair<string, object?>> tags) {
		lock (_insertLock) {
			if (_lookup.TryGetValue(tags, out var existing))
				return existing;

			var child = new SummaryChild(_options, _quantileGauge, tags);
			_children.TryAdd(child.BaseTags, child);

			var all = _all;
			var next = new SummaryChild[all.Length + 1];
			all.CopyTo(next, 0);
			next[all.Length] = child;
			Volatile.Write(ref _all, next);
			return child;
		}
	}

	private List<Measurement<double>> CollectQuantiles() {
		var quantiles = _options.Quantiles;
		var measurements = _quantileMeasurements;
		measurements.Clear();

		foreach (var child in Volatile.Read(ref _all)) {
			lock (child.SyncRoot) {
				if (!child.Collect(quantiles))
					continue;
				for (var i = 0; i < quantiles.Length; i++)
					measurements.Add(CreateMeasurement(child.QuantileValues[i], child.QuantileTagArrays[i]));
			}
		}

		return measurements;
	}

	private List<Measurement<double>> CollectMax() {
		var measurements = _maxMeasurements;
		measurements.Clear();

		foreach (var child in Volatile.Read(ref _all))
			lock (child.SyncRoot)
				if (child.Last.Count > 0)
					measurements.Add(CreateMeasurement(child.Last.Max, child.BaseTags));

		return measurements;
	}

	// Cumulative, per-child tagged totals (a child only ever grows), so the counters can be grouped and filtered
	// by the recorded tags (e.g. http_route, status). Children with no recorded value are skipped; the untagged
	// child emits a tagless series.
	private List<Measurement<long>> CollectCount() {
		var measurements = _countMeasurements;
		measurements.Clear();

		foreach (var child in Volatile.Read(ref _all))
			lock (child.SyncRoot)
				if (child.TotalCount > 0)
					measurements.Add(CreateMeasurement(child.TotalCount, child.BaseTags));

		return measurements;
	}

	private List<Measurement<double>> CollectSum() {
		var measurements = _sumMeasurements;
		measurements.Clear();

		foreach (var child in Volatile.Read(ref _all))
			lock (child.SyncRoot)
				if (child.TotalCount > 0)
					measurements.Add(CreateMeasurement(child.TotalSum, child.BaseTags));

		return measurements;
	}

	private List<Measurement<long>> CollectDropped() {
		var measurements = _droppedMeasurements;
		measurements.Clear();

		foreach (var child in Volatile.Read(ref _all))
			lock (child.SyncRoot)
				if (child.TotalDropped > 0)
					measurements.Add(CreateMeasurement(child.TotalDropped, child.BaseTags));

		return measurements;
	}

	// The public constructors copy the tags into a new array on every export; the children's arrays are built
	// once and never change, so they are handed over as they are.
	private static Measurement<T> CreateMeasurement<T>(T value, KeyValuePair<string, object?>[] tags) where T : struct {
		var m = new Measurement<T>(value);
		MeasurementAccessor<T>.Tags(ref m) = tags;
		return m;
	}

	private static class MeasurementAccessor<T> where T : struct {
		[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_tags")]
		public static extern ref KeyValuePair<string, object?>[] Tags(ref Measurement<T> measurement);
	}
}
