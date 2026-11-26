namespace Nanov.OpenTelemetry.Summary.Tests.Reservoir;

using OpenTelemetry.Summary.Reservoir;

public class ExponentiallyDecayingReservoirTests {
	[Fact]
	public void EmptyReservoir_SnapshotReturnsZeros() {
		var reservoir = new ExponentiallyDecayingReservoir();
		Span<double> qv = stackalloc double[1];
		var snapshot = reservoir.SnapshotAndReset([0.50], qv);

		Assert.Equal(0, snapshot.Count);
		Assert.Equal(0, snapshot.Sum);
		Assert.Equal(0, qv[0]);
	}

	[Fact]
	public void SingleRecord_SnapshotReflectsValue() {
		var reservoir = new ExponentiallyDecayingReservoir();
		reservoir.Record(42.0);

		Span<double> qv = stackalloc double[1];
		var snapshot = reservoir.SnapshotAndReset([0.50], qv);

		Assert.Equal(1, snapshot.Count);
		Assert.Equal(42.0, snapshot.Sum);
		Assert.Equal(42.0, qv[0]);
	}

	[Fact]
	public void SnapshotAndReset_ClearsReservoir() {
		var reservoir = new ExponentiallyDecayingReservoir();
		for (var i = 0; i < 100; i++)
			reservoir.Record(i);

		Span<double> qv = stackalloc double[1];
		var first = reservoir.SnapshotAndReset([0.50], qv);
		Assert.Equal(100, first.Count);

		var second = reservoir.SnapshotAndReset([0.50], qv);
		Assert.Equal(0, second.Count);
	}

	[Fact]
	public void RespectsMaxSampleSize() {
		var reservoir = new ExponentiallyDecayingReservoir(sampleSize: 10);
		for (var i = 0; i < 1000; i++)
			reservoir.Record(i);

		Span<double> qv = stackalloc double[1];
		var snapshot = reservoir.SnapshotAndReset([0.50], qv);

		Assert.Equal(1000, snapshot.Count);
		Assert.True(qv[0] > 0);
	}

	[Fact]
	public void UniformDistribution_QuantilesAreReasonable() {
		var reservoir = new ExponentiallyDecayingReservoir(sampleSize: 1028);
		var random = new Random(42);

		for (var i = 0; i < 10_000; i++)
			reservoir.Record(random.NextDouble() * 100);

		Span<double> qv = stackalloc double[3];
		var snapshot = reservoir.SnapshotAndReset([0.50, 0.95, 0.99], qv);

		Assert.Equal(10_000, snapshot.Count);
		Assert.InRange(qv[0], 40, 60);
		Assert.InRange(qv[1], 90, 100);
		Assert.InRange(qv[2], 95, 100);
	}

	[Fact]
	public void SumIsAccurate() {
		var reservoir = new ExponentiallyDecayingReservoir();
		reservoir.Record(10);
		reservoir.Record(20);
		reservoir.Record(30);

		Span<double> qv = stackalloc double[1];
		var snapshot = reservoir.SnapshotAndReset([0.50], qv);

		Assert.Equal(60.0, snapshot.Sum);
		Assert.Equal(3, snapshot.Count);
	}

	[Fact]
	public void MultipleSnapshotCycles_Independent() {
		var reservoir = new ExponentiallyDecayingReservoir();
		Span<double> qv = stackalloc double[1];

		reservoir.Record(100);
		var s1 = reservoir.SnapshotAndReset([0.50], qv);

		reservoir.Record(200);
		var s2 = reservoir.SnapshotAndReset([0.50], qv);

		Assert.Equal(1, s1.Count);
		Assert.Equal(100, s1.Sum);
		Assert.Equal(1, s2.Count);
		Assert.Equal(200, s2.Sum);
	}
}

public class ExponentiallyDecayingReservoirSamplingTests {
	private static long Now => Environment.TickCount64 / 1000;

	[Fact]
	public void ExactWhileUnderTheSampleSize() {
		var r = new ExponentiallyDecayingReservoir();
		for (var v = 1; v <= 100; v++)
			r.Record(v, Now);

		Span<double> qv = stackalloc double[4];
		var s = r.SnapshotAndReset([0.5, 0.95, 0.99, 1.0], qv);

		Assert.Equal([50.0, 95.0, 99.0, 100.0], qv.ToArray());
		Assert.Equal(100, s.Count);
		Assert.Equal(5050.0, s.Sum);
		Assert.Equal(1.0, s.Min);
		Assert.Equal(100.0, s.Max);
	}

	[Fact]
	public void TheSampleGrowsWithItsValues_UpToItsSize() {
		var r = new ExponentiallyDecayingReservoir();
		Assert.Equal(0, r.Capacity);

		r.Record(1.0, Now);
		Assert.True(r.Capacity < 1028);

		for (var v = 0; v < 10_000; v++)
			r.Record(v, Now);
		Assert.Equal(1028, r.SampledCount);
		Assert.Equal(1028, r.Capacity);
	}

	[Fact]
	public void MaxAndMin_CoverEvictedValues() {
		var r = new ExponentiallyDecayingReservoir(sampleSize: 4);
		r.Record(-1_000, Now);
		for (var v = 0; v < 10_000; v++)
			r.Record(v % 10, Now);
		r.Record(1_000_000, Now);
		for (var v = 0; v < 10_000; v++)
			r.Record(v % 10, Now);

		Span<double> qv = stackalloc double[1];
		var s = r.SnapshotAndReset([0.5], qv);

		Assert.Equal(1_000_000, s.Max);
		Assert.Equal(-1_000, s.Min);
		Assert.Equal(20_002, s.Count);
	}

	[Fact]
	public void RecentValuesWeighMore() {
		var r = new ExponentiallyDecayingReservoir(sampleSize: 100, alpha: 0.5);
		var start = Environment.TickCount64 / 1000;
		for (var i = 0; i < 1000; i++)
			r.Record(1.0, start);
		for (var i = 0; i < 1000; i++)
			r.Record(1000.0, start + 60);

		Span<double> qv = stackalloc double[1];
		r.SnapshotAndReset([0.05], qv);

		Assert.Equal(1000.0, qv[0]);
	}

	[Fact]
	public void RescalingKeepsTheOrder() {
		var r = new ExponentiallyDecayingReservoir(sampleSize: 10, rescaleInterval: TimeSpan.FromSeconds(10));
		var start = Environment.TickCount64 / 1000;
		r.Record(1.0, start);
		r.Record(2.0, start + 3_600);

		Span<double> qv = stackalloc double[1];
		var s = r.SnapshotAndReset([1.0], qv);

		Assert.Equal(2.0, qv[0]);
		Assert.Equal(2, s.Count);
	}
}
