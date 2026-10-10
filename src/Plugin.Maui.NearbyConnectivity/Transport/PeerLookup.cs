using System.Security.Cryptography;

namespace Plugin.Maui.NearbyConnectivity;

sealed partial class PeerLookup
{
    internal const int DeviceIdBytes = 8;

    readonly ConcurrentDictionary<string, NearbyDevice> _peers = new(StringComparer.Ordinal);

    internal required ILogger Logger { get; init; }

    // Set once by the session before any pump can run. It lives here because this is the one place
    // a device is built on either platform, which makes it the one layer that attaches the gateway:
    // a device reached through a connection or a request carries the same live verbs as one read
    // from the registry.
    internal INearbyDeviceGateway? Gateway { get; set; }

    internal static string MintDeviceId()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(DeviceIdBytes));

    internal NearbyDevice Record(string deviceId, string? displayName, ReadOnlyMemory<byte> info = default)
        => _peers.TryGetValue(deviceId, out var known)
            ? known
            : _peers.GetOrAdd(
                deviceId,
                static (id, state) => new NearbyDevice(id, state.Presence) { Gateway = state.Gateway },
                (Presence: NearbyPresence.FromRemote(displayName, info), Gateway));

    internal bool TryGetDevice(string deviceId, [NotNullWhen(true)] out NearbyDevice? device)
        => _peers.TryGetValue(deviceId, out device);

    internal NearbyDevice? Remove(string deviceId)
    {
        PlatformRemove(deviceId);

        return _peers.TryRemove(deviceId, out var device)
            ? device
            : null;
    }

    internal void Clear()
    {
        PlatformClear();
        _peers.Clear();
    }

    partial void PlatformRemove(string deviceId);

    partial void PlatformClear();
}
