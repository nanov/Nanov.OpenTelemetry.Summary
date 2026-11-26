# Nanov.OpenTelemetry.Summary

[![NuGet](https://img.shields.io/nuget/v/Nanov.OpenTelemetry.Summary.svg)](https://www.nuget.org/packages/Nanov.OpenTelemetry.Summary)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Nanov.OpenTelemetry.Summary.svg)](https://www.nuget.org/packages/Nanov.OpenTelemetry.Summary)
[![CI](https://github.com/nanov/Nanov.OpenTelemetry.Summary/actions/workflows/ci.yml/badge.svg)](https://github.com/nanov/Nanov.OpenTelemetry.Summary/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A Summary metric for OpenTelemetry .NET: quantiles (p95, p99, ...) computed in your app and exported as gauges through the standard OTLP pipeline.

## Why

OpenTelemetry .NET has no Summary type. A Histogram works, but every bucket boundary is its own time series. With 15 buckets across many pods and short export intervals, that gets expensive on backends like Coralogix. A summary emits one gauge per quantile instead.

## Install

Targets .NET 9, .NET 10 and .NET 11.

```bash
dotnet add package Nanov.OpenTelemetry.Summary
```

## Quick start

```csharp
using System.Diagnostics.Metrics;
using Nanov.OpenTelemetry.Summary;

var meter = new Meter("MyApp");
var summary = meter.CreateSummary("http.request.duration", unit: "ms");

summary.Record(42.5);

summary.Record(42.5, new TagList {
    { "endpoint", "/api/users" },
    { "method", "GET" }
});
```

The summary registers observable instruments on the meter, so any configured exporter picks them up.

Tags are compared as a set: `{a, b}` and `{b, a}` are the same series. Every tag set lives as long as the summary, so keep cardinality low: route templates, not ids.

## Choosing how to record

A tagged `Record` must find its series first: it hashes every tag key and value and looks the set up in a dictionary. That lookup is most of its cost. A bound handle does it once.

| Call | Cost (.NET 11, M4 Pro) | Use when |
|---|---|---|
| `summary.Record(value)` | ~4 ns | No tags. |
| `handle.Record(value)` from `summary.Bind(tags)` | ~5 ns | Tags known ahead of time: a fixed route, a queue name. Bind once and keep the handle. |
| `summary.Record(value, tags)` | ~45 ns with 3 tags | Tags known only per call: a status code, an outcome. |

```csharp
var getUser = summary.Bind(new TagList { { "endpoint", "/api/users" }, { "method", "GET" } });

getUser.Record(12.0);

using (getUser.Time()) {
    await ProcessRequest();
}

summary.Record(elapsed, new TagList { { "endpoint", "/api/users" }, { "status", statusCode } });
```

A handle is a small struct; copies record into the same series. Binding per call gains nothing over `Record(value, tags)`. None of the forms allocates (except once, for a new tag set's series) or waits on a lock, so cost doesn't grow with the number of recording threads.

## What gets reported

| Metric | Type | Default | Description |
|---|---|---|---|
| `{name}` | Gauge | on | Quantiles, tagged `quantile="0.95"`, `quantile="0.99"` |
| `{name}.count` | Counter | on | Number of values |
| `{name}.sum` | Counter | on | Sum of values |
| `{name}.max` | Gauge | opt-in | Largest value in the window |
| `{name}.min` | Gauge | opt-in | Smallest value in the window |
| `{name}.dropped` | Counter | when non-zero | Values left out of the quantiles' sample |

Quantiles, max and min cover the window since the previous export; a tag set with no values in it reports none. Count and sum are cumulative. Count, sum, max and min include dropped values; only the quantiles miss them. If `.dropped` grows, raise `WithBufferCapacity`.

Quantile labels use two decimals in the invariant culture (`0.95`, `0.50`) unless that would round: `0.999` stays `0.999`. NaN values are ignored.

## Configuration

```csharp
var summary = meter.CreateSummary("http.request.duration", "ms",
    description: "HTTP request latency",
    configure: options => options
        .WithQuantiles(0.50, 0.95, 0.99)   // default: 0.95, 0.99
        .WithMax()
        .WithMin()
        .WithoutSum()
        .WithoutCount()
        .WithBufferCapacity(8192)           // default: 4096, per tag set
        .WithReservoir(sampleSize: 2048, alpha: 0.015));
```

## Timing operations

Tags known up front:

```csharp
using (summary.Time(new TagList { { "endpoint", "/api/users" } }))
{
    await ProcessRequest();
}
```

Tags that depend on the outcome:

```csharp
var timer = summary.Time();
try
{
    await ProcessRequest();
    timer.Record(new TagList { { "status", "success" } });
}
catch
{
    timer.Record(new TagList { { "status", "failure" } });
    throw;
}
```

Don't mix the two: a timer in a `using` block that also calls `Record(tags)` records twice.

A timer resolves its starting tags when it starts, so disposing it costs a handle's `Record`. For a series you time often, `Bind(tags).Time()` on a kept handle skips the lookup at start too. `Duration()` is an alias for `Time()`.

## OTel pipeline setup

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp")
        .AddOtlpExporter());
```

## ASP.NET Core & HttpClient instrumentation

Companion packages turn the built-in .NET HTTP duration histograms into summaries, with an API that mirrors the OpenTelemetry contrib instrumentation:

```bash
dotnet add package Nanov.OpenTelemetry.Summary.Instrumentation.AspNetCore
dotnet add package Nanov.OpenTelemetry.Summary.Instrumentation.Http
```

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddSummaryAspNetCoreInstrumentation()
        .AddSummaryHttpClientInstrumentation(o => o.WithQuantiles(0.5, 0.95, 0.99))
        .AddOtlpExporter());
```

They listen to `http.server.request.duration` / `http.client.request.duration` and forward each measurement into a summary of the **same name**. The source meter is never registered with the `MeterProvider`, so the histogram is never aggregated or exported; only the summary is. Don't also call `AddAspNetCoreInstrumentation()` / `AddHttpClientInstrumentation()` unless you want both.

Both build on `Nanov.OpenTelemetry.Summary.Instrumentation`, whose `AddSummaryBridge(meterName, instrumentName, ...)` bridges any other histogram.

## Grafana / Coralogix queries

```promql
# p99 / p95 by endpoint, worst pod
max by (endpoint) (http_request_duration_ms{quantile="0.99"})
max by (endpoint) (http_request_duration_ms{quantile="0.95"})

# Requests per second by endpoint
sum by (endpoint) (rate(http_request_duration_ms_count[5m]))

# Average latency by endpoint
sum by (endpoint) (rate(http_request_duration_ms_sum[5m]))
/
sum by (endpoint) (rate(http_request_duration_ms_count[5m]))

# SLO alert: p99 above 500 ms (use with for: 5m)
max by (service) (http_request_duration_ms{quantile="0.99"}) > 500
```

Quantiles can't be averaged across pods, so aggregate them with `max`: if any pod breaches, you see it.

## How it works

`Record` finds the tag set's series with a lock-free lookup that hashes the caller's tags in place, then stores the value in that series' buffer with one atomic add. The buffer has one stripe per core, so threads don't contend; a stripe allocates its memory on first use, so a quiet tag set stays small.

When a stripe is three-quarters full, the series queues itself on the thread pool, which moves the values into an exponentially decaying reservoir (EDR): up to 1028 samples by default, biased toward recent values.

At export, the quantile gauge drains what is left, computes weighted quantiles and starts a new window; the other instruments report from that same window.

A value that finds both halves of its stripe full skips the sample but still counts in `.count`, `.sum`, `.max` and `.min`, and in `.dropped`.

## Performance

Apple M4 Pro, BenchmarkDotNet short job, with a `MeterListener` attached so the summary is enabled. Nothing allocates.

| Operation | .NET 9 | .NET 11 |
|---|---|---|
| `Record`, no tags | 5.7 ns | 4.4 ns |
| `Record`, 3 tags | 44 ns | 45 ns |
| Bound handle `Record` | 5.9 ns | 4.5 ns |
| 4 threads × 10,000 records | 158 µs | 150 µs |
| 8 threads × 10,000 records | 216 µs | 270 µs |
| Reservoir: 1,000 records + snapshot | 55 µs | 58 µs |

Against the previous release on .NET 9: the concurrent runs dropped from 985 µs and 2,561 µs, the reservoir from 185 µs. A tagged `Record` used to cost ~4 ns because it only copied the tags and left the lookup to one aggregation thread, which fell behind and discarded values without counting them. The lookup now runs on the caller's thread; see [Choosing how to record](#choosing-how-to-record).

Tight loops record faster than the thread pool drains, so part of each figure is values overflowing into `.dropped`.

Comparisons with prometheus-net and AppMetrics: [`RESULTS.MD`](benchmarks/Nanov.OpenTelemetry.Summary.Benchmarks/RESULTS.MD).

```bash
cd benchmarks/Nanov.OpenTelemetry.Summary.Benchmarks
dotnet run -c Release -f net9.0 -- --filter '*'
dotnet run -c Release -f net11.0 -- --filter '*' --inProcess   # BenchmarkDotNet 0.15 doesn't know .NET 11 yet
```

## License

MIT
