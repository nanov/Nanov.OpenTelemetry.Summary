namespace Nanov.OpenTelemetry.Summary.Instrumentation;

using System.Diagnostics.Metrics;

/// <summary>
/// Bridges a single source histogram instrument (e.g. <c>http.server.request.duration</c>)
/// into a <see cref="Summary"/> quantile gauge. A private <see cref="MeterListener"/> subscribes
/// to the source instrument and forwards every measurement to the summary. The source meter is
/// never registered with the <c>MeterProvider</c>, so the original histogram is observed but never
/// aggregated into buckets or exported — only the summary's gauges are.
/// </summary>
public sealed class SummaryBridge : IDisposable {
	internal const string MeterName = "Nanov.OpenTelemetry.Summary.Instrumentation";

	private readonly string _sourceMeterName;
	private readonly string _sourceInstrumentName;
	private readonly string _outputName;
	private readonly Action<SummaryOptions>? _configure;
	private readonly Meter _meter;
	private readonly MeterListener _listener;

	private Summary? _summary;

	public SummaryBridge(
		string sourceMeterName,
		string sourceInstrumentName,
		string outputName,
		Action<SummaryOptions>? configure) {
		_sourceMeterName = sourceMeterName;
		_sourceInstrumentName = sourceInstrumentName;
		_outputName = outputName;
		_configure = configure;
		_meter = new Meter(MeterName);

		_listener = new MeterListener {
			InstrumentPublished = OnInstrumentPublished,
		};
		_listener.SetMeasurementEventCallback<double>(OnMeasurement);
		_listener.Start();
	}

	private void OnInstrumentPublished(Instrument instrument, MeterListener listener) {
		if (instrument.Meter.Name != _sourceMeterName || instrument.Name != _sourceInstrumentName)
			return;

		// Create the summary lazily so we can copy the source instrument's unit verbatim (no value conversion).
		_summary ??= _meter.CreateSummary(_outputName, instrument.Unit, instrument.Description, _configure);
		listener.EnableMeasurementEvents(instrument);
	}

	private void OnMeasurement(
		Instrument instrument,
		double measurement,
		ReadOnlySpan<KeyValuePair<string, object?>> tags,
		object? state)
		=> _summary?.Record(measurement, tags);

	public void Dispose() {
		_listener.Dispose();
		_meter.Dispose();
	}
}
