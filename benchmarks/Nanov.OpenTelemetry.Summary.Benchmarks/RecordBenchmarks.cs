namespace Nanov.OpenTelemetry.Summary.Benchmarks;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using App.Metrics;
using App.Metrics.ReservoirSampling.ExponentialDecay;
using App.Metrics.Timer;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Reservoir;
using PromMetrics = Prometheus.Metrics;
using OtelSummary = Nanov.OpenTelemetry.Summary.Summary;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class RecordBenchmarks {
	private OtelSummary _otelSummary = null!;
	private IMetrics _appMetrics = null!;
	private TimerOptions _appMetricsTimer = null!;
	private Prometheus.Summary _promSummary = null!;
	private Prometheus.Summary.Child _promSummaryChild = null!;
	private Meter _meter = null!;
	private TagList _tags;

	[GlobalSetup]
	public void Setup() {
		_meter = new Meter("Benchmark");
		_otelSummary = _meter.CreateSummary("bench.duration", "ms", configure:
			o => o.WithQuantiles(0.50, 0.95, 0.99).WithMax().WithCount());

		_appMetrics = new MetricsBuilder()
			.Configuration.Configure(o => o.Enabled = true)
			.Build();

		_appMetricsTimer = new TimerOptions {
			Name = "bench.duration",
			DurationUnit = TimeUnit.Milliseconds,
			RateUnit = TimeUnit.Seconds,
			Reservoir = () => new DefaultForwardDecayingReservoir(1028, 0.015)
		};

		var promRegistry = PromMetrics.NewCustomRegistry();
		_promSummary = PromMetrics.WithCustomRegistry(promRegistry)
			.CreateSummary("prom_duration", "Benchmark", new Prometheus.SummaryConfiguration {
				Objectives = [
					new Prometheus.QuantileEpsilonPair(0.50, 0.01),
					new Prometheus.QuantileEpsilonPair(0.95, 0.01),
					new Prometheus.QuantileEpsilonPair(0.99, 0.001)
				],
				LabelNames = ["endpoint", "method", "status"]
			});
		_promSummaryChild = _promSummary.WithLabels("/api/test", "GET", "200");

		_tags = new TagList {
			{ "endpoint", "/api/test" },
			{ "method", "GET" },
			{ "status", "200" }
		};
	}

	[GlobalCleanup]
	public void Cleanup() => _meter.Dispose();

	[Benchmark(Baseline = true)]
	public void Summary_Record_NoTags() => _otelSummary.Record(42.5);

	[Benchmark]
	public void Summary_Record_WithTags() => _otelSummary.Record(42.5, _tags);

	[Benchmark]
	public void AppMetrics_Record_NoTags() => _appMetrics.Measure.Timer.Time(_appMetricsTimer, 42);

	[Benchmark]
	public void AppMetrics_Record_WithTags() {
		var metricTags = new MetricTags(
			new[] { "endpoint", "method", "status" },
			new[] { "/api/test", "GET", "200" });
		_appMetrics.Measure.Timer.Time(_appMetricsTimer, metricTags, 42);
	}

	[Benchmark]
	public void PrometheusNet_Record_NoLabels() => _promSummary.Observe(42.5);

	[Benchmark]
	public void PrometheusNet_Record_CachedChild() => _promSummaryChild.Observe(42.5);

	[Benchmark]
	public void PrometheusNet_Record_WithLabelsLookup() => _promSummary.WithLabels("/api/test", "GET", "200").Observe(42.5);
}

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class SnapshotBenchmarks {
	private OtelSummary _otelSummary = null!;
	private DefaultForwardDecayingReservoir _appMetricsReservoir = null!;
	private ExponentiallyDecayingReservoir _otelReservoir = null!;
	private Meter _meter = null!;

	[Params(100, 1000, 10_000)]
	public int SampleCount;

	[GlobalSetup]
	public void Setup() {
		_meter = new Meter("Benchmark.Snapshot");
		_otelSummary = _meter.CreateSummary("bench.snapshot", "ms", configure:
			o => o.WithQuantiles(0.50, 0.95, 0.99).WithMax().WithCount());

		_appMetricsReservoir = new DefaultForwardDecayingReservoir(1028, 0.015);
		_otelReservoir = new ExponentiallyDecayingReservoir();
	}

	[GlobalCleanup]
	public void Cleanup() => _meter.Dispose();

	[Benchmark(Baseline = true)]
	public void OtelReservoir_RecordAndSnapshot() {
		for (var i = 0; i < SampleCount; i++)
			_otelReservoir.Record(i * 0.5);

		Span<double> qv = stackalloc double[3];
		_otelReservoir.SnapshotAndReset([0.50, 0.95, 0.99], qv);
	}

	[Benchmark]
	public void AppMetrics_RecordAndSnapshot() {
		for (var i = 0; i < SampleCount; i++)
			_appMetricsReservoir.Update(i);

		_appMetricsReservoir.GetSnapshot(resetReservoir: true);
	}
}

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class ConcurrentRecordBenchmarks {
	private OtelSummary _otelSummary = null!;
	private IMetrics _appMetrics = null!;
	private TimerOptions _appMetricsTimer = null!;
	private Prometheus.Summary _promSummary = null!;
	private Meter _meter = null!;

	[Params(4, 8)]
	public int ThreadCount;

	[GlobalSetup]
	public void Setup() {
		_meter = new Meter("Benchmark.Concurrent");
		_otelSummary = _meter.CreateSummary("bench.concurrent", "ms", configure:
			o => o.WithQuantiles(0.50, 0.95, 0.99));

		_appMetrics = new MetricsBuilder()
			.Configuration.Configure(o => o.Enabled = true)
			.Build();

		_appMetricsTimer = new TimerOptions {
			Name = "bench.concurrent",
			DurationUnit = TimeUnit.Milliseconds,
			RateUnit = TimeUnit.Seconds,
			Reservoir = () => new DefaultForwardDecayingReservoir(1028, 0.015)
		};

		var promRegistry = PromMetrics.NewCustomRegistry();
		_promSummary = PromMetrics.WithCustomRegistry(promRegistry)
			.CreateSummary("prom_concurrent", "Benchmark", new Prometheus.SummaryConfiguration {
				Objectives = [
					new Prometheus.QuantileEpsilonPair(0.50, 0.01),
					new Prometheus.QuantileEpsilonPair(0.95, 0.01),
					new Prometheus.QuantileEpsilonPair(0.99, 0.001)
				]
			});
	}

	[GlobalCleanup]
	public void Cleanup() => _meter.Dispose();

	[Benchmark(Baseline = true)]
	public void Summary_ConcurrentRecord() {
		var tasks = new Task[ThreadCount];
		for (var t = 0; t < ThreadCount; t++)
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < 10_000; i++)
					_otelSummary.Record(i * 0.1);
			});
		Task.WaitAll(tasks);
	}

	[Benchmark]
	public void AppMetrics_ConcurrentRecord() {
		var tasks = new Task[ThreadCount];
		for (var t = 0; t < ThreadCount; t++)
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < 10_000; i++)
					_appMetrics.Measure.Timer.Time(_appMetricsTimer, i);
			});
		Task.WaitAll(tasks);
	}

	[Benchmark]
	public void PrometheusNet_ConcurrentRecord() {
		var tasks = new Task[ThreadCount];
		for (var t = 0; t < ThreadCount; t++)
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < 10_000; i++)
					_promSummary.Observe(i * 0.1);
			});
		Task.WaitAll(tasks);
	}
}
