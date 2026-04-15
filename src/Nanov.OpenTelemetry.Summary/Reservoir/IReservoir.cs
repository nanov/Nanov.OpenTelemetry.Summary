namespace Nanov.OpenTelemetry.Summary.Reservoir;

internal interface IReservoir {
	void Record(double value);
	SnapshotResult SnapshotAndReset(ReadOnlySpan<double> quantiles, Span<double> quantileValues);
}
