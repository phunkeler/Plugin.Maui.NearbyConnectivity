using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Session")]
public sealed class ChangeStreamTests
{
    [Fact]
    public async Task StartAsync_Advertise_RaisesChangedWithIsActiveTrue()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        using var changes = Observe.Activity(session.Advertiser);

        // Act
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await changes.WaitForAsync(1);

        // Assert
        Assert.Equal([true], changes.Seen);
    }

    [Fact]
    public async Task StopAsync_Advertise_RaisesChangedWithARequestedStop()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        using var stops = Observe.Stops(session.Advertiser);

        // Act
        await session.Advertiser.StopAsync();
        await stops.WaitForAsync(1);

        // Assert
        var stop = Assert.Single(stops.Seen);
        Assert.Equal(NearbyActivityStopReason.Requested, stop?.Reason);
        Assert.False(session.Advertiser.IsActive);
    }

    [Fact]
    public async Task StopAsync_Advertise_WhenNotAdvertising_RaisesNothing()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        using var changes = Observe.Activity(session.Advertiser);

        // Act
        await session.Advertiser.StopAsync();
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await changes.WaitForAsync(1);

        // Assert
        Assert.Equal([true], changes.Seen);
    }

    [Fact]
    public async Task AdvertiseStartFailure_RaisesTwice_AndEndsInactive()
    {
        // Arrange
        var connections = new FakeNearby { AdvertiseFault = new NearbyException("nope") };
        var session = Create.Session(connections);
        using var changes = Observe.Activity(session.Advertiser);

        // Act
        await Assert.ThrowsAsync<NearbyException>(
            () => session.Advertiser.StartAsync(TestContext.Current.CancellationToken));
        await changes.WaitForAsync(2);

        // Assert
        Assert.Equal(2, changes.Seen.Count);
        Assert.False(changes.Seen[^1]);
        Assert.False(session.Advertiser.IsActive);
    }

    [Fact]
    public async Task AdvertiseFaultAfterSuccessfulStart_RecordsAFaultedStop_AndLeavesDiscoveryRunning()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        using var stops = Observe.Stops(session.Advertiser);

        // Act
        connections.FaultAdvertiseStream(new NearbyException("radio dropped"));
        await stops.WaitForAsync(1);

        // Assert
        Assert.False(session.Advertiser.IsActive);
        Assert.Equal(NearbyActivityStopReason.Faulted, session.Advertiser.LastStop?.Reason);
        Assert.NotNull(session.Advertiser.LastStop?.Error);
        Assert.True(session.Discoverer.IsActive, "Advertising and discovery are independent.");
    }

    [Fact]
    public async Task DiscoverFaultAfterSuccessfulStart_RecordsAFaultedStop_AndLeavesAdvertisingRunning()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        using var stops = Observe.Stops(session.Discoverer);

        // Act
        connections.FaultDiscoverStream(new NearbyException("radio dropped"));
        await stops.WaitForAsync(1);

        // Assert
        Assert.False(session.Discoverer.IsActive);
        Assert.Equal(NearbyActivityStopReason.Faulted, session.Discoverer.LastStop?.Reason);
        Assert.True(session.Advertiser.IsActive, "Advertising and discovery are independent.");
    }

    [Fact]
    public async Task StartingAgain_ClearsLastStop()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        await session.Discoverer.StopAsync();

        // Act
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(session.Discoverer.LastStop);
    }

    [Fact]
    public async Task DiscoveryRefresh_RaisesNothing_AndIsActiveStaysTrue()
    {
        // A refresh restarts the underlying scan. Discovery never logically stopped, so the
        // flag holds and nothing is raised; otherwise a bound indicator blinks every refresh
        // interval.

        // Arrange
        var time = new FakeTimeProvider();
        var interval = TimeSpan.FromSeconds(30);
        var connections = new FakeNearby();
        var session = Create.Session(connections, new NearbyOptions { DiscoveryRefreshInterval = interval }, time);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        using var changes = Observe.Activity(session.Discoverer);

        // Act
        time.Advance(interval);
        await Wait.UntilAsync(() => connections.DiscoverCallCount >= 2);

        // Assert
        Assert.True(session.Discoverer.IsActive, "Discovery does not stop across a refresh.");

        await session.Discoverer.StopAsync();
        await changes.WaitForAsync(1);
        Assert.Equal([false], changes.Seen);
    }
}
