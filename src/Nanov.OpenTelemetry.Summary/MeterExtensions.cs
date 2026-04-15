namespace Nanov.OpenTelemetry.Summary;

using System.Diagnostics.Metrics;

public static class MeterExtensions {
	public static Summary CreateSummary(
		this Meter meter,
		string name,
		string? unit = null,
		string? description = null,
		Action<SummaryOptions>? configure = null) {
		var options = new SummaryOptions();
		configure?.Invoke(options);
		return new Summary(meter, name, unit, description, options);
	}
}
