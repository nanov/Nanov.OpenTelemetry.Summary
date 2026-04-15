namespace Nanov.OpenTelemetry.Summary.Tests.Internal;

using OpenTelemetry.Summary.Internal;

public class SwapBufferTests {
	private sealed class TestConsumer : IBufferConsumer<RecordEntry> {
		public readonly List<double> Values = [];
		public int ConsumeCallCount;

		public void Consume(ReadOnlySpan<RecordEntry> entries) {
			Interlocked.Increment(ref ConsumeCallCount);
			foreach (ref readonly var entry in entries)
				lock (Values)
					Values.Add(entry.Value);
		}
	}

	[Fact]
	public void DrainForSnapshot_ReturnsAllWrittenEntries() {
		var consumer = new TestConsumer();
		var buffer = new SwapBuffer<RecordEntry, TestConsumer>(100, 0.75, consumer);

		buffer.Write(new RecordEntry(1.0, default));
		buffer.Write(new RecordEntry(2.0, default));
		buffer.Write(new RecordEntry(3.0, default));

		buffer.DrainForSnapshot();

		Assert.Equal(3, consumer.Values.Count);
		Assert.Contains(1.0, consumer.Values);
		Assert.Contains(2.0, consumer.Values);
		Assert.Contains(3.0, consumer.Values);
	}

	[Fact]
	public void DrainForSnapshot_ClearsBuffer() {
		var consumer = new TestConsumer();
		var buffer = new SwapBuffer<RecordEntry, TestConsumer>(100, 0.75, consumer);

		buffer.Write(new RecordEntry(1.0, default));
		buffer.DrainForSnapshot();

		consumer.Values.Clear();
		buffer.DrainForSnapshot();

		Assert.Empty(consumer.Values);
	}

	[Fact]
	public void HighWaterMark_TriggersAggregation() {
		var consumer = new TestConsumer();
		var buffer = new SwapBuffer<RecordEntry, TestConsumer>(10, 0.5, consumer);

		for (var i = 0; i < 5; i++)
			buffer.Write(new RecordEntry(i, default));

		Thread.Sleep(100);

		Assert.True(consumer.ConsumeCallCount >= 1);
	}

	[Fact]
	public void WriteBeyondCapacity_DropsEntries() {
		var consumer = new TestConsumer();
		var buffer = new SwapBuffer<RecordEntry, TestConsumer>(5, 2.0, consumer);

		for (var i = 0; i < 20; i++)
			buffer.Write(new RecordEntry(i, default));

		buffer.DrainForSnapshot();

		Assert.True(consumer.Values.Count <= 5);
	}

	[Fact]
	public async Task ConcurrentWriteAndDrain() {
		var consumer = new TestConsumer();
		var buffer = new SwapBuffer<RecordEntry, TestConsumer>(10_000, 0.75, consumer);
		var writing = true;

		var writerTask = Task.Run(() => {
			var i = 0;
			// ReSharper disable once AccessToModifiedClosure
			while (Volatile.Read(ref writing))
				buffer.Write(new RecordEntry(i++, default));
		});

		for (var d = 0; d < 5; d++) {
			Thread.Sleep(50);
			buffer.DrainForSnapshot();
		}

		Volatile.Write(ref writing, false);
		await writerTask;
		buffer.DrainForSnapshot();

		Assert.True(consumer.Values.Count > 0);
	}

	[Fact]
	public async Task MultipleWriters_NothingLostUnderCapacity() {
		var consumer = new TestConsumer();
		var buffer = new SwapBuffer<RecordEntry, TestConsumer>(100_000, 2.0, consumer);
		var tasks = new Task[10];

		for (var t = 0; t < tasks.Length; t++) {
			tasks[t] = Task.Run(() => {
				for (var i = 0; i < 1000; i++)
					buffer.Write(new RecordEntry(i, default));
			});
		}

		await Task.WhenAll(tasks);
		buffer.DrainForSnapshot();

		Assert.Equal(10_000, consumer.Values.Count);
	}
}
