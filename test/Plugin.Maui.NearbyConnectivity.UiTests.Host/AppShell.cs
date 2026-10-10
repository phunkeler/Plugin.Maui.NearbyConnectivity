using Plugin.Maui.NearbyConnectivity.UiTests.Host.Pages;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Host;

public sealed class AppShell : Shell
{
    public AppShell()
    {
        // A flyout would need a gesture to open before any test could navigate. Tabs are visible
        // and tappable from a cold start, which keeps AppShellNav a single tap per page.
        var tabs = new TabBar();

        tabs.Items.Add(Tab<PermissionsPage>("Perms", Routes.Permissions));
        tabs.Items.Add(Tab<AvailabilityPage>("Avail", Routes.Availability));
        tabs.Items.Add(Tab<AdvertisingPage>("Advertise", Routes.Advertising));
        tabs.Items.Add(Tab<DiscoveryPage>("Discover", Routes.Discovery));
        tabs.Items.Add(Tab<ConnectionsPage>("Connect", Routes.Connections));
        tabs.Items.Add(Tab<PayloadsPage>("Payloads", Routes.Payloads));

        Items.Add(tabs);
    }

    static ShellContent Tab<TPage>(string title, string route)
        where TPage : Page
        => new()
        {
            Title = title,
            Route = route,
            // AutomationId on ShellContent is not surfaced on the rendered tab; tests select tabs
            // by their title text instead. See AppShellNav in the test project.
            ContentTemplate = new DataTemplate(typeof(TPage)),
        };

    public static class Routes
    {
        public const string Permissions = "permissions";
        public const string Availability = "availability";
        public const string Advertising = "advertising";
        public const string Discovery = "discovery";
        public const string Connections = "connections";
        public const string Payloads = "payloads";
    }
}
