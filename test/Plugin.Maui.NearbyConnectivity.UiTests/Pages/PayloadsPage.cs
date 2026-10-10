using System.Globalization;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Pages;

public sealed class PayloadsPage : CapabilityPageObject
{
    const string MaxBytes = "max-bytes";
    const string LastReceived = "last-received";
    const string ReceivedCount = "received-count";
    const string Progress = "progress";
    const string Text = "text";
    const string SendBytes = "send-bytes";
    const string SendFile = "send-file";
    const string SendLargeFile = "send-large-file";
    const string CancelInbound = "cancel-inbound";
    const string InboundStatus = "inbound-status";

    internal PayloadsPage(AppSession session)
        : base(session, "payloads")
    {
    }

    private protected override string LoadedMarker => Id(SendBytes);

    /// <summary>The library's published byte-payload ceiling, as the app reports it.</summary>
    public int MaxBytesPayloadLength
        => int.Parse(Session.Text(Id(MaxBytes)), CultureInfo.InvariantCulture);

    public string LastReceived_ => Session.Text(Id(LastReceived));

    public int ReceivedCount_ => int.Parse(Session.Text(Id(ReceivedCount)), CultureInfo.InvariantCulture);

    public string Progress_ => Session.Text(Id(Progress));

    public void EnterText(string value) => Session.Fill(Id(Text), value);

    /// <summary>Taps send and waits for the attempt to resolve into either an error or silence.</summary>
    public void SendBytesPayload() => TapAndSettle(SendBytes);

    public void SendFilePayload() => TapAndSettle(SendFile);

    public void SendLargeFilePayload() => TapAndSettle(SendLargeFile);

    /// <summary>The latest inbound transfer snapshot, as <c>Status:BytesTransferred</c>, or "idle".</summary>
    public string InboundStatus_ => Session.Text(Id(InboundStatus));

    /// <summary>Cancels the latest inbound transfer the page saw. Does nothing when it saw none.</summary>
    public void CancelInboundTransfer() => TapAndSettle(CancelInbound);

    /// <summary>Waits for a payload to arrive, returning its rendered description.</summary>
    public string WaitForReceived(TimeSpan? timeout = null)
        => Session.WaitForText(Id(LastReceived), t => t is not "-", timeout);

    /// <summary>
    /// Taps a send button and waits for the send to resolve. The page clears its error label to
    /// "none" before each attempt and sets it on failure, so a failing send is observable as a
    /// transition away from "none". A successful send leaves it at "none" for the whole window,
    /// which is why this returns quietly on timeout rather than throwing — the caller asserts on
    /// LastError to say which outcome it expected.
    /// </summary>
    void TapAndSettle(string button)
    {
        Session.Tap(Id(button));

        try
        {
            Session.WaitForText(
                $"{Prefix}-error",
                t => !string.Equals(t, "none", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));
        }
        catch (WebDriverTimeoutException)
        {
            // No error appeared: the send succeeded, or is still in flight.
        }
    }
}
