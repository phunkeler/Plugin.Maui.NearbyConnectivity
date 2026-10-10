namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Session")]
public sealed class WatcherHazardTests
{
    [Fact]
    public async Task EnumeratingDevices_WhileCollectionMutates_DoesNotThrow()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        for (var i = 0; i < 50; i++)
        {
            await connections.EmitDeviceFoundAsync(new NearbyDevice($"peer-{i}", NearbyPresence.FromRemote($"Device {i}", default)));
        }

        var mutating = Task.Run(async () =>
        {
            for (var i = 50; i < 150; i++)
            {
                await connections.EmitDeviceFoundAsync(new NearbyDevice($"peer-{i}", NearbyPresence.FromRemote($"Device {i}", default)));
            }
        }, TestContext.Current.CancellationToken);

        // Act
        // A consumer snapshotting the collection must not observe a torn enumeration.
        for (var pass = 0; pass < 50; pass++)
        {
            _ = session.Devices.ToArray().Length;
        }

        await mutating;

        // Assert
        Assert.True(session.Devices.Count >= 150);
    }

    [Fact]
    public async Task WatchCycles_LeaveTheSurvivingWatcherReceivingExactlyOnce()
    {
        // Five enter-and-leave page visits, each unsubscribing as it leaves.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        for (var visit = 0; visit < 5; visit++)
        {
            await using var transient = new ChangeRecorder(session);
        }

        // Discovered before the sixth visit starts watching, so the only change the recorder
        // sees is the connect.
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));

        // Sixth visit: still watching when the change happens.
        await using var recorder = new ChangeRecorder(session);

        // Act
        connections.ConnectResult = Create.Connection(device);
        await device.ConnectAsync(TestContext.Current.CancellationToken);
        await recorder.WaitForAsync("peer-1", 1);

        // Assert
        // Each change must reach a live watcher once, not once per past page visit.
        Assert.Single(recorder.StatusesFor("peer-1"), st => st is NearbyDeviceStatus.Connected);
    }

    [Fact]
    public async Task StopAsync_RejectsOutstandingRequests()
    {
        // Otherwise the remote device waits on a request nobody will ever answer.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        var rejected = false;
        await connections.EmitRequestAsync(
            device,
            () => Create.Connection(device),
            onReject: () => rejected = true);

        // Act
        await session.StopAllAsync();

        // Assert
        Assert.True(rejected);
        Assert.Empty(session.Devices);
    }
}
