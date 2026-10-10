using System.Threading.Channels;

namespace Plugin.Maui.NearbyConnectivity;

sealed class MergedPayloadStream(IAsyncEnumerable<NearbyConnection> connections)
    : IAsyncEnumerable<NearbyInboundPayload>
{
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned enumerator owns the linked source and disposes it.")]
    public IAsyncEnumerator<NearbyInboundPayload> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var source = connections.GetAsyncEnumerator(stopping.Token);
        var merged = Channel.CreateUnbounded<NearbyInboundPayload>(new UnboundedChannelOptions { SingleReader = true });
        var follow = FollowAsync(source, merged, stopping.Token);

        return new Enumerator(merged.Reader.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator(CancellationToken.None), follow, stopping);
    }

    static async Task FollowAsync(
        IAsyncEnumerator<NearbyConnection> connections,
        Channel<NearbyInboundPayload> merged,
        CancellationToken cancellationToken)
    {
        var readers = new List<Task>();

        try
        {
            while (await connections.MoveNextAsync().ConfigureAwait(false))
            {
                readers.Add(DrainConnectionAsync(connections.Current, merged, cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The enumeration ended. The readers below end with it.
        }
        finally
        {
            await Task.WhenAll(readers).ConfigureAwait(false);
            merged.Writer.TryComplete();
            await connections.DisposeAsync().ConfigureAwait(false);
        }
    }

    static async Task DrainConnectionAsync(
        NearbyConnection connection,
        Channel<NearbyInboundPayload> merged,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await connection.Inbound.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (connection.Inbound.TryRead(out var payload))
                {
                    Forward(connection, payload, merged);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            while (connection.Inbound.TryRead(out var payload))
            {
                Forward(connection, payload, merged);
            }
        }
        catch (ChannelClosedException)
        {
            // The connection completed its reader while this was waiting. Nothing left.
        }
    }

    static void Forward(NearbyConnection connection, NearbyPayload payload, Channel<NearbyInboundPayload> merged)
        => merged.Writer.TryWrite(new NearbyInboundPayload(connection.RemoteDevice, payload));

    sealed class Enumerator(
        IAsyncEnumerator<NearbyInboundPayload> inner,
        Task follow,
        CancellationTokenSource stopping) : IAsyncEnumerator<NearbyInboundPayload>
    {
        int _disposed;

        public NearbyInboundPayload Current => inner.Current;

        public ValueTask<bool> MoveNextAsync() => inner.MoveNextAsync();

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            await stopping.CancelAsync().ConfigureAwait(false);

            try
            {
                await follow.ConfigureAwait(false);
            }
            finally
            {
                stopping.Dispose();
                await inner.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
