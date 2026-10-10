namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// On iOS, AppLifecycleObserver calls StopAsync when the app enters the background, because
/// MultipeerConnectivity does not survive suspension. The observer needs UIKit, so these pin the
/// contract it depends on instead: StopAsync alone produces every consumer-visible transition that
/// backgrounding requires.
/// </summary>
[Trait("Category", "Session")]
public sealed class BackgroundTeardownTests
{
    [Fact]
    public async Task StopAsync_RaisesConnectionDropped_ForEveryLiveConnection()
    {
        // The zombie-Connected bug: without this, a consumer backgrounded mid-conversation
        // is never told the connection ended, because iOS tears MPC down silently and with
        // no NSError. The device change is the only signal it will ever get.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        connections.ConnectResult = Create.Connection(device);

        await device.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NearbyDeviceStatus.Connected, session.StatusOf("peer-1"));

        await using var recorder = new ChangeRecorder(session);

        await session.StopAllAsync();

        // Two independent async paths settle here: the registry publishes Removed, and the
        // platform's connection table clears once Disconnected completes (the release path).
        // Waiting on the change alone races the second, so wait for both.
        await Wait.UntilAsync(() => recorder.For("peer-1")
            .Any(c => c.Action is NearbyDeviceChangeAction.Removed)
            && session.ConnectionOf("peer-1") is null);

        // Removal is how a stopped session reports the device is gone; the connection going
        // away is what the backgrounded consumer must be able to observe.

        // Act
        var removals = recorder.For("peer-1")
            .Where(c => c.Action is NearbyDeviceChangeAction.Removed)
            .ToArray();

        // Assert
        Assert.Single(removals);
        Assert.Null(session.ConnectionOf("peer-1"));
    }

    [Fact]
    public async Task StopAsync_ClearsConnectedState_SoNoDeviceIsLeftReportingConnected()
    {
        // Devices is the state consumers bind to. A row still reading Connected after the OS
        // ended the session is precisely the state this fix exists to eliminate.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        connections.ConnectResult = Create.Connection(device);
        await device.ConnectAsync(TestContext.Current.CancellationToken);

        await session.StopAllAsync();

        Assert.Empty(session.Devices);

        // Awaited, not asserted outright: the per-device clear happens in WatchDisconnectAsync,
        // which observes the connection's Disconnected task and therefore lands after StopAsync
        // returns. See StopAsync_ClearingDeviceState_IsNotSynchronous for why that ordering is
        // load-bearing on iOS rather than an incidental detail.

        // Act
        await Wait.UntilAsync(() =>
            device.Status is NearbyDeviceStatus.Visible && session.ConnectionOf(device.Id) is null);

        // Assert
        Assert.NotEqual(NearbyDeviceStatus.Connected, device.Status);
        Assert.True(
            session.ConnectionOf(device.Id) is null,
            "A cleared device must not still resolve to a dead connection.");
    }

    [Fact]
    public async Task StopAsync_ClearingDeviceState_IsNotSynchronous()
    {
        // Documents a real hazard rather than asserting desired behaviour. StopAsync clears the
        // Devices collection synchronously, but per-device Status and Role, and the connection
        // lookup entry, are cleared by WatchDisconnectAsync, which runs as a continuation on the
        // connection's Disconnected task. So immediately after StopAsync returns, a device the
        // caller still holds a reference to can briefly report Connected with a resolvable
        // connection.
        //
        // Consumers binding to Devices never see this — the row is already gone. It matters on
        // iOS: AppLifecycleObserver cannot await teardown (UIKit gives seconds before
        // suspension), so the process may suspend before this continuation runs. That is
        // acceptable because iOS has already destroyed the transport and the state is rebuilt
        // from scratch on foreground — but if this ever needs to be synchronous, this test is
        // the one that will fail and say why.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        connections.ConnectResult = Create.Connection(device);
        await device.ConnectAsync(TestContext.Current.CancellationToken);

        await session.StopAllAsync();

        // The collection consumers bind to is cleared synchronously.
        Assert.Empty(session.Devices);
        // Wait on every condition asserted below, not just the first. Status and the connection
        // lookup are cleared on the same continuation but not atomically, so polling one and
        // asserting the other can observe the gap between them.

        // Act
        await Wait.UntilAsync(() =>
            device.Status is NearbyDeviceStatus.Visible && session.ConnectionOf(device.Id) is null);

        // Assert
        // Per-device state is cleared, just asynchronously.
        Assert.Equal(NearbyDeviceStatus.Visible, device.Status);
        Assert.True(
            session.ConnectionOf(device.Id) is null,
            "The connection lookup is cleared on the same continuation.");
    }

    [Fact]
    public async Task StopAsync_ClearsBothToggles_SoNeitherReportsScanningWhileSuspended()
    {
        // The second zombie state. While suspended nothing is advertising or scanning, so
        // leaving these true would misreport the radio just as Connected misreported the session.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await session.StopAllAsync();

        // Assert
        Assert.False(session.Advertiser.IsActive);
        Assert.False(session.Discoverer.IsActive);
    }

    [Fact]
    public async Task StopAsync_LeavesSessionReusable_SoTheAppCanStartAgainOnForeground()
    {
        // Nothing restarts automatically: the app calls Start* again on foreground. That is
        // only viable if StopAsync leaves the session usable rather than terminally torn down.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        await session.StopAllAsync();
        Assert.False(session.Discoverer.IsActive);

        // Act
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.Discoverer.IsActive);
        // Restart must reach the platform, not be swallowed as a no-op.
        Assert.Equal(2, connections.DiscoverCallCount);
    }

    [Fact]
    public async Task StopAsync_IsIdempotent_SoARepeatedBackgroundNotificationIsHarmless()
    {
        // DidEnterBackground can arrive more than once across a suspend/resume cycle, and the
        // observer does not deduplicate — it relies on StopAsync being safe to call again.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        connections.ConnectResult = Create.Connection(device);
        await device.ConnectAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);

        await session.StopAllAsync();
        await session.StopAllAsync();
        await session.StopAllAsync();

        // Act
        await Wait.UntilAsync(() => recorder.For("peer-1")
            .Any(c => c.Action is NearbyDeviceChangeAction.Removed));

        // Assert
        // A device must be reported removed once per connection, not once per StopAsync call.
        Assert.Single(recorder.For("peer-1"), c => c.Action is NearbyDeviceChangeAction.Removed);
        Assert.False(session.Advertiser.IsActive);
        Assert.False(session.Discoverer.IsActive);
    }
}
