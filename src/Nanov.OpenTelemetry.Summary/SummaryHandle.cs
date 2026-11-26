namespace Nanov.OpenTelemetry.Summary;

using System.Diagnostics;

/// <summary>
/// One tag set of a <see cref="Summary"/>, from <see cref="Summary.Bind(in TagList)"/>. Records without looking
/// the tags up: the hot-path form when the tags are known ahead of time. Cheap to copy; copies record into the
/// same series.
/// </summary>
public readonly struct SummaryHandle {
	private readonly SummaryChild _child;

	internal SummaryHandle(SummaryChild child) {
		_child = child;
	}

	/// <summary>Whether a listener is subscribed to the summary. Recording is a no-op otherwise.</summary>
	public bool IsEnabled => _child.IsEnabled;

	/// <summary>Records <paramref name="value"/>. NaN is ignored.</summary>
	public void Record(double value)
		=> _child.Record(value);

	/// <summary>Times until the returned timer is disposed, then records the elapsed milliseconds.</summary>
	public SummaryHandleTimer Time() => new(_child);
}

/// <summary>From <see cref="SummaryHandle.Time"/>. Records the elapsed milliseconds when disposed.</summary>
public readonly struct SummaryHandleTimer : IDisposable {
	private readonly SummaryChild _child;
	private readonly long _startTimestamp;

	internal SummaryHandleTimer(SummaryChild child) {
		_child = child;
		_startTimestamp = Stopwatch.GetTimestamp();
	}

	public double ElapsedMilliseconds => Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;

	public void Dispose()
		=> _child.Record(ElapsedMilliseconds);
}
