namespace Nanov.OpenTelemetry.Summary;

using System.Diagnostics;
using Reservoir;

internal sealed class SummaryChild {
	private readonly IReservoir _reservoir;
	public readonly KeyValuePair<string, object?>[][] QuantileTagArrays;
	public readonly KeyValuePair<string, object?>[] BaseTags;

	// Cumulative (monotonic) totals for the count/sum counters. Accumulated at snapshot time
	// (Summary.CollectQuantiles) so the counters carry this child's tags and grow across windows.
	public long CumulativeCount;
	public double CumulativeSum;

	public SummaryChild(SummaryOptions options, in TagList tags = default) {
		_reservoir = options.CreateReservoir();

		BaseTags = tags.Count > 0 ? tags.ToArray() : [];

		QuantileTagArrays = new KeyValuePair<string, object?>[options.Quantiles.Length][];
		for (var i = 0; i < options.Quantiles.Length; i++) {
			var arr = new KeyValuePair<string, object?>[tags.Count + 1];
			for (var j = 0; j < tags.Count; j++)
				arr[j] = tags[j];
			arr[tags.Count] = new KeyValuePair<string, object?>("quantile", options.QuantileLabels[i]);
			QuantileTagArrays[i] = arr;
		}
	}

	public void Record(double value)
		=> _reservoir.Record(value);

	public SnapshotResult SnapshotAndReset(ReadOnlySpan<double> quantiles, Span<double> quantileValues)
		=> _reservoir.SnapshotAndReset(quantiles, quantileValues);
}
