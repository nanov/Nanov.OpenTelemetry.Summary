namespace Nanov.OpenTelemetry.Summary.Instrumentation;

using global::OpenTelemetry.Metrics;

/// <summary>
/// Generic primitive for bridging any source histogram instrument into a <see cref="Summary"/>
/// quantile gauge. The ASP.NET Core and HttpClient instrumentation packages build on this.
/// </summary>
public static class SummaryBridgeMeterProviderBuilderExtensions {
	/// <summary>
	/// Bridges the histogram identified by <paramref name="sourceMeterName"/> /
	/// <paramref name="sourceInstrumentName"/> into a <see cref="Summary"/>. The summary's quantile
	/// gauges are emitted under <paramref name="outputName"/> (defaults to the source instrument name).
	/// The source meter is not registered with the provider, so the original histogram is observed but
	/// never aggregated into buckets or exported.
	/// </summary>
	public static MeterProviderBuilder AddSummaryBridge(
		this MeterProviderBuilder builder,
		string sourceMeterName,
		string sourceInstrumentName,
		string? outputName = null,
		Action<SummaryOptions>? configure = null)
		=> builder
			.AddMeter(SummaryBridge.MeterName)
			.AddInstrumentation(() => new SummaryBridge(
				sourceMeterName, sourceInstrumentName, outputName ?? sourceInstrumentName, configure));
}
