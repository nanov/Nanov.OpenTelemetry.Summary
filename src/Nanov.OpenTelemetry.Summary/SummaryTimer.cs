namespace Nanov.OpenTelemetry.Summary;

using System.Diagnostics;

/// <summary>
/// From <see cref="Summary.Time()"/>. Records the elapsed milliseconds when disposed, under the tags it started
/// with, or call <see cref="Record(in TagList)"/> when the tags depend on the outcome. The tag set is resolved
/// when the timer starts, so disposing it does no lookup.
/// </summary>
public readonly struct SummaryTimer : IDisposable {
	private readonly Summary _summary;
	private readonly SummaryChild? _child;
	private readonly long _startTimestamp;

	internal SummaryTimer(Summary summary, SummaryChild? child) {
		_summary = summary;
		_child = child;
		_startTimestamp = Stopwatch.GetTimestamp();
	}

	public double ElapsedMilliseconds => Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;

	/// <summary>Records now, under the tags the timer started with.</summary>
	public void Record()
		=> _child?.Record(ElapsedMilliseconds);

	/// <summary>
	/// Records now under <paramref name="tags"/> instead of the ones the timer started with. Don't also dispose
	/// the timer, or it records twice.
	/// </summary>
	public void Record(in TagList tags)
		=> _summary.Record(ElapsedMilliseconds, in tags);

	public void Dispose()
		=> _child?.Record(ElapsedMilliseconds);
}
