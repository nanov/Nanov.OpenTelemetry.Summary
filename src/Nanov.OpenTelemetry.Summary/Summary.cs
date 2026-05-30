namespace Nanov.OpenTelemetry.Summary;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Internal;
using Reservoir;

public sealed class Summary : IBufferConsumer<RecordEntry> {
	private readonly SwapBuffer<RecordEntry, Summary> _buffer;
	private readonly Dictionary<TagList, SummaryChild> _children = new(TagListComparer.Instance);
	private readonly SummaryOptions _options;
	private readonly SummaryChild _untaggedChild;
	private readonly ObservableGauge<double> _quantileGauge;

	private readonly Dictionary<TagList, (SnapshotResult Snapshot, TagList Tags)> _lastSnapshots = new(TagListComparer.Instance);
	private SnapshotResult _lastUntaggedSnapshot;
	private bool _hasUntaggedSnapshot;
	private readonly List<Measurement<double>> _measurements = [];
	private readonly List<Measurement<long>> _countMeasurements = [];

	internal Summary(Meter meter, string name, string? unit, string? description, SummaryOptions options) {
		_options = options;
		_untaggedChild = new SummaryChild(options);
		_buffer = new SwapBuffer<RecordEntry, Summary>(options.BufferCapacity, 0.75, this);

		_quantileGauge = meter.CreateObservableGauge(name, () => CollectQuantiles(), unit, description);

		if (options.ReportMax)
			meter.CreateObservableGauge($"{name}.max", () => CollectMax(), unit);

		if (options.ReportCount)
			meter.CreateObservableCounter($"{name}.count", () => CollectCount());

		if (options.ReportSum)
			meter.CreateObservableCounter($"{name}.sum", () => CollectSum(), unit);
	}

	public bool IsEnabled => _quantileGauge.Enabled;

	public void Record(double value) {
		if (!_quantileGauge.Enabled) return;
		_buffer.Write(new RecordEntry(value, default));
	}

	public void Record(double value, in TagList tags) {
		if (!_quantileGauge.Enabled) return;
		_buffer.Write(new RecordEntry(value, tags));
	}

	public void Record(double value, params ReadOnlySpan<KeyValuePair<string, object?>> tags)
		=> Record(value, new TagList(tags));

	public SummaryTimer Time() => new(this, default);
	public SummaryTimer Time(in TagList tags) => new(this, tags);
	public SummaryTimer Duration() => new(this, default);
	public SummaryTimer Duration(in TagList tags) => new(this, tags);

	void IBufferConsumer<RecordEntry>.Consume(ReadOnlySpan<RecordEntry> entries) {
		foreach (ref readonly var entry in entries) {
			var child = GetOrCreateChild(entry.Tags);
			child.Record(entry.Value);
		}
	}

	private SummaryChild GetOrCreateChild(in TagList tags) {
		if (tags.Count == 0)
			return _untaggedChild;

		if (!_children.TryGetValue(tags, out var child)) {
			child = new SummaryChild(_options, tags);
			_children[tags] = child;
		}
		return child;
	}


	private List<Measurement<double>> CollectQuantiles() {
		_buffer.DrainForSnapshot();

		var quantiles = _options.Quantiles;
		_measurements.Clear();

		Span<double> qv = stackalloc double[quantiles.Length];

		var untagged = _untaggedChild.SnapshotAndReset(quantiles, qv);
		_lastUntaggedSnapshot = untagged;
		_hasUntaggedSnapshot = untagged.Count > 0;
		_untaggedChild.CumulativeCount += untagged.Count;
		_untaggedChild.CumulativeSum += untagged.Sum;

		if (_hasUntaggedSnapshot)
			for (var i = 0; i < quantiles.Length; i++)
				_measurements.Add(CreateMeasurement(qv[i], _untaggedChild.QuantileTagArrays[i]));

		foreach (var (key, child) in _children) {
			var snapshot = child.SnapshotAndReset(quantiles, qv);
			if (snapshot.Count == 0) continue;

			child.CumulativeCount += snapshot.Count;
			child.CumulativeSum += snapshot.Sum;

			ref var snapshotEntry = ref CollectionsMarshal.GetValueRefOrAddDefault(_lastSnapshots, key, out _);
			snapshotEntry = (snapshot, key);

			for (var i = 0; i < quantiles.Length; i++)
				_measurements.Add(CreateMeasurement(qv[i], child.QuantileTagArrays[i]));
		}

		return _measurements;
	}

	private List<Measurement<double>> CollectMax() {
		_measurements.Clear();

		if (_hasUntaggedSnapshot)
			_measurements.Add(new Measurement<double>(_lastUntaggedSnapshot.Max));

		foreach (var (key, child) in _children) {
			ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(_lastSnapshots, key);
			if (!Unsafe.IsNullRef(ref entry))
				_measurements.Add(CreateMeasurement(entry.Snapshot.Max, child.BaseTags));
		}

		return _measurements;
	}

	// Cumulative, per-child tagged totals (a child only ever grows), mirroring sum/max so the counter
	// can be grouped and filtered by the recorded tags (e.g. http_route, status). Children with no
	// recorded value are skipped; the untagged child emits a tagless series.
	private List<Measurement<long>> CollectCount() {
		_countMeasurements.Clear();

		if (_untaggedChild.CumulativeCount > 0)
			_countMeasurements.Add(new Measurement<long>(_untaggedChild.CumulativeCount));

		foreach (var (_, child) in _children)
			if (child.CumulativeCount > 0)
				_countMeasurements.Add(CreateCountMeasurement(child.CumulativeCount, child.BaseTags));

		return _countMeasurements;
	}

	private List<Measurement<double>> CollectSum() {
		_measurements.Clear();

		if (_untaggedChild.CumulativeCount > 0)
			_measurements.Add(new Measurement<double>(_untaggedChild.CumulativeSum));

		foreach (var (_, child) in _children)
			if (child.CumulativeCount > 0)
				_measurements.Add(CreateMeasurement(child.CumulativeSum, child.BaseTags));

		return _measurements;
	}

	private static Measurement<double> CreateMeasurement(double value, KeyValuePair<string, object?>[] tags) {
		var m = new Measurement<double>(value);
		MeasurementTagsRef(ref m) = tags;
		return m;
	}

	private static Measurement<long> CreateCountMeasurement(long value, KeyValuePair<string, object?>[] tags) {
		var m = new Measurement<long>(value);
		MeasurementCountTagsRef(ref m) = tags;
		return m;
	}

	[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_tags")]
	private static extern ref KeyValuePair<string, object?>[] MeasurementTagsRef(ref Measurement<double> measurement);

	[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_tags")]
	private static extern ref KeyValuePair<string, object?>[] MeasurementCountTagsRef(ref Measurement<long> measurement);
}
