namespace Nanov.OpenTelemetry.Summary.Internal;

using System.Diagnostics;
using System.Runtime.CompilerServices;

internal sealed class TagListComparer : IEqualityComparer<TagList> {
	public static readonly TagListComparer Instance = new();

	public bool Equals(TagList x, TagList y) {
		if (x.Count != y.Count)
			return false;

		var xTags = GetTags(ref x);
		for (var i = 0; i < xTags.Length; i++) {
			if (!y.Contains(xTags[i]))
				return false;
		}

		return true;
	}

	public int GetHashCode(TagList tags) {
		var hash = 0;
		var span = GetTags(ref tags);
		foreach (ref readonly var tag in span)
			hash ^= HashCode.Combine(tag.Key, tag.Value);
		return hash;
	}

	[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Tags")]
	private static extern ReadOnlySpan<KeyValuePair<string, object?>> GetTags(ref TagList tagList);
}
