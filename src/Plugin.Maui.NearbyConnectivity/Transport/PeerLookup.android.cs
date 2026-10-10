namespace Plugin.Maui.NearbyConnectivity;

sealed partial class PeerLookup
{
    readonly ConcurrentDictionary<string, string> _deviceIdByEndpoint = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, string> _endpointByDeviceId = new(StringComparer.Ordinal);

    // The factory stays pure: GetOrAdd may run it twice under a race and keep one result, so the
    // reverse entry is written from the id that won.
    internal string DeviceIdFor(string endpointId)
    {
        var deviceId = _deviceIdByEndpoint.GetOrAdd(endpointId, static _ => MintDeviceId());
        _endpointByDeviceId.TryAdd(deviceId, endpointId);

        return deviceId;
    }

    internal bool TryGetEndpointId(string deviceId, [NotNullWhen(true)] out string? endpointId)
        => _endpointByDeviceId.TryGetValue(deviceId, out endpointId);

    partial void PlatformRemove(string deviceId)
    {
        if (_endpointByDeviceId.TryRemove(deviceId, out var endpointId))
        {
            _deviceIdByEndpoint.TryRemove(endpointId, out _);
        }
    }

    partial void PlatformClear()
    {
        _deviceIdByEndpoint.Clear();
        _endpointByDeviceId.Clear();
    }
}
