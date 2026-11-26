namespace Nanov.OpenTelemetry.Summary.Tests.Internal;

using System.Diagnostics.Metrics;

public class SummaryChildTests : IDisposable {
	private static readonly double[] Quantiles = [0.5, 1.0];

	private readonly Meter _meter = new("SummaryChild.Tests." + Guid.NewGuid().ToString("N"));
	private readonly MeterListener _listener = new();
	private readonly ObservableGauge<double> _gate;

	public SummaryChildTests() {
		_gate = _meter.CreateObservableGauge("gate", () => 0.0);
		_listener.InstrumentPublished = (instrument, l) => {
			if (instrument.Meter == _meter)
				l.EnableMeasurementEvents(instrument);
		};
		_listener.Start();
	}

	public void Dispose() {
		_listener.Dispose();
		_meter.Dispose();
	}

	private SummaryChild Child(int capacity, int stripes = 1, params KeyValuePair<string, object?>[] tags)
		=> new(new SummaryOptions().WithQuantiles(0.5, 1.0).WithBufferCapacity(capacity), _gate, tags, stripes);

	private static bool Collect(SummaryChild child) {
		lock (child.SyncRoot)
			return child.Collect(Quantiles);
	}

	[Fact]
	public void QuantileTags_AppendTheLabel() {
		var child = Child(8, 1, new KeyValuePair<string, object?>("route", "/a"));
		Assert.Equal([new("route", "/a"), new("quantile", "0.50")], child.QuantileTagArrays[0]);
		Assert.Equal([new("route", "/a"), new("quantile", "1.00")], child.QuantileTagArrays[1]);
	}

	[Fact]
	public void SingleThread_LosesNothingBelowCapacity() {
		var child = Child(1000);
		for (var v = 0; v < 700; v++)
			child.Record(v);
		child.Record(double.NaN);

		Assert.True(Collect(child));
		Assert.Equal(700, child.TotalCount);
		Assert.Equal(244_650.0, child.TotalSum);
		Assert.Equal(0, child.TotalDropped);

		Assert.False(Collect(child), "the next window is empty");
		Assert.Equal(700, child.TotalCount);
	}

	[Fact]
	public void DroppedValues_StillReachCountSumAndMax() {
		var child = Child(64);
		// Hold the lock so no aggregation frees a bucket: everything past 128 slots is dropped.
		lock (child.SyncRoot) {
			for (var v = 1; v <= 200; v++)
				child.Record(v);
			// Past both buckets, so only the drop path sees it.
			child.Record(-5);

			Assert.True(child.Collect(Quantiles));
		}

		Assert.Equal(201, child.TotalCount);
		Assert.Equal(20_095.0, child.TotalSum);
		Assert.Equal(200.0, child.Last.Max);
		Assert.Equal(-5.0, child.Last.Min);
		Assert.True(child.TotalDropped >= 201 - 128, child.TotalDropped.ToString());
		Assert.True(child.QuantileValues[1] < 200.0, "the quantiles only saw the sample");
	}

	[Fact]
	public void CollectSeesEveryValue_AcrossAggregations() {
		var child = Child(256, stripes: 4);
		var threads = new Thread[4];
		for (var t = 0; t < threads.Length; t++) {
			threads[t] = new Thread(() => {
				for (var v = 0; v < 500; v++) {
					child.Record(v);
					if (v % 50 == 0)
						Thread.Yield();
				}
			});
			threads[t].Start();
		}
		foreach (var t in threads)
			t.Join();

		Assert.True(Collect(child));
		Assert.Equal(2000, child.TotalCount);
		Assert.True(child.Last.Max <= 499.0);
	}

	[Fact]
	public void CountAndSum_StayExact_WhileADrainRacesTheWriters() {
		// Small stripes and a drainer that never stops: writers are often between reserving a slot and storing
		// into it when their bucket is sealed.
		var child = Child(256, stripes: 4);
		var done = 0;
		var drainer = new Thread(() => {
			while (Volatile.Read(ref done) == 0)
				Collect(child);
		});
		drainer.Start();

		var writers = new Thread[8];
		for (var t = 0; t < writers.Length; t++) {
			writers[t] = new Thread(() => {
				for (var i = 0; i < 100_000; i++)
					child.Record(1.0);
			});
			writers[t].Start();
		}
		foreach (var w in writers)
			w.Join();
		Volatile.Write(ref done, 1);
		drainer.Join();

		Collect(child);
		Assert.Equal(800_000, child.TotalCount);
		Assert.Equal(800_000.0, child.TotalSum);
	}

	[Fact]
	public void AStripesSlots_AreAllocatedByItsFirstValue() {
		var child = Child(1024, stripes: 8);
		Assert.Equal(0, child.AllocatedStripes);

		child.Record(1.0);
		Assert.Equal(1, child.AllocatedStripes);

		Assert.True(Collect(child));
		Assert.Equal(1, child.TotalCount);
	}

	[Fact]
	public void Record_IsANoOp_WhileTheGateIsDisabled() {
		using var meter = new Meter("SummaryChild.Disabled." + Guid.NewGuid().ToString("N"));
		var gate = meter.CreateObservableGauge("gate", () => 0.0);
		var child = new SummaryChild(new SummaryOptions(), gate, [], 1);

		child.Record(1.0);

		Assert.Equal(0, child.AllocatedStripes);
		Assert.False(Collect(child));
	}
}
