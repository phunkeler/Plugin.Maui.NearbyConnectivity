namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// Peer disconnection on Android (<c>OnDisconnected</c>): the connection's
/// <see cref="NearbyConnection.Closed"/> task completes and the platform's bookkeeping for
/// the peer is released.
/// </summary>
public sealed class DisconnectTests : DeviceTest
{
    [Fact]
    public async Task RemoteDisconnect_CompletesDisconnectedTaskAndReleasesPeer()
    {
        // Arrange
        // A live connection established through the real callback path.
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (connection, endpointId, deviceId) = await Create.ConnectedAsync(platform, "Alice", cts.Token);

        // Act
        platform.Android().OnDisconnected(endpointId);

        // Assert
        await connection.Closed.WaitAsync(cts.Token);
        Assert.False(platform.Connections.IsConnected(deviceId));
        Assert.False(platform.PeerLookup.TryGetDevice(deviceId, out _));
    }
}
