namespace Plugin.Maui.NearbyConnectivity.UiTests.Pages;

public sealed class AvailabilityPage : CapabilityPageObject
{
    const string Current = "current";
    const string Checked = "checked";
    const string Check = "check";

    internal AvailabilityPage(AppSession session)
        : base(session, "availability")
    {
    }

    private protected override string LoadedMarker => Id(Check);

    /// <summary>The cached snapshot, as AvailabilityChanged last reported it.</summary>
    public string Current_ => Session.Text(Id(Current));

    /// <summary>Forces a fresh read and waits for the result.</summary>
    public string CheckAvailability()
    {
        Session.Tap(Id(Check));
        return Session.WaitForText(Id(Checked), t => t is not "-");
    }
}
