namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// The exception that is thrown when a file transfer is canceled by something other than the
/// caller's <see cref="CancellationToken"/>: the other device canceled it, or the platform did.
/// </summary>
/// <remarks>
/// <para>
/// The usual cause is the receiver calling
/// <see cref="NearbyConnection.CancelInboundTransferAsync(long)"/>. On Android, Google Play
/// services also cancels unfinished transfers when the connection closes, so check
/// <see cref="NearbyConnection.IsOpen"/>: if the connection is still open, the other device refused
/// the file.
/// </para>
/// </remarks>
/// <seealso cref="NearbyTransferException"/>
public sealed class NearbyTransferCanceledException : NearbyTransferException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NearbyTransferCanceledException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public NearbyTransferCanceledException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NearbyTransferCanceledException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public NearbyTransferCanceledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
