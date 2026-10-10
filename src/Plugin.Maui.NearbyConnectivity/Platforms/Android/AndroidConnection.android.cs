namespace Plugin.Maui.NearbyConnectivity;

// One established GMS link. The adapter resolves the endpoint id on every call.
sealed class AndroidConnection(AndroidAdapter adapter, string deviceId) : IPlatformConnection
{
    int _disposed;

    public Task SendBytesAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        => adapter.SendBytesAsync(deviceId, data, cancellationToken);

    public Task SendFileAsync(string uri, string? fileName, IProgress<NearbyTransferProgress>? progress, CancellationToken cancellationToken)
        => adapter.SendFileAsync(deviceId, uri, fileName, progress, cancellationToken);

    public Task<Stream> OpenStreamAsync(string name, CancellationToken cancellationToken)
        => adapter.OpenStreamAsync(deviceId, name, cancellationToken);

    public Task CancelInboundTransferAsync(long payloadId)
        => adapter.CancelInboundTransferAsync(deviceId, payloadId);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // A local close leaves the device discovered and reconnectable, so the peer entry stays.
        await adapter.DisconnectEndpointAsync(deviceId).ConfigureAwait(false);
    }
}
