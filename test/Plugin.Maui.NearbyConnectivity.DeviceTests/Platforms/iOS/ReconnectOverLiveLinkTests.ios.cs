namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// A close on iOS sends the in-band disconnect frame and leaves the MultipeerConnectivity link up:
/// MPC has no per-peer disconnect, and the shared session must outlive the connection while a
/// browser or advertiser runs. A re-invite over that live link is answered silently — no state
/// change ever follows — so the second handshake travels in-band as control frames, and this is
/// the receiving half of each of them.
/// </summary>
public sealed class ReconnectOverLiveLinkTests : DeviceTest
{
    static CancellationTokenSource Timeout() => new(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task ConnectAcceptedFrame_ThenCodeExchange_ResolvesThePendingHandshake()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var alice = Create.PeerId("Alice");
        using var timeout = Timeout();
        var (tcs, id) = Create.PendingHandshake(platform, alice);

        // Act
        Deliver.ControlFrame(platform, alice, ControlMessageType.ConnectAccepted);
        Deliver.CodeExchange(platform, alice);

        // Assert
        var connection = await tcs.Task.WaitAsync(timeout.Token);
        Assert.Equal("Alice", connection.RemoteDevice.Presence.DisplayName);
        Assert.True(platform.Connections.IsConnected(id));
    }

    [Fact]
    public async Task ConnectRejectedFrame_FaultsThePendingHandshake()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var alice = Create.PeerId("Alice");
        using var timeout = Timeout();
        var (tcs, id) = Create.PendingHandshake(platform, alice);

        // Act
        Deliver.ControlFrame(platform, alice, ControlMessageType.ConnectRejected);

        // Assert
        var ex = await Assert.ThrowsAsync<NearbyConnectException>(() => tcs.Task.WaitAsync(timeout.Token));
        Assert.Equal(NearbyConnectFailure.Failed, ex.Reason);
        Assert.False(platform.Connections.IsConnected(id));
    }

    [Fact]
    public async Task RevealWithoutACommitment_FaultsThePendingHandshake()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var alice = Create.PeerId("Alice");
        using var timeout = Timeout();
        var (tcs, id) = Create.PendingHandshake(platform, alice);
        Deliver.ControlFrame(platform, alice, ControlMessageType.ConnectAccepted);

        // Act
        Deliver.Bytes(platform, alice, ControlMessage.EncodeCodeReveal(new byte[ControlMessage.CodeNonceBytes]));

        // Assert
        var ex = await Assert.ThrowsAsync<NearbyConnectException>(() => tcs.Task.WaitAsync(timeout.Token));
        Assert.Equal(NearbyConnectFailure.Failed, ex.Reason);
        Assert.False(platform.Connections.IsConnected(id));
    }

    [Fact]
    public async Task RevealThatDoesNotMatchItsCommitment_FaultsThePendingHandshake()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var alice = Create.PeerId("Alice");
        using var timeout = Timeout();
        var (tcs, id) = Create.PendingHandshake(platform, alice);
        Deliver.ControlFrame(platform, alice, ControlMessageType.ConnectAccepted);
        Deliver.Bytes(platform, alice, ControlMessage.EncodeCodeCommit(new byte[ControlMessage.CodeCommitmentBytes]));

        // Act
        Deliver.Bytes(platform, alice, ControlMessage.EncodeCodeReveal(new byte[ControlMessage.CodeNonceBytes]));

        // Assert
        await Assert.ThrowsAsync<NearbyConnectException>(() => tcs.Task.WaitAsync(timeout.Token));
        Assert.False(platform.Connections.IsConnected(id));
    }

    [Fact]
    public async Task Reconnect_KeepsTheCertificate_AndDerivesAFreshCode()
    {
        // Arrange
        // Accepting Alice's invitation mints the local identity; her certificate arrives
        // once, with the link. Bob's connection holds the shared session open across Alice's
        // disconnect, the way a running browser or advertiser does.
        await using var platform = Create.NearbyTransport();
        using var alice = Create.PeerId("Alice");
        using var bob = Create.PeerId("Bob");
        using var timeout = Timeout();
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        platform.Ios().DidReceiveInvitationFromPeer(
            advertiser: null!, peerID: alice, context: null, invitationHandler: (_, _) => answered.TrySetResult());
        var request = await platform._advertiseChannel.Reader.ReadAsync(timeout.Token);
        var accepting = request.AcceptAsync(timeout.Token);
        await answered.Task.WaitAsync(timeout.Token);
        platform.Ios().OnCertificateReceived([EphemeralIdentity.Create().Certificate], alice);
        platform.Ios().OnPeerStateChanged(alice, MCSessionState.Connected);
        Deliver.CodeExchange(platform, alice);
        var first = await accepting.WaitAsync(timeout.Token);
        await Create.ConnectedAsync(platform, bob, timeout.Token);
        Deliver.ControlFrame(platform, alice, ControlMessageType.Disconnect);
        var (tcs, _) = Create.PendingHandshake(platform, alice);

        // Act
        Deliver.ControlFrame(platform, alice, ControlMessageType.ConnectAccepted);
        Deliver.CodeExchange(platform, alice);

        // Assert
        var second = await tcs.Task.WaitAsync(timeout.Token);
        Assert.Matches("^[0-9]{6}$", first.AuthenticationCode);
        Assert.Matches("^[0-9]{6}$", second.AuthenticationCode);
        Assert.NotEqual(first.AuthenticationCode, second.AuthenticationCode);
    }

    [Fact]
    public async Task ADifferentCertificateOnTheLiveLink_VoidsTheCode()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var alice = Create.PeerId("Alice");
        using var timeout = Timeout();
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        platform.Ios().DidReceiveInvitationFromPeer(
            advertiser: null!, peerID: alice, context: null, invitationHandler: (_, _) => answered.TrySetResult());
        var request = await platform._advertiseChannel.Reader.ReadAsync(timeout.Token);
        var accepting = request.AcceptAsync(timeout.Token);
        await answered.Task.WaitAsync(timeout.Token);
        platform.Ios().OnCertificateReceived([EphemeralIdentity.Create().Certificate], alice);

        // Act
        platform.Ios().OnCertificateReceived([EphemeralIdentity.Create().Certificate], alice);
        platform.Ios().OnPeerStateChanged(alice, MCSessionState.Connected);
        Deliver.CodeExchange(platform, alice);

        // Assert
        var connection = await accepting.WaitAsync(timeout.Token);
        Assert.Equal("", connection.AuthenticationCode);
    }

    [Fact]
    public async Task ThreeSpentExchanges_RefuseTheNextHandshake()
    {
        // Arrange
        // Each failed round comes after this side revealed its nonce. Bob's connection
        // keeps the span open, as a running advertiser would.
        await using var platform = Create.NearbyTransport();
        using var alice = Create.PeerId("Alice");
        using var bob = Create.PeerId("Bob");
        using var timeout = Timeout();
        await Create.ConnectedAsync(platform, bob, timeout.Token);

        for (var i = 0; i < 3; i++)
        {
            var (spent, _) = Create.PendingHandshake(platform, alice);
            Deliver.ControlFrame(platform, alice, ControlMessageType.ConnectAccepted);
            Deliver.Bytes(platform, alice, ControlMessage.EncodeCodeCommit(new byte[ControlMessage.CodeCommitmentBytes]));
            Deliver.Bytes(platform, alice, ControlMessage.EncodeCodeReveal(new byte[ControlMessage.CodeNonceBytes]));
            await Assert.ThrowsAsync<NearbyConnectException>(() => spent.Task.WaitAsync(timeout.Token));
        }

        var (tcs, _) = Create.PendingHandshake(platform, alice);

        // Act
        Deliver.ControlFrame(platform, alice, ControlMessageType.ConnectAccepted);

        // Assert
        await Assert.ThrowsAsync<NearbyConnectException>(() => tcs.Task.WaitAsync(timeout.Token));
    }

    [Fact]
    public async Task ConnectRequestFrame_WritesAConnectionRequestForThePeer()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var alice = Create.PeerId("Alice");
        using var timeout = Timeout();

        // Act
        Deliver.Bytes(platform, alice, ControlMessage.EncodeConnectRequest(TimeSpan.FromSeconds(1), new() { DisplayName = "Alice" }));

        // Assert
        var request = await platform._advertiseChannel.Reader.ReadAsync(timeout.Token);
        Assert.Equal(platform.PeerLookup.DeviceIdFor(alice), request.RemoteDevice.Id);
    }

    [Fact]
    public async Task ConnectRequestFrame_AcceptedWithNoLinkToAnswerOn_Faults()
    {
        // Arrange
        // The frame says the link is live, but this session holds no such peer, so the
        // in-band accept cannot be delivered. The acceptor must learn that, not sit on a handshake
        // the other side will never complete.
        await using var platform = Create.NearbyTransport();
        using var alice = Create.PeerId("Alice");
        using var timeout = Timeout();
        Deliver.Bytes(platform, alice, ControlMessage.EncodeConnectRequest(TimeSpan.FromSeconds(1), new() { DisplayName = "Alice" }));
        var request = await platform._advertiseChannel.Reader.ReadAsync(timeout.Token);

        // Act
        var pending = request.AcceptAsync(CancellationToken.None);

        // Assert
        var ex = await Assert.ThrowsAsync<NearbyConnectException>(() => pending);
        Assert.Equal(NearbyConnectFailure.Failed, ex.Reason);
        Assert.True(platform.Handshakes.IsEmpty);
    }
}
