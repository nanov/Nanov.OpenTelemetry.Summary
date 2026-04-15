namespace Nanov.OpenTelemetry.Summary.Tests.Summary;

using System.Diagnostics;
using System.Diagnostics.Metrics;

public class SummaryTests : IDisposable {
	private readonly Meter _meter = new("Summary.Tests." + Guid.NewGuid().ToString("N"));

	public void Dispose() => _meter.Dispose();

	[Fact]
	public void Record_WithoutTags_Works() {
		var summary = _meter.CreateSummary("test.metric", "ms", configure:
			o => o.WithQuantiles(0.50, 0.99).WithMax().WithCount());

		for (var i = 0; i < 100; i++)
			summary.Record(i);

		var measurements = CollectMeasurements("test.metric");
		Assert.NotEmpty(measurements);
	}

	[Fact]
	public void Record_WithTags_CreatesChildren() {
		var summary = _meter.CreateSummary("test.tagged", "ms", configure:
			o => o.WithQuantiles(0.50));

		summary.Record(10, new TagList { { "endpoint", "/a" } });
		summary.Record(20, new TagList { { "endpoint", "/b" } });

		var measurements = CollectMeasurements("test.tagged");
		Assert.True(measurements.Count >= 2);
	}

	[Fact]
	public void Record_WithKeyValuePair_Works() {
		var summary = _meter.CreateSummary("test.kvp", "ms", configure:
			o => o.WithQuantiles(0.50));

		summary.Record(42, new KeyValuePair<string, object?>("key", "val"));

		var measurements = CollectMeasurements("test.kvp");
		Assert.NotEmpty(measurements);
	}

	[Fact]
	public void Record_WithTwoKeyValuePairs_Works() {
		var summary = _meter.CreateSummary("test.kvp2", "ms", configure:
			o => o.WithQuantiles(0.50));

		summary.Record(42,
			new KeyValuePair<string, object?>("k1", "v1"),
			new KeyValuePair<string, object?>("k2", "v2"));

		var measurements = CollectMeasurements("test.kvp2");
		Assert.NotEmpty(measurements);
	}

	[Fact]
	public void WithMax_ReportsMaxGauge() {
		var summary = _meter.CreateSummary("test.max", "ms", configure:
			o => o.WithQuantiles(0.50).WithMax());

		summary.Record(10);
		summary.Record(99);

		var all = CollectAllMeasurements();
		Assert.True(all.ContainsKey("test.max.max"));
		Assert.NotEmpty(all["test.max.max"]);
	}

	[Fact]
	public void WithCount_ReportsCountAndSum() {
		var summary = _meter.CreateSummary("test.count", "ms", configure:
			o => o.WithQuantiles(0.50).WithCount());

		summary.Record(10);
		summary.Record(20);

		var all = CollectAllMeasurements();
		Assert.True(all.TryGetValue("test.count.count", out var counts));
		Assert.NotEmpty(counts);
	}

	[Fact]
	public void Time_RecordsElapsed() {
		var summary = _meter.CreateSummary("test.timer", "ms", configure:
			o => o.WithQuantiles(0.50));

		using (summary.Time()) {
			Thread.Sleep(10);
		}

		var measurements = CollectMeasurements("test.timer");
		Assert.NotEmpty(measurements);
		Assert.True(measurements[0].value > 0);
	}

	[Fact]
	public void Time_WithTags_RecordsElapsed() {
		var summary = _meter.CreateSummary("test.timer.tags", "ms", configure:
			o => o.WithQuantiles(0.50));

		var tags = new TagList { { "op", "test" } };
		using (summary.Time(tags)) {
			Thread.Sleep(10);
		}

		var measurements = CollectMeasurements("test.timer.tags");
		Assert.NotEmpty(measurements);
	}

	[Fact]
	public void SnapshotAndReset_IndependentWindows() {
		var summary = _meter.CreateSummary("test.windows", "ms", configure:
			o => o.WithQuantiles(0.50));

		summary.Record(100);
		var first = CollectMeasurements("test.windows");

		summary.Record(200);
		var second = CollectMeasurements("test.windows");

		Assert.NotEmpty(first);
		Assert.NotEmpty(second);
	}

	[Fact]
	public void Options_DefaultQuantiles() {
		var options = new SummaryOptions();
		Assert.Equal([0.95, 0.99], options.Quantiles);
	}

	[Fact]
	public void Options_FluentConfiguration() {
		var options = new SummaryOptions()
			.WithQuantiles(0.5, 0.9)
			.WithMax()
			.WithCount()
			.WithBufferCapacity(1024)
			.WithReservoir(sampleSize: 512, alpha: 0.01);

		Assert.Equal([0.5, 0.9], options.Quantiles);
		Assert.True(options.ReportMax);
		Assert.True(options.ReportCount);
		Assert.True(options.ReportSum);
		Assert.Equal(1024, options.BufferCapacity);
		Assert.Equal(512, options.SampleSize);
		Assert.Equal(0.01, options.Alpha);
	}

	private Dictionary<string, List<(double value, Dictionary<string, string> tags)>> CollectAllMeasurements() {
		var results = new Dictionary<string, List<(double, Dictionary<string, string>)>>();

		using var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, meterListener) => {
			if (instrument.Meter == _meter)
				meterListener.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => {
			if (!results.TryGetValue(instrument.Name, out var list))
				results[instrument.Name] = list = [];
			var dict = new Dictionary<string, string>();
			foreach (var tag in tags)
				dict[tag.Key] = tag.Value?.ToString() ?? "";
			list.Add((value, dict));
		});
		listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => {
			if (!results.TryGetValue(instrument.Name, out var list))
				results[instrument.Name] = list = [];
			var dict = new Dictionary<string, string>();
			foreach (var tag in tags)
				dict[tag.Key] = tag.Value?.ToString() ?? "";
			list.Add((value, dict));
		});
		listener.Start();
		listener.RecordObservableInstruments();

		return results;
	}

	private List<(double value, Dictionary<string, string> tags)> CollectMeasurements(string metricName) {
		var results = new List<(double, Dictionary<string, string>)>();

		using var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, meterListener) => {
			if (instrument.Meter == _meter && instrument.Name == metricName)
				meterListener.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<double>((_, value, tags, _) => {
			var dict = new Dictionary<string, string>();
			foreach (var tag in tags)
				dict[tag.Key] = tag.Value?.ToString() ?? "";
			results.Add((value, dict));
		});
		listener.Start();
		listener.RecordObservableInstruments();

		return results;
	}
}
