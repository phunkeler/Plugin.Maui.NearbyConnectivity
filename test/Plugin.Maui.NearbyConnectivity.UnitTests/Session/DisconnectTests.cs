namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// The drop is reported exactly once, whichever side ended it.
/// </summary>
[Trait("Category", "Session")]
public sealed class DisconnectTests
{
    [Fact]
    public async Task RemoteDisconnect_RaisesConnectionDroppedExactlyOnce()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        var connection = Create.Connection(device);
        connections.ConnectResult = connection;

        await device.ConnectAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);

        // Act
        await connection.CloseAsync();

        // The registry is written before subscribers are published to, so StatusOf can already
        // read Visible while the recorder's channel is still undrained. Wait on the recorder.
        await Wait.UntilAsync(() => recorder.StatusesFor("peer-1").Any());

        // Assert
        Assert.Single(recorder.StatusesFor("peer-1"));
        Assert.Equal(NearbyDeviceStatus.Visible, session.StatusOf("peer-1"));
    }

    [Fact]
    public async Task DisposeAsync_AfterADisconnect_LeavesTheRegistryEmpty()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        var connection = Create.Connection(device);
        connections.ConnectResult = connection;

        await device.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        // StopAsync disposes each live connection, and the platform's own table clears
        // on release, so the disconnect watcher wakes mid-disposal and calls ResetToVisible
        // after _registry.Clear() has already run.
        await session.DisposeAsync();

        // Give the watcher every chance to run and re-add the row.
        await Wait.UntilAsync(() => session.Devices.Count > 0);

        // Assert
        // A watcher waking after disposal must not resurrect a row. Registry.Update returns
        // early for an absent id, which is what makes this hold — keep it that way.
        Assert.Empty(session.Devices);
    }

    [Fact]
    public async Task DisconnectAsync_ReportsTheDropExactlyOnce()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        connections.ConnectResult = Create.Connection(device);

        await device.ConnectAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);

        // Act
        await device.DisconnectAsync();

        // The registry is written before subscribers are published to, so StatusOf can already
        // read Visible while the recorder's channel is still undrained. Wait on the recorder.
        await Wait.UntilAsync(() => recorder.StatusesFor("peer-1").Any());

        // Assert
        // A local disconnect must be reported exactly like a remote one.
        Assert.Single(recorder.StatusesFor("peer-1"));
    }

    [Fact]
    public async Task DisconnectAsync_WhenNotConnected_IsNoOp()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));

        // Act
        await device.DisconnectAsync();

        // Assert
        Assert.Equal(NearbyDeviceStatus.Visible, device.Status);
    }

    [Fact]
    public async Task ReturnsDeviceToVisible_WithTheEndReason()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        connections.ConnectResult = Create.Connection(device);
        await using var recorder = new ChangeRecorder(session);

        // Act
        await device.ConnectAsync(TestContext.Current.CancellationToken);
        await device.DisconnectAsync();
        await Wait.UntilAsync(() => recorder.For("peer-1").Any(c => c.Reason is not null));

        // Assert
        var ended = recorder.For("peer-1").Single(c => c.Reason is not null);
        Assert.Equal(NearbyDeviceStatus.Visible, ended.Device.Status);
        Assert.Equal(NearbyEndReason.ClosedLocally, ended.Reason);
    }

    // DiscoveryPageViewModel filters on "Visible or Connecting". A device left in any other
    // state after a drop silently vanishes from the discovery list — a capability regression
    // that no other test in this suite would catch.
    [Fact]
    public async Task DroppedDevice_RejoinsTheDiscoveryFilter()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        connections.ConnectResult = Create.Connection(device);

        // Act
        await device.ConnectAsync(TestContext.Current.CancellationToken);
        await device.DisconnectAsync();
        await Wait.UntilAsync(() => session.StatusOf("peer-1") is not NearbyDeviceStatus.Connected);

        // Assert
        Assert.True(
            session.StatusOf("peer-1") is NearbyDeviceStatus.Visible or NearbyDeviceStatus.Connecting,
            $"A dropped device must rejoin the discovery filter, but was {session.StatusOf("peer-1")}.");
    }

    // Cancelled, TimedOut and Failed are branched per exception type rather than defaulted, so
    // a caller that withdraws is not reported as a failure. Discovery is on: with it off the
    // device would be dropped rather than left Visible, which is the advertiser-only case.
    [Fact]
    public async Task ConnectAsync_Cancelled_LeavesDeviceVisible()
    {
        // Arrange
        var connections = new FakeNearby { ConnectFault = new OperationCanceledException() };
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));

        // Act
        await Assert.ThrowsAsync<OperationCanceledException>(() => device.ConnectAsync(TestContext.Current.CancellationToken));

        // Assert
        // A cancelled handshake must not strand the row on Connecting.
        Assert.Equal(NearbyDeviceStatus.Visible, session.StatusOf("peer-1"));
    }

    [Fact]
    public async Task DisconnectAsync_LeavesOtherConnectionsIntact()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        var alice = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        var bob = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-2", NearbyPresence.FromRemote("Bob", default)));

        connections.ConnectResult = Create.Connection(alice);
        await alice.ConnectAsync(TestContext.Current.CancellationToken);
        connections.ConnectResult = Create.Connection(bob);
        await bob.ConnectAsync(TestContext.Current.CancellationToken);

        await alice.DisconnectAsync();

        // Alice's entry is removed on the Disconnected continuation, not inside DisconnectAsync
        // — the same asynchrony documented by StopAsync_ClearingDeviceState_IsNotSynchronous.

        // Act
        await Wait.UntilAsync(() => session.ConnectionOf(alice.Id) is null);

        // Assert
        Assert.Equal(NearbyDeviceStatus.Connected, session.StatusOf("peer-2"));
        Assert.True(
            session.ConnectionOf(bob.Id) is not null,
            "Disconnecting one device must not tear down the others.");
    }
}
