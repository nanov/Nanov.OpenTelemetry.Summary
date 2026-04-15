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
