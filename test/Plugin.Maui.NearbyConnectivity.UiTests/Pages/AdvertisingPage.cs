using System.Globalization;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Pages;

public sealed class AdvertisingPage : CapabilityPageObject
{
    const string State = "state";
    const string LastChange = "last-change";
    const string RequestCount = "request-count";
    const string Start = "start";
    const string Stop = "stop";

    internal AdvertisingPage(AppSession session)
        : base(session, "advertising")
    {
    }

    private protected override string LoadedMarker => Id(Start);

    public bool IsAdvertising => string.Equals(Session.Text(Id(State)), "advertising", StringComparison.Ordinal);

    public string State_ => Session.Text(Id(State));

    /// <summary>The last transition, as "started" or "stopped:&lt;reason&gt;".</summary>
    public string LastChange_ => Session.Text(Id(LastChange));

    public int PendingRequestCount
        => int.Parse(Session.Text(Id(RequestCount)), CultureInfo.InvariantCulture);

    /// <summary>
    /// Starts advertising and waits for the attempt to resolve — either the state turns
    /// "advertising", or the page reports why it could not. Waiting for "idle" alone would return
    /// instantly, since that is the state before the tap.
    /// </summary>
    public void StartAdvertising()
    {
        Session.Tap(Id(Start));
        WaitForOutcome("advertising");
    }

    public void StopAdvertising()
    {
        Session.Tap(Id(Stop));
        WaitForOutcome("idle");
    }

    public bool HasRequestFrom(string deviceId) => Session.Exists($"advertising-request-row-{deviceId}");

    public void AcceptRequest(string deviceId) => Session.Tap($"advertising-request-accept-{deviceId}");

    public void DeclineRequest(string deviceId) => Session.Tap($"advertising-request-decline-{deviceId}");

    // A start can legitimately fail (permission missing, radio off), so this waits for either the
    // expected state or an error to appear, and lets the test decide which it wanted.
    void WaitForOutcome(string expected)
        => Session.WaitForText(
            Id(State),
            t => string.Equals(t, expected, StringComparison.Ordinal)
                || !string.Equals(LastError, "none", StringComparison.Ordinal));
}
