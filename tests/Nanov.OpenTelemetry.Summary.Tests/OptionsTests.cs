namespace Nanov.OpenTelemetry.Summary.Tests;

using System.Globalization;

public class OptionsTests {
	[Theory]
	[InlineData(0.95, "0.95")]
	[InlineData(0.5, "0.50")]
	[InlineData(1.0, "1.00")]
	[InlineData(0.999, "0.999")]
	public void Labels_MatchF2_UnlessItWouldRound(double q, string expected)
		=> Assert.Equal(expected, SummaryOptions.Label(q));

	[Fact]
	public void Labels_IgnoreTheCurrentCulture() {
		var previous = CultureInfo.CurrentCulture;
		try {
			CultureInfo.CurrentCulture = new CultureInfo("de-DE");
			Assert.Equal("0.95", SummaryOptions.Label(0.95));
		}
		finally {
			CultureInfo.CurrentCulture = previous;
		}
	}

	[Theory]
	[InlineData(1.5)]
	[InlineData(-0.1)]
	[InlineData(double.NaN)]
	public void QuantilesOutsideZeroToOne_Throw(double q)
		=> Assert.Throws<ArgumentOutOfRangeException>(() => new SummaryOptions().WithQuantiles(q));

	[Fact]
	public void NoQuantiles_Throws()
		=> Assert.Throws<ArgumentException>(() => new SummaryOptions().WithQuantiles());
}
