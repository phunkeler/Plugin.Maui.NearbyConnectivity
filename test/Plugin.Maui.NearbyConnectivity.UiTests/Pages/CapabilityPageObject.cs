namespace Plugin.Maui.NearbyConnectivity.UiTests.Pages;

/// <summary>
/// Shared behaviour for every page object. Waiting is the page's job, never the test's.
/// </summary>
public abstract class CapabilityPageObject
{
    private protected CapabilityPageObject(AppSession session, string prefix)
    {
        Session = session;
        Prefix = prefix;
    }

    private protected AppSession Session { get; }

    private protected string Prefix { get; }

    /// <summary>The element whose presence means this page is on screen.</summary>
    private protected abstract string LoadedMarker { get; }

    /// <summary>The last failure this page surfaced, or "none".</summary>
    public string LastError => Session.Text($"{Prefix}-error");

    internal void WaitUntilLoaded() => Session.WaitForElement(LoadedMarker);

    private protected string Id(string element) => $"{Prefix}-{element}";
}
