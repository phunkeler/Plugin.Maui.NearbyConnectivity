using System.Globalization;

using Plugin.Maui.NearbyConnectivity.UiTests.Components;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Pages;

public sealed class DiscoveryPage : CapabilityPageObject
{
    const string State = "state";
    const string LastChange = "last-change";
    const string DeviceCount = "device-count";
    const string Start = "start";
    const string Stop = "stop";
    const string RefreshButton = "refresh";
    const string RowPrefix = "discovery-device";

    internal DiscoveryPage(AppSession session)
        : base(session, "discovery")
    {
    }

    private protected override string LoadedMarker => Id(Start);

    public bool IsDiscovering => string.Equals(Session.Text(Id(State)), "discovering", StringComparison.Ordinal);

    public string State_ => Session.Text(Id(State));

    public string LastChange_ => Session.Text(Id(LastChange));

    public int DiscoveredCount => int.Parse(Session.Text(Id(DeviceCount)), CultureInfo.InvariantCulture);

    public void StartDiscovering()
    {
        Session.Tap(Id(Start));
        WaitForOutcome("discovering");
    }

    public void StopDiscovering()
    {
        Session.Tap(Id(Stop));
        WaitForOutcome("idle");
    }

    public void Refresh() => Session.Tap(Id(RefreshButton));

    /// <summary>The row for a device, once it appears. Scoped to that device, not the screen.</summary>
    public DeviceRowComponent WaitForDevice(string deviceId, TimeSpan? timeout = null)
    {
        Session.WaitForElement($"{RowPrefix}-row-{deviceId}", timeout);
        return new DeviceRowComponent(Session, RowPrefix, deviceId);
    }

    public bool HasDevice(string deviceId) => Session.Exists($"{RowPrefix}-row-{deviceId}");

    void WaitForOutcome(string expected)
        => Session.WaitForText(
            Id(State),
            t => string.Equals(t, expected, StringComparison.Ordinal)
                || !string.Equals(LastError, "none", StringComparison.Ordinal));
}
