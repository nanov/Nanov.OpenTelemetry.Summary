namespace Nanov.OpenTelemetry.Summary;

using Reservoir;

public sealed class SummaryOptions {
	public double[] Quantiles { get; private set; } = [0.95, 0.99];
	internal string[] QuantileLabels { get; private set; } = ["0.95", "0.99"];
	public bool ReportMax { get; private set; }
	public bool ReportCount { get; private set; } = true;
	public bool ReportSum { get; private set; } = true;
	public int BufferCapacity { get; private set; } = 4_096;
	public int SampleSize { get; private set; } = 1028;
	public double Alpha { get; private set; } = 0.015;

	public SummaryOptions WithQuantiles(params double[] quantiles) {
		Quantiles = quantiles;
		QuantileLabels = Array.ConvertAll(quantiles, q => q.ToString("F2"));
		return this;
	}

	public SummaryOptions WithMax(bool enabled = true) {
		ReportMax = enabled;
		return this;
	}

	public SummaryOptions WithCount(bool enabled = true) {
		ReportCount = enabled;
		return this;
	}

	public SummaryOptions WithSum(bool enabled = true) {
		ReportSum = enabled;
		return this;
	}

	public SummaryOptions WithoutCount() {
		ReportCount = false;
		return this;
	}

	public SummaryOptions WithoutSum() {
		ReportSum = false;
		return this;
	}

	public SummaryOptions WithBufferCapacity(int capacity) {
		BufferCapacity = capacity;
		return this;
	}

	public SummaryOptions WithReservoir(int sampleSize = 1028, double alpha = 0.015) {
		SampleSize = sampleSize;
		Alpha = alpha;
		return this;
	}

	internal IReservoir CreateReservoir() => new ExponentiallyDecayingReservoir(SampleSize, Alpha);
}
