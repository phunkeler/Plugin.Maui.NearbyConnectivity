namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// MPC session-state transitions beyond the shared success/failure cases:
/// <c>MCSessionState.Connecting</c> is optional and never a required waypoint, so a handshake that
/// waits for it can hang.
/// </summary>
public sealed class SessionStateTests : DeviceTest
{
    [Fact]
    public async Task Connecting_LeavesHandshakePending()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        var id = platform.PeerLookup.DeviceIdFor(peerID);
        var tcs = platform.Handshakes.Register(id, out _, deadline: null, attributedTo: CancellationToken.None);

        // Act
        platform.Ios().OnPeerStateChanged(peerID, MCSessionState.Connecting);

        // Assert
        // Connecting is informational; the handshake is neither resolved nor faulted.
        Assert.False(tcs.Task.IsCompleted);
    }

    [Fact]
    public async Task ConnectingThenNotConnected_FaultsHandshake()
    {
        // Arrange
        // The invitation-declined shape: Connecting arrives, then NotConnected.
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        var id = platform.PeerLookup.DeviceIdFor(peerID);
        var tcs = platform.Handshakes.Register(id, out _, deadline: null, attributedTo: CancellationToken.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        platform.Ios().OnPeerStateChanged(peerID, MCSessionState.Connecting);
        platform.Ios().OnPeerStateChanged(peerID, MCSessionState.NotConnected);

        // Assert
        // The concrete type, not the NearbyException base: ThrowsAsync is an exact-type match.
        await Assert.ThrowsAsync<NearbyConnectException>(() => tcs.Task.WaitAsync(cts.Token));
    }

    [Fact]
    public async Task FailedHandshake_WhileDiscovering_KeepsSessionAndLocalPeer()
    {
        // Arrange
        // The reported repro: an invite goes out and the handshake fails while discovery
        // is still running. The peer and session must survive: MPC rejects a fresh MCSession built
        // on a peer a disposed one used, and a browser rebuilt on a fresh peer re-mints every
        // remote device id. Both were seen on device; the session outliving the failure is the one
        // arrangement that does neither.
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var peerID = Create.PeerId("Alice");
        var run = await DiscoveryRun.StartAsync(platform, cts.Token);

        var device = platform.PeerLookup.Record(peerID);
        var before = platform.Ios().GetLocalPeerId();
        platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: CancellationToken.None);
        await platform.Ios().InitiateConnectAsync(device, cts.Token);

        // Act
        platform.Ios().OnPeerStateChanged(peerID, MCSessionState.NotConnected);

        // Assert
        // Same peer for the next invite; the retirement waits for discovery to stop.
        Assert.Same(before, platform.Ios().GetLocalPeerId());

        await run.DisposeAsync();

        Assert.NotSame(before, platform.Ios().GetLocalPeerId());
    }
}
