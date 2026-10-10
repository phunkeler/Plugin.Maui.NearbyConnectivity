namespace Plugin.Maui.NearbyConnectivity.UiTests.Pages;

public sealed class PermissionsPage : CapabilityPageObject
{
    const string Status = "status";
    const string Check = "check";
    const string Request = "request";

    internal PermissionsPage(AppSession session)
        : base(session, "permissions")
    {
    }

    private protected override string LoadedMarker => Id(Check);

    public string Status_ => Session.Text(Id(Status));

    /// <summary>Checks the current status and waits for it to be reported.</summary>
    public string CheckStatus()
    {
        Session.Tap(Id(Check));
        return Session.WaitForText(Id(Status), t => t is not "-");
    }

    /// <summary>Requests permission and waits for a terminal status.</summary>
    public string RequestPermission()
    {
        Session.Tap(Id(Request));
        return Session.WaitForText(Id(Status), t => t is not "-", TimeSpan.FromSeconds(30));
    }
}
