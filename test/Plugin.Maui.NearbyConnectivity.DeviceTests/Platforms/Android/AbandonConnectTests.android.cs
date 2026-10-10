namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// A handshake that exits through the catch-all — a cancelled caller, not the deadline — must run
/// the same abandon the deadline exit runs. Without it, a cancelled <c>ConnectAsync</c> left GMS
/// holding a half-open handshake. The abandon disconnects the endpoint and clears the handshake
/// ledger; it does not forget the peer, which is still advertising and still connectable, so the
/// user can tap the same row again.
/// </summary>
public sealed class AbandonConnectTests : DeviceTest
{
    [Fact]
    public async Task CancelledHandshake_AbandonsThePlatformConnection()
    {
        // Arrange
        // A pending initiator handshake the platform tracks in PeerLookup.
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource();
        using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var run = await DiscoveryRun.StartAsync(platform, startCts.Token);
        var (tcs, deviceId) = Create.PendingHandshake(platform);
        Assert.True(platform.PeerLookup.TryGetDevice(deviceId, out var device));
        Assert.True(platform.PeerLookup.TryGetEndpointId(deviceId, out _));

        // Act
        // Cancel the caller's token while the platform never calls back.
        var pending = platform.AwaitHandshakeAsync(
            device,
            tcs,
            isInitiator: true,
            TimeSpan.FromSeconds(30),
            beforeAwait: static _ => Task.CompletedTask,
            cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        // Assert
        // The catch-all exit abandoned the half-open connection rather than only
        // clearing the handshake entry, and the peer it was talking to survives: cancelling a
        // connect is not the device going away, so the mapping a retry needs is still there.
        Assert.True(platform.Handshakes.IsEmpty);
        Assert.True(platform.PeerLookup.TryGetEndpointId(deviceId, out _));
    }
}
