namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Session")]
public sealed class StartFailureTests
{
    [Fact]
    public async Task AdvertiseStartFailure_ThrowsAndClearsIsAdvertising()
    {
        // Arrange
        var connections = new FakeNearby
        {
            AdvertiseFault = new NearbyException("radio off"),
        };
        var session = Create.Session(connections);

        // Act & Assert
        await Assert.ThrowsAsync<NearbyException>(
            () => session.Advertiser.StartAsync(TestContext.Current.CancellationToken));

        Assert.False(session.Advertiser.IsActive, "A failed start must not leave the session claiming to advertise.");
    }

    [Fact]
    public async Task DiscoverStartFailure_ThrowsAndClearsIsDiscovering()
    {
        // Arrange
        var connections = new FakeNearby
        {
            DiscoverFault = new NearbyException("permission denied"),
        };
        var session = Create.Session(connections);

        // Act & Assert
        await Assert.ThrowsAsync<NearbyException>(
            () => session.Discoverer.StartAsync(TestContext.Current.CancellationToken));

        Assert.False(session.Discoverer.IsActive);
    }

    [Fact]
    public async Task AdvertiseStartFailure_IsRetryable()
    {
        // A failed start must clean up the dead pump so a retry reaches the platform again,
        // rather than reusing a Task/CancellationTokenSource pair that already faulted.

        // Arrange
        var connections = new FakeNearby
        {
            AdvertiseFault = new NearbyException("radio off"),
        };
        var session = Create.Session(connections);

        // Act
        await Assert.ThrowsAsync<NearbyException>(
            () => session.Advertiser.StartAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<NearbyException>(
            () => session.Advertiser.StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(2, connections.AdvertiseCallCount);
    }

    [Fact]
    public async Task AdvertiseFaultAfterSuccessfulStart_DoesNotThrowFromStartAsync()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        // Act
        connections.FaultAdvertiseStream(new NearbyException("radio dropped"));
        await Wait.UntilAsync(() => !session.Advertiser.IsActive);

        // Assert
        Assert.False(session.Advertiser.IsActive);
    }

    [Fact]
    public async Task DiscoverFaultAfterSuccessfulStart_DoesNotThrowFromStartAsync()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Act
        connections.FaultDiscoverStream(new NearbyException("radio dropped"));
        await Wait.UntilAsync(() => !session.Discoverer.IsActive);

        // Assert
        Assert.False(session.Discoverer.IsActive);
    }

    [Fact]
    public async Task AdvertiseFaultAfterSuccessfulStart_PumpTakesLoggedPathAndSessionRestarts()
    {
        // The dead pump's own Cts/Task must not wedge a later restart: a fault that loses the
        // started race (started.TrySetException returns false, so the pump falls through to the
        // logged path instead of rethrowing) still leaves the pump cleanly stoppable, so a fresh
        // StartAsync reaches the platform again rather than hanging on stale state.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        connections.FaultAdvertiseStream(new NearbyException("radio dropped"));
        await Wait.UntilAsync(() => !session.Advertiser.IsActive);

        // Act
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.Advertiser.IsActive);
        Assert.Equal(2, connections.AdvertiseCallCount);
    }

    [Fact]
    public async Task DiscoverFaultAfterSuccessfulStart_PumpTakesLoggedPathAndSessionRestarts()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        connections.FaultDiscoverStream(new NearbyException("radio dropped"));
        await Wait.UntilAsync(() => !session.Discoverer.IsActive);

        // Act
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.Discoverer.IsActive);
        Assert.Equal(2, connections.DiscoverCallCount);
    }
}
