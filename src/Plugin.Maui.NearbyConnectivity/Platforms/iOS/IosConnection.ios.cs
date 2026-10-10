namespace Plugin.Maui.NearbyConnectivity;

// One established MPC link. Disposal sends the in-band disconnect frame — MPC has no per-peer
// disconnect — then releases the peer's bookkeeping and retires the session when it holds nothing
// else.
sealed class IosConnection(IosAdapter adapter, string deviceId) : IPlatformConnection
{
    int _disposed;

    public Task SendBytesAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        => adapter.SendBytesAsync(deviceId, data, cancellationToken);

    public Task SendFileAsync(string uri, string? fileName, IProgress<NearbyTransferProgress>? progress, CancellationToken cancellationToken)
        => adapter.SendFileAsync(deviceId, uri, fileName, progress, cancellationToken);

    public Task<Stream> OpenStreamAsync(string name, CancellationToken cancellationToken)
        => adapter.OpenStreamAsync(deviceId, name, cancellationToken);

    public Task CancelInboundTransferAsync(long payloadId)
    {
        adapter.CancelInboundTransfer(deviceId, payloadId);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await adapter.DisconnectPeerAsync(deviceId).ConfigureAwait(false);
    }
}
