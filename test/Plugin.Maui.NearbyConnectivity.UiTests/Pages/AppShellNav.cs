namespace Plugin.Maui.NearbyConnectivity.UiTests.Pages;

/// <summary>
/// The only way to reach a page object. Each method returns its destination, so an illegal
/// sequence — asserting on advertising state while the discovery tab is open — cannot be written.
/// </summary>
public sealed class AppShellNav
{
    // Tab titles, from AppShell. Not AutomationIds: Shell does not surface one on the rendered tab,
    // but it does set the title as the tab's ContentDescription. See AppSession.TapTab.
    const string PermissionsTab = "Perms";
    const string AvailabilityTab = "Avail";
    const string AdvertisingTab = "Advertise";
    const string DiscoveryTab = "Discover";
    const string ConnectionsTab = "Connect";
    const string PayloadsTab = "Payloads";

    readonly AppSession _session;

    internal AppShellNav(AppSession session) => _session = session;

    public PermissionsPage GoToPermissions() => Go(PermissionsTab, s => new PermissionsPage(s));

    public AvailabilityPage GoToAvailability() => Go(AvailabilityTab, s => new AvailabilityPage(s));

    public AdvertisingPage GoToAdvertising() => Go(AdvertisingTab, s => new AdvertisingPage(s));

    public DiscoveryPage GoToDiscovery() => Go(DiscoveryTab, s => new DiscoveryPage(s));

    public ConnectionsPage GoToConnections() => Go(ConnectionsTab, s => new ConnectionsPage(s));

    public PayloadsPage GoToPayloads() => Go(PayloadsTab, s => new PayloadsPage(s));

    TPage Go<TPage>(string tabTitle, Func<AppSession, TPage> create)
        where TPage : CapabilityPageObject
    {
        _session.TapTab(tabTitle);

        var page = create(_session);
        page.WaitUntilLoaded();

        return page;
    }
}
