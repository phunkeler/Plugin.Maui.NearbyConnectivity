namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// The exception that is thrown when a handshake ends without producing a connection.
/// </summary>
/// <remarks>
/// <para>
/// One exception for every way a connect can fail, with <see cref="Reason"/> carrying which. An
/// elapsed offer window raises the same exception to the initiator and to the acceptor, because the
/// deadline is declared once and shared.
/// </para>
/// <para>
/// Branch on <see cref="Reason"/>, not on the exception type:
/// </para>
/// <example>
/// <code language="csharp">
/// try
/// {
///     var connection = await device.ConnectAsync(cancellationToken);
/// }
/// catch (NearbyConnectException ex) when (ex.Reason is NearbyConnectFailure.TimedOut)
/// {
///     Status = $"{device.Presence.DisplayName} did not answer.";
/// }
/// </code>
/// </example>
/// <para>
/// Canceling the <see cref="CancellationToken"/> passed to the call raises
/// <see cref="OperationCanceledException"/> instead. A withdrawal by this app is not a handshake
/// failure.
/// </para>
/// </remarks>
public sealed class NearbyConnectException : NearbyException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NearbyConnectException"/> class.
    /// </summary>
    /// <param name="reason">Why the handshake ended without a connection.</param>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public NearbyConnectException(NearbyConnectFailure reason, string message) : base(message)
    {
        Reason = reason;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NearbyConnectException"/> class.
    /// </summary>
    /// <param name="reason">Why the handshake ended without a connection.</param>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public NearbyConnectException(NearbyConnectFailure reason, string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }

    /// <summary>
    /// Gets why the handshake ended without a connection.
    /// </summary>
    /// <value>
    /// <see cref="NearbyConnectFailure.TimedOut"/> when the offer window elapsed,
    /// <see cref="NearbyConnectFailure.DeviceUnknown"/> when the device is no longer known to the
    /// session, or <see cref="NearbyConnectFailure.Failed"/> when the handshake ended without a
    /// connection for any other reason, including a decline, which the platforms do not report
    /// consistently enough to report on its own.
    /// </value>
    public NearbyConnectFailure Reason { get; }
}
