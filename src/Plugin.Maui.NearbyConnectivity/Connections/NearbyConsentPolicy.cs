namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// What the session does with an inbound connection request when no per-device decision is made.
/// </summary>
/// <seealso cref="INearbyAdvertiser.SetConsent"/>
public enum NearbyConsentPolicy
{
    /// <summary>
    /// The request stays outstanding for the application to answer. This is the default.
    /// </summary>
    /// <remarks>
    /// The device reports <see cref="NearbyDeviceStatus.RequestReceived"/> and carries the request
    /// on <see cref="NearbyDevice.Request"/>, answerable until it is accepted, declined, or its
    /// offer window elapses.
    /// </remarks>
    Hold,

    /// <summary>
    /// Every request the session receives is accepted.
    /// </summary>
    /// <remarks>
    /// The device never reports <see cref="NearbyDeviceStatus.RequestReceived"/>; it moves
    /// straight to <see cref="NearbyDeviceStatus.Connected"/> on
    /// <see cref="INearbyConnectivity.DeviceChanged"/>.
    /// </remarks>
    AcceptAll,

    /// <summary>
    /// Every request the session receives is declined.
    /// </summary>
    RejectAll,
}
