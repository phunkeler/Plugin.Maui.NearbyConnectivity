using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Discovery projected onto the Devices collection.
/// </summary>
[Trait("Category", "Session")]
public sealed class DiscoveryTests
{
    [Fact]
    public async Task DeviceFound_AddsToDevices()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Act
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        device = await connections.EmitDeviceFoundAsync(device);

        // Assert
        Assert.Single(session.Devices);
        Assert.Equal(device, session.Devices[0]);
        Assert.Equal(NearbyDeviceStatus.Visible, session.Devices[0].Status);
    }

    [Fact]
    public async Task DeviceFound_Twice_DoesNotDuplicate()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Act
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        device = await connections.EmitDeviceFoundAsync(device);
        device = await connections.EmitDeviceFoundAsync(device);

        // Assert
        Assert.Single(session.Devices);
    }

    [Fact]
    public async Task DeviceLost_RemovesVisibleDevice()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Act
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        device = await connections.EmitDeviceFoundAsync(device);
        await connections.EmitDeviceLostAsync(device);

        // Assert
        Assert.Empty(session.Devices);
    }

    [Fact]
    public async Task DeviceLost_DoesNotRemoveConnectedDevice()
    {
        // Going out of discovery range is not the same as disconnecting. Removing a connected
        // device here would delete a live conversation from the UI.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        device = await connections.EmitDeviceFoundAsync(device);

        connections.ConnectResult = Create.Connection(device);
        await device.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        await connections.EmitDeviceLostAsync(device);

        // Assert
        Assert.Single(session.Devices);
        Assert.Equal(NearbyDeviceStatus.Connected, session.StatusOf("peer-1"));
    }

    [Fact]
    public async Task StopAsync_Discover_DrainsVisibleDevices()
    {
        // Otherwise the UI shows devices that are no longer being looked for, forever.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        await connections.EmitDeviceFoundAsync(new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        await connections.EmitDeviceFoundAsync(new NearbyDevice("peer-2", NearbyPresence.FromRemote("Bob", default)));

        // Act
        await session.Discoverer.StopAsync();

        // Assert
        Assert.Empty(session.Devices);
    }

    [Fact]
    public async Task StopAsync_Discover_KeepsConnectedDevices()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        device = await connections.EmitDeviceFoundAsync(device);
        connections.ConnectResult = Create.Connection(device);
        await device.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        await session.Discoverer.StopAsync();

        // Assert
        Assert.Single(session.Devices);
    }

    [Fact]
    public async Task DeviceFound_CarriesThePeersInfoOntoDevices()
    {
        // An application can filter on app-defined presence info before connecting.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        var info = "route:R17"u8.ToArray();

        // Act
        await connections.EmitDeviceFoundAsync(Create.Device("peer-1", "Alice", info));

        // Assert
        Assert.Equal(info, session.Devices[0].Presence.Info.ToArray());
    }

    // -------------------------------------------------------------------------
    // a manual refresh evicts what the sweep did not re-report
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshAsync_EvictsDevicesTheSweepDidNotSee()
    {
        // "Out-of-range devices leave Devices once the sweep completes" — the manual path restarted
        // discovery and never swept, so pull-to-refresh did nothing for the thing it exists to do.

        // Arrange
        var time = new FakeTimeProvider();
        var platform = new FakeNearby();
        await using var session = Create.Session(platform, timeProvider: time);
        var token = TestContext.Current.CancellationToken;

        await session.Discoverer.StartAsync(token);
        await platform.EmitDeviceFoundAsync(Create.Device("gone"));

        Assert.NotNull(session.Current("gone"));

        // Act
        // The device is not re-reported by the new pass. The settle window runs on the
        // session's clock, so it is advanced while the refresh is in flight.
        var refresh = session.Discoverer.RefreshAsync(token);

        await Wait.UntilAsync(() => { time.Advance(TimeSpan.FromSeconds(1)); return refresh.IsCompleted; });
        await refresh;

        // Assert
        Assert.Null(session.Current("gone"));
    }
}
