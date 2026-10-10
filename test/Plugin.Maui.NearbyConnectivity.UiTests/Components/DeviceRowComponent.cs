namespace Plugin.Maui.NearbyConnectivity.UiTests.Components;

/// <summary>
/// One device row, scoped to its device rather than to the screen. The same widget appears on the
/// discovery and connections pages, so its element names are defined once, here.
/// </summary>
public sealed class DeviceRowComponent
{
    readonly AppSession _session;
    readonly string _prefix;

    internal DeviceRowComponent(AppSession session, string prefix, string deviceId)
    {
        _session = session;
        _prefix = prefix;
        DeviceId = deviceId;
    }

    public string DeviceId { get; }

    public string DisplayName => _session.Text($"{_prefix}-name-{DeviceId}");

    public string Status => _session.Text($"{_prefix}-status-{DeviceId}");

    public bool Exists => _session.Exists($"{_prefix}-row-{DeviceId}");

    /// <summary>Taps this row's action — "Connect" on discovery, "Disconnect" on connections.</summary>
    public void InvokeAction() => _session.Tap($"{_prefix}-action-{DeviceId}");
}
