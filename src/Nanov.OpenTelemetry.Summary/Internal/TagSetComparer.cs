namespace Nanov.OpenTelemetry.Summary.Internal;

/// <summary>
/// Compares tag sets as sets: <c>[a, b]</c> and <c>[b, a]</c> are the same child. Keys are owned arrays; lookups
/// go through the span alternate, so a hit hashes the caller's tags in place and allocates nothing.
/// </summary>
internal sealed class TagSetComparer :
	IEqualityComparer<KeyValuePair<string, object?>[]>,
	IAlternateEqualityComparer<ReadOnlySpan<KeyValuePair<string, object?>>, KeyValuePair<string, object?>[]> {
	public static readonly TagSetComparer Instance = new();

	public bool Equals(KeyValuePair<string, object?>[]? x, KeyValuePair<string, object?>[]? y)
		=> ReferenceEquals(x, y) || (x is not null && y is not null && SetEquals(x, y));

	public int GetHashCode(KeyValuePair<string, object?>[] tags)
		=> Hash(tags);

	public bool Equals(ReadOnlySpan<KeyValuePair<string, object?>> alternate, KeyValuePair<string, object?>[] other)
		=> SetEquals(alternate, other);

	public int GetHashCode(ReadOnlySpan<KeyValuePair<string, object?>> alternate)
		=> Hash(alternate);

	public KeyValuePair<string, object?>[] Create(ReadOnlySpan<KeyValuePair<string, object?>> alternate)
		=> alternate.ToArray();

	/// <summary>Order-independent: the per-pair hashes are summed (XOR would cancel duplicate pairs).</summary>
	private static int Hash(ReadOnlySpan<KeyValuePair<string, object?>> tags) {
		var hash = 0;
		foreach (ref readonly var tag in tags)
			hash += HashCode.Combine(tag.Key, tag.Value);
		return hash;
	}

	private static bool SetEquals(ReadOnlySpan<KeyValuePair<string, object?>> x, ReadOnlySpan<KeyValuePair<string, object?>> y) {
		if (x.Length != y.Length)
			return false;

		// Callers almost always pass tags in the same order: compare position by position first.
		var i = 0;
		for (; i < x.Length; i++)
			if (!TagEquals(x[i], y[i]))
				break;
		if (i == x.Length)
			return true;

		for (; i < x.Length; i++)
			if (!Contains(y, x[i]))
				return false;
		return true;
	}

	private static bool Contains(ReadOnlySpan<KeyValuePair<string, object?>> tags, in KeyValuePair<string, object?> tag) {
		foreach (ref readonly var t in tags)
			if (TagEquals(t, tag))
				return true;
		return false;
	}

	private static bool TagEquals(in KeyValuePair<string, object?> x, in KeyValuePair<string, object?> y)
		=> string.Equals(x.Key, y.Key) && Equals(x.Value, y.Value);
}
