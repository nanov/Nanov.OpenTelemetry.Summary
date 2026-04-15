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

	private readonly Dictionary<TagList, (SnapshotResult Snapshot, TagList Tags)> _lastSnapshots = new(TagListComparer.Instance);
	private SnapshotResult _lastUntaggedSnapshot;
	private bool _hasUntaggedSnapshot;
	private readonly List<Measurement<double>> _measurements = [];

	private long _totalCount;

	internal Summary(Meter meter, string name, string? unit, string? description, SummaryOptions options) {
		_options = options;
		_untaggedChild = new SummaryChild(options);
		_buffer = new SwapBuffer<RecordEntry, Summary>(options.BufferCapacity, 0.75, this);

		meter.CreateObservableGauge(name, () => CollectQuantiles(), unit, description);

		if (options.ReportMax)
			meter.CreateObservableGauge($"{name}.max", () => CollectMax(), unit);

		if (options.ReportCount)
			meter.CreateObservableCounter($"{name}.count", CollectCount);

		if (options.ReportSum)
			meter.CreateObservableCounter($"{name}.sum", () => CollectSum(), unit);
	}

	public void Record(double value)
		=> _buffer.Write(new RecordEntry(value, default));

	public void Record(double value, in TagList tags)
		=> _buffer.Write(new RecordEntry(value, tags));

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

		if (_hasUntaggedSnapshot)
			for (var i = 0; i < quantiles.Length; i++)
				_measurements.Add(CreateMeasurement(qv[i], _untaggedChild.QuantileTagArrays[i]));

		foreach (var (key, child) in _children) {
			var snapshot = child.SnapshotAndReset(quantiles, qv);
			if (snapshot.Count == 0) continue;

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

	private long CollectCount() {
		var delta = 0L;
		if (_hasUntaggedSnapshot)
			delta += _lastUntaggedSnapshot.Count;

		foreach (var (key, _) in _children) {
			ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(_lastSnapshots, key);
			if (!Unsafe.IsNullRef(ref entry))
				delta += entry.Snapshot.Count;
		}

		_totalCount += delta;
		return _totalCount;
	}

	private List<Measurement<double>> CollectSum() {
		_measurements.Clear();

		if (_hasUntaggedSnapshot)
			_measurements.Add(new Measurement<double>(_lastUntaggedSnapshot.Sum));

		foreach (var (key, child) in _children) {
			ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(_lastSnapshots, key);
			if (!Unsafe.IsNullRef(ref entry))
				_measurements.Add(CreateMeasurement(entry.Snapshot.Sum, child.BaseTags));
		}

		return _measurements;
	}

	private static Measurement<double> CreateMeasurement(double value, KeyValuePair<string, object?>[] tags) {
		var m = new Measurement<double>(value);
		MeasurementTagsRef(ref m) = tags;
		return m;
	}

	[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_tags")]
	private static extern ref KeyValuePair<string, object?>[] MeasurementTagsRef(ref Measurement<double> measurement);
}
