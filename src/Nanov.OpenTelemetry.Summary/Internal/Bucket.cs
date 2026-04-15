namespace Nanov.OpenTelemetry.Summary.Internal;

internal sealed class Bucket<T> {
	private readonly T[] _entries;
	private int _count;

	public Bucket(int capacity) {
		_entries = new T[capacity];
	}

	public int Capacity => _entries.Length;

	public int Count {
		get {
			var count = Volatile.Read(ref _count);
			return count > _entries.Length || count < 0 ? _entries.Length : count;
		}
	}

	public ReadOnlySpan<T> Entries => _entries.AsSpan(0, Count);

	public bool TryWrite(T entry) {
		var idx = Interlocked.Increment(ref _count) - 1;
		if ((uint)idx >= (uint)_entries.Length)
			return false;

		_entries[idx] = entry;
		return true;
	}

	public void Reset() {
		_entries.AsSpan().Clear();
		Volatile.Write(ref _count, 0);
	}
}
