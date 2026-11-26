namespace Nanov.OpenTelemetry.Summary.Tests.Internal;

using OpenTelemetry.Summary.Internal;

public class TagSetComparerTests {
	private static KeyValuePair<string, object?> T(string key, object? value) => new(key, value);

	[Fact]
	public void OrderDoesNotMatter() {
		var c = TagSetComparer.Instance;
		KeyValuePair<string, object?>[] stored = [T("a", 1), T("b", "x")];
		ReadOnlySpan<KeyValuePair<string, object?>> reversed = [T("b", "x"), T("a", 1)];

		Assert.True(c.Equals(reversed, stored));
		Assert.Equal(c.GetHashCode(stored), c.GetHashCode(reversed));
	}

	[Fact]
	public void DifferentValuesOrSizes_AreDifferentSets() {
		var c = TagSetComparer.Instance;
		KeyValuePair<string, object?>[] stored = [T("a", 1), T("b", "x")];

		Assert.False(c.Equals([T("b", "y"), T("a", 1)], stored));
		Assert.False(c.Equals([T("a", 1)], stored));
		Assert.False(c.Equals([T("a", 2), T("b", "x")], stored));
	}

	[Fact]
	public void BoxedValues_CompareByValue()
		=> Assert.True(TagSetComparer.Instance.Equals([T("status", 200)], [T("status", 200)]));
}
