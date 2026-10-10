namespace Plugin.Maui.NearbyConnectivity.DeviceTests;

/// <summary>
/// Reaches the platform adapter behind a transport, typed — the device tests drive the adapter's
/// SDK-typed callback entry points directly.
/// </summary>
static class AdapterAccess
{
    public static AndroidAdapter Android(this NearbyTransport transport) => (AndroidAdapter)transport.Adapter;
}
