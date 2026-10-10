namespace Plugin.Maui.NearbyConnectivity;

// One established platform link. The transport's connection table pairs it with the public
// NearbyConnection, so a release disposes both in order.
interface IPlatformConnection : IAsyncDisposable
{
    // Takes the caller's buffer without copying it. Each adapter copies once at the SDK boundary,
    // which is where contiguous native memory is actually required.
    Task SendBytesAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    // Bounded by NearbyOptions.FileTransferInactivityTimeout. The uri is file:// (or content:// on
    // Android). fileName is the name the receiver sees; null means the uri's own last segment.
    Task SendFileAsync(
        string uri,
        string? fileName,
        IProgress<NearbyTransferProgress>? progress,
        CancellationToken cancellationToken);

    // The name travels in-band on Android and on the native carrier on iOS, at most 1024 UTF-8
    // bytes on the wire. Disposing the returned stream ends it for the remote reader.
    Task<Stream> OpenStreamAsync(string name, CancellationToken cancellationToken);

    Task CancelInboundTransferAsync(long payloadId);
}
