# Design: Nanov.OpenTelemetry.Summary.Instrumentation

**Date:** 2026-05-30
**Status:** Approved (pending user spec review)

## Summary

A new NuGet package, `Nanov.OpenTelemetry.Summary.Instrumentation`, that bridges
the built-in .NET HTTP duration **histograms** (ASP.NET Core server + `HttpClient`)
into `Nanov.OpenTelemetry.Summary` **quantile gauges**, without exporting the
original histograms. Adoption mirrors the OpenTelemetry contrib packages:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddSummaryAspNetCoreInstrumentation()
        .AddSummaryHttpClientInstrumentation(o => o.WithQuantiles(0.5, 0.95, 0.99))
        .AddOtlpExporter());
```

## Motivation

Modern .NET (8+) already emits `http.server.request.duration` and
`http.client.request.duration` as histograms from built-in meters. As noted in
the core README, histograms produce one time series **per bucket boundary**,
which is expensive at fleet scale on backends like Coralogix. This package lets
users get tail-latency percentiles for the standard HTTP metrics through the
existing Summary machinery (client-side quantiles emitted as a handful of
gauges) instead of paying the histogram bucket cost — with a drop-in API that
looks just like the contrib instrumentation they already know.

## Key mechanism: subscribe without exporting the histogram

An instrument and its export are decoupled:

- ASP.NET Core / `HttpClient` **always create** their histogram instruments on
  their meters. The instrument existing costs nothing on its own.
- A histogram only becomes an exported, billable time series if a
  **`MeterProvider`** is told to collect its meter (via
  `AddAspNetCoreInstrumentation()` / `AddMeter(...)`).
- A plain **`MeterListener`** can subscribe to the same instrument completely
  independently of the SDK.

So the bridge:

1. Attaches its own `MeterListener` to the source `(meter, instrument)` and feeds
   every measurement into a `Summary`.
2. Does **not** register the source meter with the `MeterProvider`, so the
   histogram is never aggregated into buckets and never exported.
3. Exports only the Summary's quantile gauges.

**Cost retained:** an instrument is `Enabled` while any listener is attached, so
ASP.NET Core still performs its per-request tag extraction (route, status,
method). That work is desired — those tags flow into the Summary.

**Coexistence:** if the user *also* calls `AddAspNetCoreInstrumentation()`, both
listeners fire and both histogram and summary export. This is acceptable opt-in
behavior, not the default.

## Architecture

### New project

`src/Nanov.OpenTelemetry.Summary.Instrumentation/Nanov.OpenTelemetry.Summary.Instrumentation.csproj`

- **TargetFrameworks:** `net8.0;net9.0;net10.0`
- **PackageId:** `Nanov.OpenTelemetry.Summary.Instrumentation`
- **Dependencies:**
  - `ProjectReference` → core `Nanov.OpenTelemetry.Summary`
  - `PackageReference` → `OpenTelemetry` (SDK `1.11.2`, for `MeterProviderBuilder`
    / `AddInstrumentation`). The core package currently also references this SDK
    (and PR #1 proposes dropping it from core); regardless of that outcome, this
    instrumentation package legitimately needs the SDK because its whole purpose
    is `MeterProviderBuilder` extension methods.
- Same packaging metadata conventions as the core csproj (authors, license,
  symbols, tags).
- Added to the solution.

### Internal component: `SummaryBridge`

`internal sealed class SummaryBridge : IDisposable`

Responsibilities (single purpose: bridge one histogram instrument to one Summary):

- **Construction inputs:** source meter name, source instrument name, output
  metric name, and an optional `Action<SummaryOptions>`.
- Creates its **own `Meter`** (e.g. named `Nanov.OpenTelemetry.Summary.Instrumentation`).
- Creates a `Summary` on that meter via `meter.CreateSummary(outputName, unit,
  description, configure)` — which registers the quantile observable gauges.
- Creates and starts a `MeterListener`:
  - `InstrumentPublished`: enable the listener for the instrument **only** when
    `instrument.Meter.Name == sourceMeterName && instrument.Name ==
    sourceInstrumentName`. Capture the source instrument's **unit** here so the
    Summary reports the same unit (seconds) — no value conversion.
  - `SetMeasurementEventCallback<double>`: forward `(value, tags)` straight to
    `summary.Record(value, tags)`. Tags are passed through unchanged.
- `Dispose()` disposes the `MeterListener` and the `Meter`.

Note: the Summary's output `Meter` must be added to the `MeterProvider` so its
gauges export. Because the helpers run inside the `MeterProviderBuilder`, they
call `AddMeter(bridgeMeterName)` as part of registration.

### Lifecycle

Each helper registers the bridge with OTel's idiomatic ownership model:

```csharp
return builder
    .AddMeter(BridgeMeterName)
    .AddInstrumentation(() => new SummaryBridge(
        sourceMeter, sourceInstrument, outputName, configure));
```

`AddInstrumentation` makes the `MeterProvider` own and dispose the bridge (and
thus its `MeterListener` + `Meter`).

### Public API

`public static class SummaryInstrumentationMeterProviderBuilderExtensions`

```csharp
public static MeterProviderBuilder AddSummaryAspNetCoreInstrumentation(
    this MeterProviderBuilder builder,
    Action<SummaryOptions>? configure = null);

public static MeterProviderBuilder AddSummaryHttpClientInstrumentation(
    this MeterProviderBuilder builder,
    Action<SummaryOptions>? configure = null);
```

Hardcoded source identifiers (stable across net8/9/10):

| Helper | Source meter | Source instrument | Output name |
|---|---|---|---|
| `AddSummaryAspNetCoreInstrumentation` | `Microsoft.AspNetCore.Hosting` | `http.server.request.duration` | `http.server.request.duration` (same) |
| `AddSummaryHttpClientInstrumentation` | `System.Net.Http` | `http.client.request.duration` | `http.client.request.duration` (same) |

The generic `SummaryBridge` stays **internal** for v1. A public
`AddSummaryBridge(meterName, instrumentName, ...)` for arbitrary histograms is a
natural future extension but is YAGNI now.

## Data flow

```
HTTP request completes
  -> ASP.NET Core records http.server.request.duration (histogram) [NOT exported]
  -> our MeterListener callback fires with (durationSeconds, tags)
  -> summary.Record(durationSeconds, tags)
  -> [export interval] Summary observable gauges produce quantile measurements
  -> OTLP exporter sends http.server.request.duration{quantile="0.99", ...}
```

## Naming & output

- Output metric uses the **same name** as the source instrument, distinguished
  by the `quantile="..."` tag the Summary already adds. Since the histogram is
  never exported, there is no collision, and dashboards/queries that don't rely
  on histogram buckets keep working (drop-in).
- Unit is copied from the source instrument (seconds), so no value scaling.

## Scope (v1)

**In scope:**
- Built-in .NET 8/9/10 meters/instruments only (names above).
- Two turnkey helpers + internal generic bridge.
- Pass-through of tags and unit.
- `SummaryOptions` forwarded to the underlying `Summary`.

**Out of scope (noted for later):**
- Legacy contrib instrument names (e.g. `http.server.duration`).
- Tag filtering / cardinality reduction.
- Public generic `AddSummaryBridge` API.
- Pre-.NET-8 frameworks.

## Testing

Integration-style tests in a new test project (or an added file in the existing
integration test project), mirroring `OtelPipelineTests`:

- Create a `Meter` named exactly like the real source (e.g.
  `Microsoft.AspNetCore.Hosting`) with a `Histogram<double>` named
  `http.server.request.duration`.
- Build a `MeterProvider` with the helper + an InMemory exporter.
- Record several values (with tags); force a collect/flush.
- Assert:
  - Quantile **gauge** measurements are exported under the source name with the
    expected `quantile` tags and forwarded tags.
  - No histogram bucket series are exported (the source meter is not collected).
  - Unit matches the source instrument.
- A coexistence test: when both the helper and a normal `AddMeter(source)` are
  registered, both summary and histogram export.

## Risks / dependencies

- **Instrument name stability:** relies on the built-in instrument names being
  stable across net8/9/10. They are part of OTel HTTP semantic conventions and
  considered stable.
