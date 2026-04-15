namespace Nanov.OpenTelemetry.Summary;

using System.Diagnostics;

public readonly struct SummaryTimer : IDisposable {
	private readonly Summary _summary;
	private readonly TagList _tags;
	private readonly long _startTimestamp;

	internal SummaryTimer(Summary summary, in TagList tags) {
		_summary = summary;
		_tags = tags;
		_startTimestamp = Stopwatch.GetTimestamp();
	}

	public double ElapsedMilliseconds => Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;

	public void Record()
		=> _summary.Record(ElapsedMilliseconds, in _tags);

	public void Record(in TagList tags)
		=> _summary.Record(ElapsedMilliseconds, in tags);

	public void Dispose()
		=> _summary.Record(ElapsedMilliseconds, in _tags);
}
