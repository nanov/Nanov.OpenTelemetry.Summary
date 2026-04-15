namespace Nanov.OpenTelemetry.Summary.Tests.Reservoir;

using OpenTelemetry.Summary.Reservoir;

public class WeightedSnapshotTests {
	[Fact]
	public void EmptyReservoir_ReturnsZeros() {
		var reservoir = new ExponentiallyDecayingReservoir();
		Span<double> qv = stackalloc double[3];
		var snapshot = reservoir.SnapshotAndReset([0.50, 0.95, 0.99], qv);

		Assert.Equal(0, snapshot.Count);
		Assert.Equal(0, snapshot.Sum);
		Assert.Equal(0, snapshot.Max);
		Assert.Equal(0, snapshot.Min);
		Assert.Equal(0, qv[0]);
		Assert.Equal(0, qv[1]);
		Assert.Equal(0, qv[2]);
	}

	[Fact]
	public void SingleSample_AllQuantilesReturnSameValue() {
		var reservoir = new ExponentiallyDecayingReservoir();
		reservoir.Record(42.0);

		Span<double> qv = stackalloc double[4];
		reservoir.SnapshotAndReset([0.0, 0.5, 0.99, 1.0], qv);

		Assert.All(qv.ToArray(), v => Assert.Equal(42.0, v));
	}

	[Fact]
	public void EqualWeights_QuantilesMatchRank() {
		var reservoir = new ExponentiallyDecayingReservoir(sampleSize: 1028);
		for (var i = 1; i <= 100; i++)
			reservoir.Record(i);

		Span<double> qv = stackalloc double[2];
		var snapshot = reservoir.SnapshotAndReset([0.50, 0.99], qv);

		Assert.Equal(1, snapshot.Min);
		Assert.Equal(100, snapshot.Max);
		Assert.InRange(qv[0], 45, 55);
		Assert.InRange(qv[1], 98, 100);
	}

	[Fact]
	public void CountAndSumAreAccurate() {
		var reservoir = new ExponentiallyDecayingReservoir();
		reservoir.Record(10);
		reservoir.Record(20);
		reservoir.Record(30);

		Span<double> qv = stackalloc double[1];
		var snapshot = reservoir.SnapshotAndReset([0.5], qv);

		Assert.Equal(3, snapshot.Count);
		Assert.Equal(60.0, snapshot.Sum);
	}
}
