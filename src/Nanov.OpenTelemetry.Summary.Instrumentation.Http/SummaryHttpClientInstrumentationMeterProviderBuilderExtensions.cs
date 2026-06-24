namespace Nanov.OpenTelemetry.Summary.Instrumentation.Http;

using global::OpenTelemetry.Metrics;
using Nanov.OpenTelemetry.Summary.Instrumentation;

/// <summary>
/// Registers turnkey HttpClient summary instrumentation on a <see cref="MeterProviderBuilder"/>.
/// </summary>
public static class SummaryHttpClientInstrumentationMeterProviderBuilderExtensions {
	private const string MeterName = "System.Net.Http";
	private const string InstrumentName = "http.client.request.duration";

	/// <summary>
	/// Bridges the <c>HttpClient</c> <c>http.client.request.duration</c> histogram into a summary, emitting
	/// quantile gauges (tagged with <c>quantile</c>) under the same name instead of histogram buckets.
	/// </summary>
	public static MeterProviderBuilder AddSummaryHttpClientInstrumentation(
		this MeterProviderBuilder builder,
		Action<SummaryOptions>? configure = null)
		=> builder.AddSummaryBridge(MeterName, InstrumentName, InstrumentName, configure);
}
