using System.Threading.Channels;

namespace Plugin.Maui.NearbyConnectivity;

sealed class Broadcast<T>(Func<IReadOnlyList<T>>? snapshot = null)
{
    readonly Lock _gate = new();
    readonly Lock _subscribeGate = new();
    readonly List<Channel<T>> _watchers = [];

    internal IAsyncEnumerable<T> Stream(CancellationToken bound = default)
        => new BroadcastStream(this, bound);

    internal IAsyncEnumerable<T> Replaying(CancellationToken bound = default)
        => new BroadcastStream(this, bound, snapshot);

    internal void Publish(T item)
    {
        Channel<T>[] watchers;

        lock (_gate)
        {
            if (_watchers.Count == 0)
            {
                return;
            }

            watchers = [.. _watchers];
        }

        foreach (var watcher in watchers)
        {
            watcher.Writer.TryWrite(item);
        }
    }

    Channel<T> Subscribe()
    {
        var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true });

        lock (_gate)
        {
            _watchers.Add(channel);
        }

        return channel;
    }

    void Unsubscribe(Channel<T> channel)
    {
        lock (_gate)
        {
            _watchers.Remove(channel);
        }

        channel.Writer.TryComplete();
    }

    sealed class BroadcastStream(
        Broadcast<T> broadcast,
        CancellationToken bound,
        Func<IReadOnlyList<T>>? snapshot = null) : IAsyncEnumerable<T>
    {
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "The returned enumerator owns the linked source and disposes it.")]
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(bound, cancellationToken);

            Channel<T> channel;
            IReadOnlyList<T>? replay;

            lock (broadcast._subscribeGate)
            {
                channel = broadcast.Subscribe();
                replay = snapshot?.Invoke();
            }

            return new Enumerator(
                broadcast,
                channel,
                Drain(replay, channel, linked.Token),
                linked);
        }

        static async IAsyncEnumerator<T> Drain(
            IReadOnlyList<T>? replay,
            Channel<T> channel,
            CancellationToken cancellationToken)
        {
            HashSet<object>? guard = null;

            if (replay is not null)
            {
                foreach (var item in replay)
                {
                    yield return item;
                }

                guard = replay.Count == 0
                    ? null
                    : new HashSet<object>(replay.OfType<object>(), ReferenceEqualityComparer.Instance);
            }

            await foreach (var item in channel.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (guard is not null)
                {
                    if (item is not null && guard.Remove(item))
                    {
                        continue;
                    }

                    if (guard.Count == 0)
                    {
                        guard = null;
                    }
                }

                yield return item;
            }
        }

        sealed class Enumerator(
            Broadcast<T> broadcast,
            Channel<T> channel,
            IAsyncEnumerator<T> inner,
            CancellationTokenSource linked) : IAsyncEnumerator<T>
        {
            int _disposed;

            public T Current
                => inner.Current;

            public ValueTask<bool> MoveNextAsync()
                => inner.MoveNextAsync();

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                broadcast.Unsubscribe(channel);
                _ = DisposeInnerAsync(inner, linked);
            }

            static async Task DisposeInnerAsync(IAsyncEnumerator<T> inner, CancellationTokenSource linked)
            {
                try
                {
                    await inner.DisposeAsync().ConfigureAwait(false);
                }
                catch (NotSupportedException)
                {
                    // Parked on an await; nothing left to release that the unsubscribe did not.
                }
                catch
                {
                    // Nothing above this observes the task, so anything escaping here would fault
                    // it unobserved — reaching no logger and surfacing on the finalizer thread far
                    // from its cause, or being dropped outright. The unsubscribe already completed
                    // the channel and removed the watcher, so a failure in the inner iterator's own
                    // teardown leaves nothing a caller could act on.
                }

                linked.Dispose();
            }
        }
    }
}
