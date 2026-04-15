namespace Nanov.OpenTelemetry.Summary.Reservoir;

internal readonly struct SnapshotResult {
	public readonly double Max;
	public readonly double Min;
	public readonly double Sum;
	public readonly long Count;

	public SnapshotResult(double max, double min, double sum, long count) {
		Max = max;
		Min = min;
		Sum = sum;
		Count = count;
	}

	public static readonly SnapshotResult Empty = new(0, 0, 0, 0);
}
