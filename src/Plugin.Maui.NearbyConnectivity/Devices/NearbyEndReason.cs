namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Specifies why a device left the device set, why a handshake ended, or why an established
/// connection ended. Carried by <see cref="NearbyDeviceChange.Reason"/>.
/// </summary>
public enum NearbyEndReason
{
    /// <summary>
    /// The local device declined an inbound connection request, through
    /// <see cref="NearbyConnectionRequest.DeclineAsync(CancellationToken)"/>.
    /// </summary>
    Declined,

    /// <summary>
    /// The caller withdrew before the handshake completed, by canceling the
    /// <see cref="CancellationToken"/> it passed.
    /// </summary>
    Canceled,

    /// <summary>
    /// The handshake did not complete within <see cref="NearbyOptions.OfferWindow"/> when this
    /// device initiated it, or before the offer's remaining window when it accepted.
    /// </summary>
    /// <remarks>
    /// This is distinct from <see cref="Failed"/> because the platforms differ: iOS has a native
    /// invitation timeout and Android has none, so the plugin supplies one on both. Collapsing the
    /// two would hide that asymmetry.
    /// </remarks>
    TimedOut,

    /// <summary>
    /// An inbound request was not answered before its offer deadline, so the plugin declined it.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="TimedOut"/>, which is an operation the caller was awaiting. Nothing
    /// awaits an outstanding request, so this reason reports a withdrawn offer rather than a failed
    /// call.
    /// </remarks>
    RequestExpired,

    /// <summary>
    /// The handshake failed for any other reason.
    /// </summary>
    Failed,

    /// <summary>
    /// An established connection ended without a local cause: the remote device closed it, or the
    /// link was lost. The platforms cannot tell those two apart, so neither does this plugin.
    /// </summary>
    PeerDisconnected,

    /// <summary>
    /// The local application ended the connection, through
    /// <see cref="NearbyDevice.DisconnectAsync"/> or
    /// <see cref="NearbyConnection.CloseAsync"/>.
    /// </summary>
    ClosedLocally,

    /// <summary>
    /// The session stopped, <see cref="INearbyConnectivity.StopAllAsync"/> or
    /// disposal, and ended the connection with it.
    /// </summary>
    SessionStopped,

    /// <summary>
    /// A discovery pass no longer reports the device, so it left the device set.
    /// </summary>
    LostFromDiscovery,

    /// <summary>
    /// The operating system suspended the application, so the session was torn down.
    /// </summary>
    /// <remarks>
    /// iOS only. Multipeer Connectivity does not survive suspension, so the plugin tears the
    /// session down on backgrounding rather than reporting connections it no longer has. Distinct
    /// from <see cref="SessionStopped"/>, which is the application asking: only one of the two is
    /// worth telling a user about, and only one is worth re-arming from.
    /// </remarks>
    AppSuspended,
}
