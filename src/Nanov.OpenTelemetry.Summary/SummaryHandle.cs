namespace Nanov.OpenTelemetry.Summary;

using System.Diagnostics;

/// <summary>
/// One tag set of a <see cref="Summary"/>, from <see cref="Summary.Bind(in TagList)"/>. Records without looking
/// the tags up: the hot-path form when the tags are known ahead of time. Cheap to copy; copies record into the
/// same series.
/// </summary>
public readonly struct SummaryHandle {
	private readonly Summary _summary;
	private readonly SummaryChild _child;

	internal SummaryHandle(Summary summary, SummaryChild child) {
		_summary = summary;
		_child = child;
	}

	/// <summary>Whether a listener is subscribed to the summary. Recording is a no-op otherwise.</summary>
	public bool IsEnabled => _child.IsEnabled;

	/// <summary>Records <paramref name="value"/>. NaN is ignored.</summary>
	public void Record(double value)
		=> _child.Record(value);

	/// <summary>Times until the returned timer is disposed, then records the elapsed milliseconds.</summary>
	public SummaryTimer Time() => new(_summary, _child);
}
