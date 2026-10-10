namespace Plugin.Maui.NearbyConnectivity;

sealed partial class PeerLookup
{
    readonly ConcurrentDictionary<MCPeerID, string> _deviceIdByPeer = new();
    readonly ConcurrentDictionary<string, MCPeerID> _peerByDeviceId = new(StringComparer.Ordinal);

    internal NearbyDevice Record(MCPeerID peerID, ReadOnlyMemory<byte> info = default)
    {
        var key = DeviceIdFor(peerID);

        var device = Record(key, NearbyText.Truncate(peerID.DisplayName), info);

        NearbyLog.PeerTracked(Logger, key);

        return device;
    }

    internal bool TryGetPeerId(string deviceId, [NotNullWhen(true)] out MCPeerID? peerID)
        => _peerByDeviceId.TryGetValue(deviceId, out peerID);

    // The factory stays pure: GetOrAdd may run it twice under a race and keep one result, and a
    // handle written by the losing run would be disposed twice by PlatformClear.
    internal string DeviceIdFor(MCPeerID peerID)
    {
        var deviceId = _deviceIdByPeer.GetOrAdd(peerID, static _ => MintDeviceId());
        _peerByDeviceId.TryAdd(deviceId, peerID);

        return deviceId;
    }

    partial void PlatformRemove(string deviceId)
    {
        NearbyLog.PeerUntracked(Logger, deviceId);

        if (_peerByDeviceId.TryRemove(deviceId, out var handle))
        {
            _deviceIdByPeer.TryRemove(handle, out _);
            handle.Dispose();
        }
    }

    partial void PlatformClear()
    {
        foreach (var (key, _) in _peerByDeviceId)
        {
            PlatformRemove(key);
        }

        _deviceIdByPeer.Clear();
    }
}
