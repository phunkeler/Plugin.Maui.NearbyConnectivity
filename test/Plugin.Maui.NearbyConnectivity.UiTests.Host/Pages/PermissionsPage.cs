namespace Plugin.Maui.NearbyConnectivity.UiTests.Host.Pages;

public sealed class PermissionsPage : CapabilityPage
{
    readonly Components.StateBadge _status;

    public PermissionsPage()
        : base("permissions")
    {
        Title = "Permissions";

        _status = AddBadge("status");
        Stack.Add(Error);

        AddButton("check", "Check", async () =>
            _status.Set((await Permissions.CheckStatusAsync<NearbyPermission>().ConfigureAwait(true)).ToString()));

        AddButton("request", "Request", async () =>
            _status.Set((await Permissions.RequestAsync<NearbyPermission>().ConfigureAwait(true)).ToString()));
    }
}
