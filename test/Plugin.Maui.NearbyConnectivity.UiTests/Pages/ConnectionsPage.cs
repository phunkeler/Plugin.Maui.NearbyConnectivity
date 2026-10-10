using System.Globalization;

using Plugin.Maui.NearbyConnectivity.UiTests.Components;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Pages;

public sealed class ConnectionsPage : CapabilityPageObject
{
    const string Count = "count";
    const string LastClosed = "last-closed";
    const string StopAllButton = "stop-all";
    const string RowPrefix = "connections-device";

    internal ConnectionsPage(AppSession session)
        : base(session, "connections")
    {
    }

    private protected override string LoadedMarker => Id(StopAllButton);

    public int ConnectionCount => int.Parse(Session.Text(Id(Count)), CultureInfo.InvariantCulture);

    /// <summary>The reason the most recent connection closed.</summary>
    public string LastClosed_ => Session.Text(Id(LastClosed));

    public bool HasConnection(string deviceId) => Session.Exists($"{RowPrefix}-row-{deviceId}");

    public DeviceRowComponent WaitForConnection(string deviceId, TimeSpan? timeout = null)
    {
        Session.WaitForElement($"{RowPrefix}-row-{deviceId}", timeout);
        return new DeviceRowComponent(Session, RowPrefix, deviceId);
    }

    /// <summary>Stops advertising, discovery and every connection, then waits for the list to drain.</summary>
    public void StopAll()
    {
        Session.Tap(Id(StopAllButton));
        Session.WaitForText(Id(Count), "0", TimeSpan.FromSeconds(30));
    }
}
