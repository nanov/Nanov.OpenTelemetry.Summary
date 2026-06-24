namespace Nanov.OpenTelemetry.Summary.Instrumentation.AspNetCore;

using global::OpenTelemetry.Metrics;
using Nanov.OpenTelemetry.Summary.Instrumentation;

/// <summary>
/// Registers turnkey ASP.NET Core summary instrumentation on a <see cref="MeterProviderBuilder"/>.
/// </summary>
public static class SummaryAspNetCoreInstrumentationMeterProviderBuilderExtensions {
	private const string MeterName = "Microsoft.AspNetCore.Hosting";
	private const string InstrumentName = "http.server.request.duration";

	/// <summary>
	/// Bridges the ASP.NET Core <c>http.server.request.duration</c> histogram into a summary, emitting
	/// quantile gauges (tagged with <c>quantile</c>) under the same name instead of histogram buckets.
	/// </summary>
	public static MeterProviderBuilder AddSummaryAspNetCoreInstrumentation(
		this MeterProviderBuilder builder,
		Action<SummaryOptions>? configure = null)
		=> builder.AddSummaryBridge(MeterName, InstrumentName, InstrumentName, configure);
}
