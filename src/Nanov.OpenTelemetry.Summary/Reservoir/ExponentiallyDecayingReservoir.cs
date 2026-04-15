namespace Nanov.OpenTelemetry.Summary.Reservoir;

using System.Runtime.CompilerServices;

internal sealed class ExponentiallyDecayingReservoir : IReservoir {
	private readonly double _alpha;
	private readonly Random _random = new();
	private readonly TimeSpan _rescaleInterval;
	private readonly int _sampleSize;
	private readonly WeightedSample[] _sortBuffer;
	private long _count;
	private long _nextRescale;

	private SortedList<double, WeightedSample> _samples;
	private long _startTime;
	private double _sum;

	public ExponentiallyDecayingReservoir(int sampleSize = 1028, double alpha = 0.015, TimeSpan? rescaleInterval = null) {
		_sampleSize = sampleSize;
		_alpha = alpha;
		_rescaleInterval = rescaleInterval ?? TimeSpan.FromHours(1);
		_samples = new SortedList<double, WeightedSample>(sampleSize + 1, ReverseComparer.Instance);
		_sortBuffer = new WeightedSample[sampleSize];
		_startTime = GetTickSeconds();
		_nextRescale = _startTime + (long)_rescaleInterval.TotalSeconds;
	}

	public void Record(double value) {
		var now = GetTickSeconds();

		if (now >= _nextRescale)
			Rescale(now);

		var weight = Math.Exp(_alpha * (now - _startTime));
		var priority = weight / _random.NextDouble();
		var sample = new WeightedSample(value, weight);

		_count++;
		_sum += value;

		if (_samples.Count < _sampleSize) {
			_samples[priority] = sample;
		}
		else {
			var minKey = _samples.Keys[^1];
			if (priority > minKey) {
				_samples.RemoveAt(_samples.Count - 1);
				_samples[priority] = sample;
			}
		}
	}

	public SnapshotResult SnapshotAndReset(ReadOnlySpan<double> quantiles, Span<double> quantileValues) {
		if (_samples.Count == 0) {
			quantileValues.Clear();
			Reset();
			return SnapshotResult.Empty;
		}

		var result = ComputeSnapshot(quantiles, quantileValues);
		Reset();
		return result;
	}

	private SnapshotResult ComputeSnapshot(ReadOnlySpan<double> quantiles, Span<double> quantileValues) {
		var count = _samples.Count;
		_samples.Values.CopyTo(_sortBuffer, 0);
		var sorted = _sortBuffer.AsSpan(0, count);
		sorted.Sort(static (a, b) => a.Value.CompareTo(b.Value));

		var totalWeight = 0.0;
		for (var i = 0; i < count; i++)
			totalWeight += sorted[i].Weight;

		for (var q = 0; q < quantiles.Length; q++)
			quantileValues[q] = ComputeQuantile(sorted, totalWeight, quantiles[q]);

		return new SnapshotResult(
			max: sorted[^1].Value,
			min: sorted[0].Value,
			sum: _sum,
			count: _count);
	}

	private static double ComputeQuantile(ReadOnlySpan<WeightedSample> sorted, double totalWeight, double q) {
		switch (q) {
			case <= 0:
				return sorted[0].Value;
			case >= 1:
				return sorted[^1].Value;
		}

		var cumulative = 0.0;
		foreach (ref readonly var sample in sorted) {
			cumulative += sample.Weight / totalWeight;
			if (cumulative >= q)
				return sample.Value;
		}

		return sorted[^1].Value;
	}

	private void Reset() {
		_samples.Clear();
		_count = 0;
		_sum = 0;
		_startTime = GetTickSeconds();
		_nextRescale = _startTime + (long)_rescaleInterval.TotalSeconds;
	}

	private void Rescale(long now) {
		var factor = Math.Exp(-_alpha * (now - _startTime));
		var newSamples = new SortedList<double, WeightedSample>(_sampleSize + 1, ReverseComparer.Instance);

		foreach (var kvp in _samples) {
			var newKey = kvp.Key * factor;
			var newWeight = kvp.Value.Weight * factor;
			newSamples[newKey] = new WeightedSample(kvp.Value.Value, newWeight);
		}

		_samples = newSamples;
		_startTime = now;
		_nextRescale = now + (long)_rescaleInterval.TotalSeconds;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static long GetTickSeconds()
		=> Environment.TickCount64 / 1000;

	private sealed class ReverseComparer : IComparer<double> {
		public static readonly ReverseComparer Instance = new();

		public int Compare(double x, double y)
			=> y.CompareTo(x);
	}
}
