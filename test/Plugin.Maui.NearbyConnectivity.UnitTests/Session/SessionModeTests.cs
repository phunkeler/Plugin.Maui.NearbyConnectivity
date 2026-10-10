namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Session")]
public sealed class SessionModeTests
{
    [Fact]
    public async Task AdvertiserStart_StartsOnlyTheAdvertisePump()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        // Act
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, connections.AdvertiseCallCount);
        Assert.Equal(0, connections.DiscoverCallCount);
    }

    [Fact]
    public async Task DiscovererStart_StartsOnlyTheDiscoverPump()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        // Act
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, connections.AdvertiseCallCount);
        Assert.Equal(1, connections.DiscoverCallCount);
    }

    [Fact]
    public async Task StartingBoth_RunsBothPumps()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        // Act
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.Advertiser.IsActive);
        Assert.True(session.Discoverer.IsActive);
    }

    [Fact]
    public async Task AdvertiserStart_WhenAlreadyActive_DoesNotReachThePlatformAgain()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, connections.AdvertiseCallCount);
    }

    [Fact]
    public async Task StopAsync_ModeThatIsNotRunning_IsANoOp()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        // Act
        await session.Advertiser.StopAsync();
        await session.Discoverer.StopAsync();

        // Assert
        Assert.False(session.Advertiser.IsActive);
        Assert.False(session.Discoverer.IsActive);
    }

    [Fact]
    public async Task DiscovererStart_WhenItFails_LeavesDiscoveryStopped()
    {
        // Arrange
        var connections = new FakeNearby
        {
            DiscoverFault = new NearbyException("permission denied"),
        };
        var session = Create.Session(connections);

        // Act
        var thrown = await Assert.ThrowsAsync<NearbyException>(
            () => session.Discoverer.StartAsync(TestContext.Current.CancellationToken));

        // Assert
        // The platform's own exception surfaces, not a wrapper.
        Assert.Same(connections.DiscoverFault, thrown);
        Assert.False(session.Discoverer.IsActive);
    }

    [Fact]
    public async Task DiscovererStart_WhenItFails_LeavesAdvertisingRunning()
    {
        // Arrange
        var connections = new FakeNearby
        {
            DiscoverFault = new NearbyException("permission denied"),
        };
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await Assert.ThrowsAsync<NearbyException>(
            () => session.Discoverer.StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.True(session.Advertiser.IsActive, "Advertising was running before and is not this call's to stop.");
        Assert.False(session.Discoverer.IsActive);
    }
}
