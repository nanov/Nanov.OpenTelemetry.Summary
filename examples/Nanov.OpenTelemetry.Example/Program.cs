using System.Diagnostics;
using System.Diagnostics.Metrics;
using Nanov.OpenTelemetry.Summary;
using OpenTelemetry.Metrics;

var meter = new Meter("Summary.Example");

var summary = meter.CreateSummary("http.request.duration", "ms", configure:
	options => options
		.WithQuantiles(0.50, 0.95, 0.99)
		.WithMax()
		.WithCount());

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
	.WithMetrics(metrics => metrics
		.AddMeter("Summary.Example")
		.AddConsoleExporter((_, reader) =>
			reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000));

var app = builder.Build();

var random = new Random();

app.MapGet("/", () =>
{
	var latency = random.NextDouble() * 100;
	summary.Record(latency, new TagList {
		{ "endpoint", "/" },
		{ "method", "GET" },
		{ "status", "200" }
	});
	return $"OK (latency={latency:F1}ms)";
});

app.MapGet("/slow", () =>
{
	using var timer = summary.Time(new TagList {
		{ "endpoint", "/slow" },
		{ "method", "GET" },
		{ "status", "200" }
	});
	Thread.Sleep(random.Next(50, 200));
	return "Slow OK";
});

app.MapGet("/fire", () =>
{
	for (var i = 0; i < 1000; i++)
		summary.Record(random.NextDouble() * 500, new TagList {
			{ "endpoint", "/fire" },
			{ "method", "GET" },
			{ "status", "200" }
		});
	return "Fired 1000 observations";
});

app.Run();
