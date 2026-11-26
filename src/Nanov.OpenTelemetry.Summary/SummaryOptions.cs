namespace Nanov.OpenTelemetry.Summary;

using System.Globalization;

public sealed class SummaryOptions {
	public double[] Quantiles { get; private set; } = [0.95, 0.99];
	internal string[] QuantileLabels { get; private set; } = ["0.95", "0.99"];
	public bool ReportMax { get; private set; }
	public bool ReportMin { get; private set; }
	public bool ReportCount { get; private set; } = true;
	public bool ReportSum { get; private set; } = true;
	public int BufferCapacity { get; private set; } = 4_096;
	public int SampleSize { get; private set; } = 1028;
	public double Alpha { get; private set; } = 0.015;

	/// <summary>The quantiles to report, each in <c>[0, 1]</c>.</summary>
	public SummaryOptions WithQuantiles(params double[] quantiles) {
		ArgumentNullException.ThrowIfNull(quantiles);
		if (quantiles.Length == 0)
			throw new ArgumentException("A summary needs at least one quantile.", nameof(quantiles));

		var labels = new string[quantiles.Length];
		for (var i = 0; i < quantiles.Length; i++) {
			var q = quantiles[i];
			if (!(q is >= 0 and <= 1))
				throw new ArgumentOutOfRangeException(nameof(quantiles), q, "Quantiles must be in [0, 1].");
			labels[i] = Label(q);
		}

		Quantiles = (double[])quantiles.Clone();
		QuantileLabels = labels;
		return this;
	}

	/// <summary>
	/// <c>0.95</c> → <c>"0.95"</c>, <c>0.5</c> → <c>"0.50"</c>; <c>0.999</c> → <c>"0.999"</c>, where two decimals
	/// would round to <c>"1.00"</c> and collide with the max. Invariant culture, so a <c>de-DE</c> host does not
	/// report <c>"0,95"</c>. Matches the Rust port, so services in both languages share dashboards.
	/// </summary>
	internal static string Label(double q) {
		var two = q.ToString("F2", CultureInfo.InvariantCulture);
		return double.Parse(two, CultureInfo.InvariantCulture) == q ? two : q.ToString("R", CultureInfo.InvariantCulture);
	}

	public SummaryOptions WithMax(bool enabled = true) {
		ReportMax = enabled;
		return this;
	}

	/// <summary>Report <c>{name}.min</c>, the smallest value recorded in the window.</summary>
	public SummaryOptions WithMin(bool enabled = true) {
		ReportMin = enabled;
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

	/// <summary>
	/// Values buffered per tag set before they reach the reservoir, split between one stripe per core (at least
	/// 64 each). A burst on one thread larger than about twice its stripe between aggregations misses the sample;
	/// <c>{name}.dropped</c> counts those values.
	/// </summary>
	public SummaryOptions WithBufferCapacity(int capacity) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
		BufferCapacity = capacity;
		return this;
	}

	public SummaryOptions WithReservoir(int sampleSize = 1028, double alpha = 0.015) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleSize);
		SampleSize = sampleSize;
		Alpha = alpha;
		return this;
	}
}
