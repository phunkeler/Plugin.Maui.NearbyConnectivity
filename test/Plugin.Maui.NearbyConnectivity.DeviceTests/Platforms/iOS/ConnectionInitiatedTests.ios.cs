namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// First leg of an inbound connection on iOS: <c>DidReceiveInvitationFromPeer</c> must surface a
/// <see cref="NearbyConnectionRequest"/> on the advertise channel, with the offer's deadline
/// derived from the connect-request frame in the invitation context — or from the default window
/// when the context is absent (a legacy peer). Exercised against the real platform partial with
/// real SDK callback types — no live radio.
/// </summary>
public sealed class ConnectionInitiatedTests : DeviceTest
{
    [Fact]
    public async Task IncomingInvitation_WithNoContext_YieldsRequestWithTheDefaultWindow()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var before = DateTimeOffset.UtcNow;

        // Act
        platform.Ios().DidReceiveInvitationFromPeer(
            advertiser: null!,
            peerID: peerID,
            context: null,
            invitationHandler: (_, _) => { });

        // Assert
        var request = await platform._advertiseChannel.Reader.ReadAsync(cts.Token);
        Assert.Equal("Alice", request.RemoteDevice.Presence.DisplayName);
        Assert.InRange(
            request.ExpiresAt,
            before + OfferWindowPolicy.s_default,
            DateTimeOffset.UtcNow + OfferWindowPolicy.s_default);
    }

    [Fact]
    public async Task IncomingInvitation_WithAFrameContext_YieldsRequestWithTheDeclaredWindow()
    {
        // Arrange
        // The 9-byte window-only frame, as a new-version initiator sends it. The name
        // rides MCPeerID natively, so the display name still comes from the peer id.
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var window = TimeSpan.FromSeconds(2);
        using var context = NSData.FromArray(
            ControlMessage.EncodeConnectRequest(window, new() { DisplayName = "Alice" }));
        var before = DateTimeOffset.UtcNow;

        // Act
        platform.Ios().DidReceiveInvitationFromPeer(
            advertiser: null!,
            peerID: peerID,
            context: context,
            invitationHandler: (_, _) => { });

        // Assert
        // The clamp itself is shared code, pinned by the unit suite; the device value
        // here is only that the context reaches the adapter through the real SDK type.
        var request = await platform._advertiseChannel.Reader.ReadAsync(cts.Token);
        Assert.Equal("Alice", request.RemoteDevice.Presence.DisplayName);
        Assert.InRange(request.ExpiresAt, before + window, DateTimeOffset.UtcNow + window);
    }

    [Fact]
    public async Task Invitation_WithAFrame_CarriesTheRequestersNameAndInfo()
    {
        // iOS now encodes its full presence in the context, the same body Android sends.

        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var info = "route:R17"u8.ToArray();

        using var context = NSData.FromArray(ControlMessage.EncodeConnectRequest(
            TimeSpan.FromSeconds(30), new() { DisplayName = "Bob", Info = info }));

        // Act
        platform.Ios().DidReceiveInvitationFromPeer(
            advertiser: null!, peerID: peerID, context: context, invitationHandler: (_, _) => { });

        // Assert
        var request = await platform._advertiseChannel.Reader.ReadAsync(cts.Token);
        Assert.Equal("Bob", request.RemoteDevice.Presence.DisplayName);
        Assert.Equal(info, request.RemoteDevice.Presence.Info.ToArray());
    }
}
