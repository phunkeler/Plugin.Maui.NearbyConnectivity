namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Represents the progress of an ongoing or completed payload transfer.
/// </summary>
/// <param name="payloadId">
/// An identifier for this transfer, distinct from other transfers in flight on the same
/// connection. Session-scoped and meaningful only within this plugin.
/// </param>
/// <param name="bytesTransferred">The number of bytes transferred so far.</param>
/// <param name="totalBytes">
/// The total size of the payload in bytes, or <see langword="null"/> when unknown. Zero or less is
/// stored as <see langword="null"/>. See <see cref="TotalBytes"/>.
/// </param>
/// <param name="status">The status of the transfer.</param>
/// <remarks>
/// Instances are reported through the <see cref="IProgress{T}"/> supplied to a <c>SendFileAsync</c>
/// overload for an outgoing transfer, or through
/// <see cref="NearbyConnection.InboundTransfersAsync(CancellationToken)"/> for an incoming one.
/// </remarks>
public sealed class NearbyTransferProgress(
    long payloadId,
    long bytesTransferred,
    long? totalBytes,
    NearbyTransferStatus status)
{
    /// <summary>
    /// Gets the identifier for this transfer.
    /// </summary>
    /// <value>
    /// An identifier that distinguishes this transfer from others in flight on the same
    /// connection, on every platform. Session-scoped, and meaningful only within this plugin.
    /// Do not persist it or send it to a peer.
    /// </value>
    /// <remarks>
    /// To cancel an inbound transfer, pass this value to
    /// <see cref="NearbyConnection.CancelInboundTransferAsync(long)"/>.
    /// </remarks>
    public long PayloadId { get; } = payloadId;

    /// <summary>
    /// Gets the number of bytes transferred so far.
    /// </summary>
    /// <value>The count of bytes transferred at the time this update was reported.</value>
    public long BytesTransferred { get; } = bytesTransferred;

    /// <summary>
    /// Gets the total size of the payload.
    /// </summary>
    /// <value>
    /// The total size of the payload in bytes, or <see langword="null"/> when the size is not
    /// known in advance, as for a stream. Never zero or negative.
    /// </value>
    public long? TotalBytes { get; } = totalBytes > 0 ? totalBytes : null;

    /// <summary>
    /// Gets the status of the transfer.
    /// </summary>
    /// <value>One of the <see cref="NearbyTransferStatus"/> values.</value>
    public NearbyTransferStatus Status { get; } = status;

    /// <summary>
    /// Gets the proportion of the transfer that has completed.
    /// </summary>
    /// <value>
    /// A value between 0.0 and 1.0, or <see langword="null"/> when <see cref="TotalBytes"/> is
    /// <see langword="null"/>. Bind to this value to drive a progress indicator.
    /// </value>
    public double? Fraction => (double)BytesTransferred / TotalBytes;
}
