namespace Nanov.OpenTelemetry.Summary.Tests.Reservoir;

using OpenTelemetry.Summary.Reservoir;

public class QuantileAccuracyTests {
	[Theory]
	[InlineData(0, 50, 10)]
	[InlineData(1, 95, 5)]
	[InlineData(2, 99, 2)]
	public void UniformDistribution_QuantilesWithinEpsilon(int quantileIndex, double expected, double epsilon) {
		var quantiles = new[] { 0.50, 0.95, 0.99 };
		var reservoir = new ExponentiallyDecayingReservoir(sampleSize: 1028);
		var random = new Random(12345);

		for (var i = 0; i < 100_000; i++)
			reservoir.Record(random.NextDouble() * 100);

		Span<double> qv = stackalloc double[3];
		reservoir.SnapshotAndReset(quantiles, qv);

		Assert.InRange(qv[quantileIndex], expected - epsilon, expected + epsilon);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	public void NormalDistribution_QuantilesReasonable(int quantileIndex) {
		var quantiles = new[] { 0.50, 0.95, 0.99 };
		var reservoir = new ExponentiallyDecayingReservoir(sampleSize: 1028);
		var random = new Random(42);

		for (var i = 0; i < 50_000; i++) {
			var u1 = 1.0 - random.NextDouble();
			var u2 = random.NextDouble();
			var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
			var value = 100 + normal * 15;
			reservoir.Record(value);
		}

		Span<double> qv = stackalloc double[3];
		reservoir.SnapshotAndReset(quantiles, qv);

		Assert.InRange(qv[quantileIndex], 50, 200);
	}

	[Fact]
	public void BimodalDistribution_P99HitsUpperMode() {
		var quantiles = new[] { 0.50, 0.95 };
		var reservoir = new ExponentiallyDecayingReservoir(sampleSize: 1028);
		var random = new Random(42);

		for (var i = 0; i < 50_000; i++) {
			var value = random.NextDouble() < 0.9
				? random.NextDouble() * 10
				: 90 + random.NextDouble() * 10;
			reservoir.Record(value);
		}

		Span<double> qv = stackalloc double[2];
		reservoir.SnapshotAndReset(quantiles, qv);

		Assert.InRange(qv[0], 0, 15);
		Assert.InRange(qv[1], 80, 100);
	}

	[Fact]
	public void SmallSampleSize_StillReasonable() {
		var quantiles = new[] { 0.50, 0.99 };
		var reservoir = new ExponentiallyDecayingReservoir(sampleSize: 50);
		var random = new Random(42);

		for (var i = 0; i < 10_000; i++)
			reservoir.Record(random.NextDouble() * 100);

		Span<double> qv = stackalloc double[2];
		reservoir.SnapshotAndReset(quantiles, qv);

		Assert.InRange(qv[0], 30, 70);
		Assert.InRange(qv[1], 85, 100);
	}

	[Fact]
	public void RepeatedSnapshots_EachWindowAccurate() {
		var quantiles = new[] { 0.50, 0.99 };
		var reservoir = new ExponentiallyDecayingReservoir(sampleSize: 1028);
		var random = new Random(42);

		Span<double> qv = stackalloc double[2];
		
		for (var cycle = 0; cycle < 5; cycle++) {
			for (var i = 0; i < 10_000; i++)
				reservoir.Record(random.NextDouble() * 100);

			var snapshot = reservoir.SnapshotAndReset(quantiles, qv);

			Assert.Equal(10_000, snapshot.Count);
			Assert.InRange(qv[0], 35, 65);
			Assert.InRange(qv[1], 90, 100);
		}
	}
}
