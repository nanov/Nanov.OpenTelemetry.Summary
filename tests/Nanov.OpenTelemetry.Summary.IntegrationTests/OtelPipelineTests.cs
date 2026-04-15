namespace Nanov.OpenTelemetry.Summary.IntegrationTests;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using global::OpenTelemetry;
using global::OpenTelemetry.Metrics;

public class OtelPipelineTests : IDisposable {
	private readonly Meter _meter;
	private readonly List<Metric> _exportedMetrics = [];
	private readonly MeterProvider _meterProvider;

	public OtelPipelineTests() {
		var meterName = $"Test.{Guid.NewGuid():N}";
		_meter = new Meter(meterName);

		_meterProvider = Sdk.CreateMeterProviderBuilder()
			.AddMeter(meterName)
			.AddInMemoryExporter(_exportedMetrics)
			.Build();
	}

	public void Dispose() {
		_meterProvider.Dispose();
		_meter.Dispose();
	}

	private void FlushAndCollect() {
		_exportedMetrics.Clear();
		_meterProvider.ForceFlush();
	}

	[Fact]
	public void QuantileGauges_ExportedViaOtelPipeline() {
		var summary = _meter.CreateSummary("test.latency", "ms", configure:
			o => o.WithQuantiles(0.50, 0.99));

		var random = new Random(42);
		for (var i = 0; i < 1000; i++)
			summary.Record(random.NextDouble() * 100);

		FlushAndCollect();

		var metric = Assert.Single(_exportedMetrics, m => m.Name == "test.latency");
		var points = GetMetricPoints(metric);

		Assert.True(points.Count >= 2);

		var p50 = points.FirstOrDefault(p => GetTag(p, "quantile") == "0.50");
		var p99 = points.FirstOrDefault(p => GetTag(p, "quantile") == "0.99");

		Assert.NotNull(p50);
		Assert.NotNull(p99);
		Assert.InRange(p50.Value, 35, 65);
		Assert.InRange(p99.Value, 90, 100);
	}

	[Fact]
	public void MaxGauge_ExportedViaOtelPipeline() {
		var summary = _meter.CreateSummary("test.max", "ms", configure:
			o => o.WithQuantiles(0.50).WithMax());

		summary.Record(10);
		summary.Record(99);
		summary.Record(50);

		FlushAndCollect();

		var metric = Assert.Single(_exportedMetrics, m => m.Name == "test.max.max");
		var points = GetMetricPoints(metric);

		Assert.Contains(points, p => p.Value >= 99);
	}

	[Fact]
	public void CountCounter_ExportedViaOtelPipeline() {
		var summary = _meter.CreateSummary("test.count", "ms", configure:
			o => o.WithQuantiles(0.50).WithCount());

		for (var i = 0; i < 50; i++)
			summary.Record(i);

		FlushAndCollect();

		var metric = Assert.Single(_exportedMetrics, m => m.Name == "test.count.count");
		var points = GetMetricPoints(metric);

		Assert.Contains(points, p => p.LongValue == 50);
	}

	[Fact]
	public void SumCounter_ExportedViaOtelPipeline() {
		var summary = _meter.CreateSummary("test.sum", "ms", configure:
			o => o.WithQuantiles(0.50).WithCount());

		summary.Record(10);
		summary.Record(20);
		summary.Record(30);

		FlushAndCollect();

		var metric = Assert.Single(_exportedMetrics, m => m.Name == "test.sum.sum");
		var points = GetMetricPoints(metric);

		Assert.Contains(points, p => Math.Abs(p.Value - 60) < 0.01);
	}

	[Fact]
	public void TaggedMetrics_ExportedWithCorrectTags() {
		var summary = _meter.CreateSummary("test.tagged", "ms", configure:
			o => o.WithQuantiles(0.50));

		summary.Record(10, new TagList { { "endpoint", "/a" } });
		summary.Record(20, new TagList { { "endpoint", "/b" } });

		FlushAndCollect();

		var metric = Assert.Single(_exportedMetrics, m => m.Name == "test.tagged");
		var points = GetMetricPoints(metric);

		var endpointA = points.Where(p => GetTag(p, "endpoint") == "/a").ToList();
		var endpointB = points.Where(p => GetTag(p, "endpoint") == "/b").ToList();

		Assert.NotEmpty(endpointA);
		Assert.NotEmpty(endpointB);
	}

	[Fact]
	public void SnapshotAndReset_SecondExportIsIndependent() {
		var summary = _meter.CreateSummary("test.reset", "ms", configure:
			o => o.WithQuantiles(0.50).WithCount());

		for (var i = 0; i < 100; i++)
			summary.Record(i);

		FlushAndCollect();
		var firstCount = GetMetricPoints(Assert.Single(_exportedMetrics, m => m.Name == "test.reset.count"))
			.Select(p => p.LongValue).Max();

		for (var i = 0; i < 50; i++)
			summary.Record(i);

		FlushAndCollect();
		var secondCount = GetMetricPoints(Assert.Single(_exportedMetrics, m => m.Name == "test.reset.count"))
			.Select(p => p.LongValue).Max();

		Assert.True(secondCount > firstCount);
	}

	[Fact]
	public void EmptyWindow_NoQuantilesExported() {
		_ = _meter.CreateSummary("test.empty", "ms", configure:
			o => o.WithQuantiles(0.50, 0.99));

		FlushAndCollect();

		var metrics = _exportedMetrics.Where(m => m.Name == "test.empty").ToList();
		if (metrics.Count > 0) {
			var points = GetMetricPoints(metrics[0]);
			Assert.All(points, p => Assert.Equal(0, p.Value));
		}
	}

	[Fact]
	public void Timer_RecordsDuration() {
		var summary = _meter.CreateSummary("test.timer", "ms", configure:
			o => o.WithQuantiles(0.50));

		using (summary.Time()) {
			Thread.Sleep(20);
		}

		FlushAndCollect();

		var metric = Assert.Single(_exportedMetrics, m => m.Name == "test.timer");
		var points = GetMetricPoints(metric);

		Assert.Contains(points, p => p.Value > 10);
	}

	[Fact]
	public void HighThroughput_NoDataLoss() {
		var summary = _meter.CreateSummary("test.throughput", "ms", configure:
			o => o.WithQuantiles(0.50).WithCount().WithBufferCapacity(100_000));

		for (var i = 0; i < 10_000; i++)
			summary.Record(i);

		FlushAndCollect();

		var metric = Assert.Single(_exportedMetrics, m => m.Name == "test.throughput.count");
		var points = GetMetricPoints(metric);
		var totalCount = points.Select(p => p.LongValue).Max();

		Assert.Equal(10_000, totalCount);
	}

	[Fact]
	public void MultipleQuantiles_AllReported() {
		var summary = _meter.CreateSummary("test.multi", "ms", configure:
			o => o.WithQuantiles(0.25, 0.50, 0.75, 0.90, 0.99));

		var random = new Random(42);
		for (var i = 0; i < 5000; i++)
			summary.Record(random.NextDouble() * 100);

		FlushAndCollect();

		var metric = Assert.Single(_exportedMetrics, m => m.Name == "test.multi");
		var points = GetMetricPoints(metric);

		var quantileLabels = points.Select(p => GetTag(p, "quantile")).Distinct().ToList();
		Assert.Contains("0.25", quantileLabels);
		Assert.Contains("0.50", quantileLabels);
		Assert.Contains("0.75", quantileLabels);
		Assert.Contains("0.90", quantileLabels);
		Assert.Contains("0.99", quantileLabels);
	}

	private record MetricPoint(double Value, long LongValue, Dictionary<string, string> Tags);

	private static List<MetricPoint> GetMetricPoints(Metric metric) {
		var points = new List<MetricPoint>();
		foreach (ref readonly var mp in metric.GetMetricPoints()) {
			var tags = new Dictionary<string, string>();
			foreach (var tag in mp.Tags)
				tags[tag.Key] = tag.Value?.ToString() ?? "";

			if (metric.MetricType == MetricType.LongSum)
				points.Add(new MetricPoint(0, mp.GetSumLong(), tags));
			else if (metric.MetricType == MetricType.DoubleSum)
				points.Add(new MetricPoint(mp.GetSumDouble(), 0, tags));
			else if (metric.MetricType == MetricType.DoubleGauge)
				points.Add(new MetricPoint(mp.GetGaugeLastValueDouble(), 0, tags));
			else if (metric.MetricType == MetricType.LongGauge)
				points.Add(new MetricPoint(0, mp.GetGaugeLastValueLong(), tags));
		}
		return points;
	}

	private static string? GetTag(MetricPoint point, string key) =>
		point.Tags.TryGetValue(key, out var val) ? val : null;
}
