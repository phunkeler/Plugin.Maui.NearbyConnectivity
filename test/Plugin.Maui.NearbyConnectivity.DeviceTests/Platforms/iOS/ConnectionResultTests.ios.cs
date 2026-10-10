namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// The terminal "connection succeeded/failed" callback on iOS (<c>OnPeerStateChanged</c>). The
/// repo's documented invariant: every failure path must resolve or fault the pending
/// <c>_connectionTcs</c> entry, or <c>AcceptAsync</c>/<c>ConnectAsync</c> hang forever.
/// </summary>
public sealed class ConnectionResultTests : DeviceTest
{
    [Fact]
    public async Task Connected_ThenCodeExchange_ResolvesConnectionTcsAndRegistersActiveConnection()
    {
        // Arrange
        // Connected with NO prior Connecting: iOS does not guarantee that waypoint.
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        var (tcs, id) = Create.PendingHandshake(platform, peerID);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        platform.Ios().OnPeerStateChanged(peerID, MCSessionState.Connected);
        Deliver.CodeExchange(platform, peerID);

        // Assert
        var connection = await tcs.Task.WaitAsync(cts.Token);
        Assert.Equal("Alice", connection.RemoteDevice.Presence.DisplayName);
        Assert.True(platform.Connections.IsConnected(id));
    }

    [Fact]
    public async Task Connected_WithoutTheCodeExchange_LeavesTheHandshakePending()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        var (tcs, id) = Create.PendingHandshake(platform, peerID);

        // Act
        platform.Ios().OnPeerStateChanged(peerID, MCSessionState.Connected);

        // Assert
        Assert.False(tcs.Task.IsCompleted);
        Assert.False(platform.Connections.IsConnected(id));
    }

    [Fact]
    public async Task NotConnected_FaultsConnectionTcs()
    {
        // Arrange
        // Straight to NotConnected with no prior Connecting: the documented latent-hang path.
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        var (tcs, id) = Create.PendingHandshake(platform, peerID);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        platform.Ios().OnPeerStateChanged(peerID, MCSessionState.NotConnected);

        // Assert
        // The concrete type, not the NearbyException base: ThrowsAsync is an exact-type match.
        await Assert.ThrowsAsync<NearbyConnectException>(() => tcs.Task.WaitAsync(cts.Token));
        Assert.False(platform.Connections.IsConnected(id));
    }

    // A declined or failed invitation is not a lost peer. MPC keeps the browsed MCPeerID, sends no
    // LostPeer, and accepts a fresh invite on the same handle. Forgetting it here left the session
    // showing a Visible row whose handle was gone, so the next Connect failed with "No peer found".
    [Fact]
    public async Task NotConnected_OnFailedHandshake_KeepsThePeerReconnectable()
    {
        // Arrange
        // A discovered peer whose Found event is drained, then a failed handshake.
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var run = await DiscoveryRun.StartAsync(platform, cts.Token);
        platform.Ios().FoundPeer(browser: null!, peerID: peerID, info: null);
        await run.Events.ReadAsync(cts.Token);
        var (tcs, id) = Create.PendingHandshake(platform, peerID);

        // Act
        platform.Ios().OnPeerStateChanged(peerID, MCSessionState.NotConnected);

        // Assert
        // The handshake faults, the peer stays, and nothing is reported lost.
        await Assert.ThrowsAsync<NearbyConnectException>(() => tcs.Task.WaitAsync(cts.Token));
        Assert.True(platform.PeerLookup.TryGetPeerId(id, out _));
        Assert.False(run.Events.TryRead(out _));
    }
}
