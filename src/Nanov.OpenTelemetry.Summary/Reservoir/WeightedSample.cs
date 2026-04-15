namespace Nanov.OpenTelemetry.Summary.Reservoir;

public readonly record struct WeightedSample(double Value, double Weight);