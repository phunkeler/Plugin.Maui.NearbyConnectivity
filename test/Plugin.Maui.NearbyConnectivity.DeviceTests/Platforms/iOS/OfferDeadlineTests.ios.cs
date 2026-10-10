namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// The accept path's bound on iOS is the offer's remaining window — the deadline the initiator
/// declared in the invitation context. Multipeer Connectivity's native invitation timeout bounds
/// the <em>inviting</em> side only, so once this device calls the invitation handler nothing
/// platform-side ends the wait. The offer's deadline is what does.
/// </summary>
/// <remarks>
/// Real time, not a fake clock: these run against the real platform partial, so the declared
/// windows are deliberately short rather than injected.
/// </remarks>
public sealed class OfferDeadlineTests : DeviceTest
{
    /// <summary>An invitation whose context declares a one-second offer window.</summary>
    static NSData ShortWindowContext()
        => NSData.FromArray(ControlMessage.EncodeConnectRequest(TimeSpan.FromSeconds(1), new() { DisplayName = "Alice" }));

    [Fact]
    public async Task AcceptedInvitation_WithNoTerminalCallback_TimesOutAtTheOfferDeadline()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var context = ShortWindowContext();

        platform.Ios().DidReceiveInvitationFromPeer(
            advertiser: null!,
            peerID: peerID,
            context: context,
            invitationHandler: (_, _) => { });

        var request = await platform._advertiseChannel.Reader.ReadAsync(cts.Token);

        // Act
        // Accept, then never deliver OnPeerStateChanged.
        var pending = request.AcceptAsync(CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<NearbyConnectException>(() => pending);
    }

    [Fact]
    public async Task OfferDeadline_ClearsThePendingHandshakeEntry()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var context = ShortWindowContext();

        platform.Ios().DidReceiveInvitationFromPeer(
            advertiser: null!,
            peerID: peerID,
            context: context,
            invitationHandler: (_, _) => { });

        var request = await platform._advertiseChannel.Reader.ReadAsync(cts.Token);

        // Act
        await Assert.ThrowsAsync<NearbyConnectException>(
            () => request.AcceptAsync(CancellationToken.None));

        // Assert
        Assert.True(platform.Handshakes.IsEmpty);
    }

    // MPC's invitation handler resolves one pending invitation and is not documented as callable
    // twice, so the adapter latches it: whichever of accept, reject or expiry gets there first is
    // the only one that answers. An accept that later times out has already answered positively —
    // the deadline is then reported to the caller and the handshake abandoned, but the handler is
    // not invoked a second time to say otherwise.
    [Fact]
    public async Task OfferDeadline_AfterAccepting_DoesNotAnswerTheInvitationTwice()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var context = ShortWindowContext();
        var accepted = new List<bool>();

        platform.Ios().DidReceiveInvitationFromPeer(
            advertiser: null!,
            peerID: peerID,
            context: context,
            invitationHandler: (accept, _) => accepted.Add(accept));

        var request = await platform._advertiseChannel.Reader.ReadAsync(cts.Token);

        // Act
        // Accept, then let the offer window elapse with no terminal callback.
        await Assert.ThrowsAsync<NearbyConnectException>(
            () => request.AcceptAsync(CancellationToken.None));

        // Assert
        // Answered once, positively, and not again on the way out.
        Assert.Equal([true], accepted);
    }

    // The other side of the latch: an offer nobody accepted answers negatively when it expires,
    // which is what releases the remote device from waiting on a dead offer.
    [Fact]
    public async Task OfferDeadline_WithoutAccepting_AnswersTheInvitationNegatively()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var context = ShortWindowContext();
        var accepted = new List<bool>();

        platform.Ios().DidReceiveInvitationFromPeer(
            advertiser: null!,
            peerID: peerID,
            context: context,
            invitationHandler: (accept, _) => accepted.Add(accept));

        var request = await platform._advertiseChannel.Reader.ReadAsync(cts.Token);

        // Act
        await request.DeclineAsync(cts.Token);

        // Assert
        Assert.Equal([false], accepted);
    }
}
