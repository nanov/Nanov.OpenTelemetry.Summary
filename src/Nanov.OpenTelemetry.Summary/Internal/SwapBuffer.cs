namespace Nanov.OpenTelemetry.Summary.Internal;

internal interface IBufferConsumer<T> {
	void Consume(ReadOnlySpan<T> entries);
}

internal sealed class SwapBuffer<TEntry, TConsumer> where TConsumer : IBufferConsumer<TEntry> {
	private readonly int _highWaterMark;
	private readonly TConsumer _consumer;

	private Bucket<TEntry> _active;
	private int _aggregating;
	private Bucket<TEntry> _draining;

	public SwapBuffer(int capacity, double highWaterRatio, TConsumer consumer) {
		_highWaterMark = (int)(capacity * highWaterRatio);
		_consumer = consumer;
		_active = new Bucket<TEntry>(capacity);
		_draining = new Bucket<TEntry>(capacity);
	}

	public void Write(TEntry entry) {
		var bucket = _active;

		if (!bucket.TryWrite(entry))
			return;

		if (bucket.Count >= _highWaterMark)
			TrySwapAndAggregate();
	}

	public void DrainForSnapshot() {
		var spin = new SpinWait();
		while (Volatile.Read(ref _aggregating) == 1)
			spin.SpinOnce();

		var sealed_ = Interlocked.Exchange(ref _active, _draining);
		var entries = sealed_.Entries;

		if (entries.Length > 0)
			_consumer.Consume(entries);

		sealed_.Reset();
		_draining = sealed_;
	}

	private void TrySwapAndAggregate() {
		if (Interlocked.CompareExchange(ref _aggregating, 1, 0) != 0)
			return;

		var sealed_ = Interlocked.Exchange(ref _active, _draining);

		ThreadPool.UnsafeQueueUserWorkItem(static state => {
			var (self, bucket) = ((SwapBuffer<TEntry, TConsumer>, Bucket<TEntry>))state;
			try {
				self._consumer.Consume(bucket.Entries);
			}
			finally {
				bucket.Reset();
				self._draining = bucket;
				Volatile.Write(ref self._aggregating, 0);
			}
		}, (this, sealed_), false);
	}
}
