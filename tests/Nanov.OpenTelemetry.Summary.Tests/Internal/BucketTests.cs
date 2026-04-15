namespace Nanov.OpenTelemetry.Summary.Tests.Internal;

using System.Diagnostics;
using OpenTelemetry.Summary.Internal;

public class BucketTests {
	[Fact]
	public void TryWrite_UnderCapacity_Succeeds() {
		var bucket = new Bucket<RecordEntry>(10);

		var result = bucket.TryWrite(new RecordEntry(1.0, default));

		Assert.True(result);
		Assert.Equal(1, bucket.Count);
	}

	[Fact]
	public void TryWrite_AtCapacity_Fails() {
		var bucket = new Bucket<RecordEntry>(2);

		Assert.True(bucket.TryWrite(new RecordEntry(1.0, default)));
		Assert.True(bucket.TryWrite(new RecordEntry(2.0, default)));
		Assert.False(bucket.TryWrite(new RecordEntry(3.0, default)));
	}

	[Fact]
	public void Entries_ReturnsWrittenValues() {
		var bucket = new Bucket<RecordEntry>(10);
		bucket.TryWrite(new RecordEntry(1.0, default));
		bucket.TryWrite(new RecordEntry(2.0, default));

		var entries = bucket.Entries;

		Assert.Equal(2, entries.Length);
		Assert.Equal(1.0, entries[0].Value);
		Assert.Equal(2.0, entries[1].Value);
	}

	[Fact]
	public void Reset_ClearsCountAndEntries() {
		var bucket = new Bucket<RecordEntry>(10);
		bucket.TryWrite(new RecordEntry(1.0, default));
		bucket.TryWrite(new RecordEntry(2.0, default));

		bucket.Reset();

		Assert.Equal(0, bucket.Count);
		Assert.Equal(0, bucket.Entries.Length);
	}

	[Fact]
	public void Reset_AllowsReuse() {
		var bucket = new Bucket<RecordEntry>(2);
		bucket.TryWrite(new RecordEntry(1.0, default));
		bucket.TryWrite(new RecordEntry(2.0, default));
		Assert.False(bucket.TryWrite(new RecordEntry(3.0, default)));

		bucket.Reset();

		Assert.True(bucket.TryWrite(new RecordEntry(4.0, default)));
		Assert.Equal(1, bucket.Count);
		Assert.Equal(4.0, bucket.Entries[0].Value);
	}

	[Fact]
	public void Count_CapsAtCapacity() {
		var bucket = new Bucket<RecordEntry>(2);
		bucket.TryWrite(new RecordEntry(1.0, default));
		bucket.TryWrite(new RecordEntry(2.0, default));
		bucket.TryWrite(new RecordEntry(3.0, default));
		bucket.TryWrite(new RecordEntry(4.0, default));

		Assert.Equal(2, bucket.Count);
	}

	[Fact]
	public async Task ConcurrentWrites_NoException() {
		var bucket = new Bucket<RecordEntry>(10_000);
		var tasks = new Task[10];

		for (var t = 0; t < tasks.Length; t++) {
			var threadId = t;
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < 1000; i++)
					bucket.TryWrite(new RecordEntry(threadId * 1000 + i, default));
			});
		}

		await Task.WhenAll(tasks);
		Assert.Equal(10_000, bucket.Count);
	}

	[Fact]
	public void Entries_PreservesTags() {
		var bucket = new Bucket<RecordEntry>(10);
		var tags = new TagList { { "key", "value" } };
		bucket.TryWrite(new RecordEntry(1.0, tags));

		var entry = bucket.Entries[0];
		Assert.Equal(1.0, entry.Value);
		Assert.Single(entry.Tags);
	}
}
