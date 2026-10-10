namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// Peer disconnection on iOS (<c>OnPeerStateChanged(NotConnected)</c> against a live connection):
/// the connection's <see cref="NearbyConnection.Closed"/> task completes and the platform's
/// bookkeeping for the peer is released.
/// </summary>
public sealed class DisconnectTests : DeviceTest
{
    [Fact]
    public async Task RemoteDisconnect_CompletesDisconnectedTaskAndReleasesPeer()
    {
        // Arrange
        // A live connection established through the real callback path.
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (connection, id) = await Create.ConnectedAsync(platform, peerID, cts.Token);

        // Act
        platform.Ios().OnPeerStateChanged(peerID, MCSessionState.NotConnected);

        // Assert
        await connection.Closed.WaitAsync(cts.Token);
        Assert.False(platform.Connections.IsConnected(id));
        Assert.False(platform.PeerLookup.TryGetDevice(id, out _));
    }

    [Fact]
    public async Task LocalDisconnect_ReleasesTheConnectionButKeepsThePeerReconnectable()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var run = await DiscoveryRun.StartAsync(platform, cts.Token);
        var (connection, id) = await Create.ConnectedAsync(platform, peerID, cts.Token);

        // Act
        await connection.CloseAsync();

        // Assert
        Assert.False(platform.Connections.IsConnected(id));
        Assert.True(platform.PeerLookup.TryGetDevice(id, out _));
        Assert.True(platform.PeerLookup.TryGetPeerId(id, out _));
    }
}
