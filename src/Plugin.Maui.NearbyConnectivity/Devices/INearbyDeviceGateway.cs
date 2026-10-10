namespace Plugin.Maui.NearbyConnectivity;

// The session capability a NearbyDevice acts through, so a device row carries its own verbs
// instead of every consumer being handed the facade to look one up. NearbyConnectionRequest acts
// through the same gateway, for the same reason.
//
// Deliberately narrower than INearbyConnectivity: holding the whole facade would let a row reach
// members that have no meaning on a single device.
//
// Every device member is id-keyed rather than device-keyed, which is what makes a stale snapshot
// safe — the session resolves the id at the moment of the call. The two request members are keyed
// on the request instance instead, because the claim they settle must not take a newer request
// that replaced this one for the same device.
interface INearbyDeviceGateway
{
    Task<NearbyConnection> ConnectAsync(string deviceId, CancellationToken cancellationToken = default);

    Task DisconnectAsync(string deviceId);

    bool TryGetConnection(string deviceId, [NotNullWhen(true)] out NearbyConnection? connection);

    bool TryGetRequest(string deviceId, [NotNullWhen(true)] out NearbyConnectionRequest? request);

    bool TryGetConnectDeadline(string deviceId, [NotNullWhen(true)] out DateTimeOffset? deadline);

    Task<NearbyConnection> AcceptRequestAsync(
        NearbyConnectionRequest request,
        CancellationToken cancellationToken = default);

    Task DeclineRequestAsync(
        NearbyConnectionRequest request,
        CancellationToken cancellationToken = default);
}
