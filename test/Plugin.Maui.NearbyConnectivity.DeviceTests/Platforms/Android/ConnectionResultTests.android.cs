namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// The terminal "connection succeeded/failed" callback on Android (<c>OnConnectionResult</c>). The
/// repo's documented invariant: every failure path must resolve or fault the pending
/// <c>_connectionTcs</c> entry, or <c>AcceptAsync</c>/<c>ConnectAsync</c> hang forever.
/// </summary>
public sealed class ConnectionResultTests : DeviceTest
{
    [Fact]
    public async Task Success_ResolvesConnectionTcsAndRegistersActiveConnection()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        var (tcs, deviceId) = Create.PendingHandshake(platform);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        platform.Android().OnConnectionResult("endpoint-1", Create.Resolution());

        // Assert
        var connection = await tcs.Task.WaitAsync(cts.Token);
        Assert.Equal("Alice", connection.RemoteDevice.Presence.DisplayName);
        Assert.True(platform.Connections.IsConnected(deviceId));
    }

    [Fact]
    public async Task Failure_FaultsConnectionTcs()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        var (tcs, deviceId) = Create.PendingHandshake(platform);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        platform.Android().OnConnectionResult("endpoint-1", Create.Resolution(ConnectionsStatusCodes.StatusConnectionRejected));

        // Assert
        // The concrete type, not the NearbyException base: ThrowsAsync is an exact-type match, and
        // a rejected handshake reports the specific failure it was given.
        var ex = await Assert.ThrowsAsync<NearbyConnectException>(() => tcs.Task.WaitAsync(cts.Token));
        Assert.Equal(NearbyConnectFailure.Failed, ex.Reason);
        Assert.False(platform.Connections.IsConnected(deviceId));
    }

    // A rejected handshake leaves the peer discovered: GMS sends no EndpointLost for a peer that
    // merely declined, and the endpoint id still takes another requestConnection. Reporting the
    // device lost here would remove a row the user can legitimately tap again, and the retry would
    // then have no mapping to send on. Only OnEndpointLost and OnDisconnected forget a peer.
    [Fact]
    public async Task Failure_KeepsThePeerAndPublishesNoLostEvent()
    {
        // Arrange
        SkipUnlessRadioHardware();
        // A discovered endpoint whose Found event is drained, then a failed handshake.
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var run = await DiscoveryRun.StartAsync(platform, cts.Token);
        platform.Android().OnEndpointFound("endpoint-1", Create.DiscoveredEndpointInfo());
        await run.Events.ReadAsync(cts.Token);
        var (_, deviceId) = Create.PendingHandshake(platform);

        // Act
        platform.Android().OnConnectionResult("endpoint-1", Create.Resolution(ConnectionsStatusCodes.StatusConnectionRejected));

        // Assert
        Assert.True(platform.PeerLookup.TryGetDevice(deviceId, out _));
        Assert.False(run.Events.TryRead(out _));
    }

    // The peer surviving a rejection is only useful if the retry can actually be sent: the
    // endpoint mapping has to still be there for RespondToConnectionAsync and InitiateConnectAsync
    // to resolve. This is the half a lost-event assertion alone would not catch.
    [Fact]
    public async Task Failure_LeavesTheEndpointMappingIntactForARetry()
    {
        // Arrange
        SkipUnlessRadioHardware();
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var run = await DiscoveryRun.StartAsync(platform, cts.Token);
        platform.Android().OnEndpointFound("endpoint-1", Create.DiscoveredEndpointInfo());
        await run.Events.ReadAsync(cts.Token);
        var (_, deviceId) = Create.PendingHandshake(platform);

        // Act
        platform.Android().OnConnectionResult("endpoint-1", Create.Resolution(ConnectionsStatusCodes.StatusConnectionRejected));

        // Assert
        Assert.True(platform.PeerLookup.TryGetEndpointId(deviceId, out var endpointId));
        Assert.Equal("endpoint-1", endpointId);
    }
}
