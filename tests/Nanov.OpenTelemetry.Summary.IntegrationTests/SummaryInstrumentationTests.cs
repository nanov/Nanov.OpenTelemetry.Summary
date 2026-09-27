namespace Nanov.OpenTelemetry.Summary.IntegrationTests;

using System.Diagnostics.Metrics;
using global::OpenTelemetry;
using global::OpenTelemetry.Metrics;
using Nanov.OpenTelemetry.Summary.Instrumentation.AspNetCore;
using Nanov.OpenTelemetry.Summary.Instrumentation.Http;

public class SummaryInstrumentationTests {
	private const string AspNetCoreMeterName = "Microsoft.AspNetCore.Hosting";
	private const string ServerDurationName = "http.server.request.duration";
	private const string HttpClientMeterName = "System.Net.Http";
	private const string ClientDurationName = "http.client.request.duration";

	[Fact]
	public void Bridge_EmitsQuantileGauges_FromSourceHistogram() {
		var exported = new List<Metric>();

		using var sourceMeter = new Meter(AspNetCoreMeterName);
		var histogram = sourceMeter.CreateHistogram<double>(ServerDurationName, "s");

		using var provider = Sdk.CreateMeterProviderBuilder()
			.AddSummaryAspNetCoreInstrumentation(o => o.WithQuantiles(0.50, 0.99))
			.AddInMemoryExporter(exported)
			.Build();

		var random = new Random(42);
		for (var i = 0; i < 1000; i++)
			histogram.Record(random.NextDouble() * 100);

		provider!.ForceFlush();

		var summary = Assert.Single(exported, m => m.Name == ServerDurationName);
		Assert.Equal(MetricType.DoubleGauge, summary.MetricType);
		Assert.Equal("s", summary.Unit);

		var points = GetPoints(summary);
		var p50 = points.FirstOrDefault(p => Tag(p, "quantile") == "0.50");
		var p99 = points.FirstOrDefault(p => Tag(p, "quantile") == "0.99");
		Assert.NotNull(p50);
		Assert.NotNull(p99);
		Assert.InRange(p50!.Value, 35, 65);
		Assert.InRange(p99!.Value, 90, 100);
	}

	[Fact]
	public void Bridge_DoesNotExportSourceHistogramBuckets() {
		var exported = new List<Metric>();

		using var sourceMeter = new Meter(AspNetCoreMeterName);
		var histogram = sourceMeter.CreateHistogram<double>(ServerDurationName, "s");

		using var provider = Sdk.CreateMeterProviderBuilder()
			.AddSummaryAspNetCoreInstrumentation()
			.AddInMemoryExporter(exported)
			.Build();

		histogram.Record(10);
		histogram.Record(20);

		provider!.ForceFlush();

		// The source meter is never registered, so no histogram series may be exported.
		Assert.DoesNotContain(exported, m => m.MetricType == MetricType.Histogram);
	}

	[Fact]
	public void Bridge_ForwardsTags() {
		var exported = new List<Metric>();

		using var sourceMeter = new Meter(AspNetCoreMeterName);
		var histogram = sourceMeter.CreateHistogram<double>(ServerDurationName, "s");

		using var provider = Sdk.CreateMeterProviderBuilder()
			.AddSummaryAspNetCoreInstrumentation(o => o.WithQuantiles(0.50))
			.AddInMemoryExporter(exported)
			.Build();

		histogram.Record(10, new KeyValuePair<string, object?>("http.route", "/a"));
		histogram.Record(20, new KeyValuePair<string, object?>("http.route", "/b"));

		provider!.ForceFlush();

		var summary = Assert.Single(exported, m => m.Name == ServerDurationName);
		var points = GetPoints(summary);

		Assert.Contains(points, p => Tag(p, "http.route") == "/a");
		Assert.Contains(points, p => Tag(p, "http.route") == "/b");
	}

	[Fact]
	public void Bridge_CoexistsWithHistogramExportWhenSourceMeterAlsoRegistered() {
		var exported = new List<Metric>();

		using var sourceMeter = new Meter(AspNetCoreMeterName);
		var histogram = sourceMeter.CreateHistogram<double>(ServerDurationName, "s");

		using var provider = Sdk.CreateMeterProviderBuilder()
			.AddSummaryAspNetCoreInstrumentation(o => o.WithQuantiles(0.50))
			.AddMeter(AspNetCoreMeterName)
			.AddInMemoryExporter(exported)
			.Build();

		for (var i = 0; i < 100; i++)
			histogram.Record(i);

		provider!.ForceFlush();

		Assert.Contains(exported, m => m.Name == ServerDurationName && m.MetricType == MetricType.Histogram);
		Assert.Contains(exported, m => m.Name == ServerDurationName && m.MetricType == MetricType.DoubleGauge);
	}

	[Fact]
	public void HttpClientBridge_EmitsQuantileGauges_FromSourceHistogram() {
		var exported = new List<Metric>();

		using var sourceMeter = new Meter(HttpClientMeterName);
		var histogram = sourceMeter.CreateHistogram<double>(ClientDurationName, "s");

		using var provider = Sdk.CreateMeterProviderBuilder()
			.AddSummaryHttpClientInstrumentation(o => o.WithQuantiles(0.50, 0.99))
			.AddInMemoryExporter(exported)
			.Build();

		var random = new Random(42);
		for (var i = 0; i < 1000; i++)
			histogram.Record(random.NextDouble() * 100);

		provider!.ForceFlush();

		var summary = Assert.Single(exported, m => m.Name == ClientDurationName);
		Assert.Equal(MetricType.DoubleGauge, summary.MetricType);
		Assert.Equal("s", summary.Unit);
		Assert.DoesNotContain(exported, m => m.MetricType == MetricType.Histogram);
	}

	private sealed record Point(double Value, Dictionary<string, string> Tags);

	private static List<Point> GetPoints(Metric metric) {
		var points = new List<Point>();
		foreach (ref readonly var mp in metric.GetMetricPoints()) {
			var tags = new Dictionary<string, string>();
			foreach (var tag in mp.Tags)
				tags[tag.Key] = tag.Value?.ToString() ?? "";
			points.Add(new Point(mp.GetGaugeLastValueDouble(), tags));
		}
		return points;
	}

	private static string? Tag(Point point, string key)
		=> point.Tags.TryGetValue(key, out var val) ? val : null;
}
