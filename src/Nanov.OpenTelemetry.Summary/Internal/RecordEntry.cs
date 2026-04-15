namespace Nanov.OpenTelemetry.Summary.Internal;

using System.Diagnostics;

internal readonly struct RecordEntry(double value, in TagList tags) {
	public readonly double Value = value;
	public readonly TagList Tags = tags;
}
