namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Why a connection request or handshake ended without producing a connection.
/// </summary>
/// <remarks>
/// <para>
/// One vocabulary for both roles. The offer's deadline is declared once, by the initiator, and
/// travels to the acceptor on the wire, so an elapsed window is the same fact on both sides and
/// reports the same <see cref="TimedOut"/> value, rather than two exception types split by which
/// side observed it.
/// </para>
/// <para>
/// Carried by <see cref="NearbyConnectException.Reason"/>. Branch on this rather than on the
/// exception type.
/// </para>
/// </remarks>
public enum NearbyConnectFailure
{
    /// <summary>
    /// The offer window elapsed before the request was answered.
    /// </summary>
    /// <remarks>
    /// The window is <see cref="NearbyOptions.OfferWindow"/>, declared by the initiator and
    /// honored by both sides. An initiator sees it when nobody answered; an acceptor sees it when
    /// an accept lost its race with the same deadline.
    /// </remarks>
    TimedOut,

    /// <summary>
    /// The handshake ended without a connection: the remote device declined the request, it left
    /// range mid-handshake, the radio dropped, or the platform returned an error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A decline is not reported separately, because the two platforms cannot agree on it. Android
    /// distinguishes one, since Nearby Connections answers a declined request with
    /// <c>STATUS_CONNECTION_REJECTED</c>, but iOS does not: MultipeerConnectivity answers
    /// <c>invitationHandler(false, null)</c>, which reaches the initiator as an ordinary
    /// <c>MCSessionState.NotConnected</c> with no reason attached, and no session exists to carry
    /// one.
    /// </para>
    /// <para>
    /// Reporting a decline only on Android would make one user action arrive as two different
    /// values, so every caller who cared would branch on the platform. This value is deliberately
    /// the coarser, uniform answer instead. Android's status code is still written to the log for
    /// diagnostics.
    /// </para>
    /// </remarks>
    Failed,

    /// <summary>
    /// No device with that identifier is currently known to the session.
    /// </summary>
    /// <remarks>
    /// The answer for a device that has gone out of range or stopped advertising since the
    /// caller read its id. Reported here rather than as a bare <see cref="NearbyException"/> so a
    /// caller branches on <see cref="NearbyConnectException.Reason"/> for every connect outcome
    /// instead of on the exception type for one of them.
    /// </remarks>
    DeviceUnknown,
}
