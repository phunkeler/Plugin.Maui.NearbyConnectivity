namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Advertising and discovery toggle independently.
/// </summary>
[Trait("Category", "Session")]
public sealed class SessionToggleTests
{
    [Fact]
    public async Task StartAsync_Advertise_SetsIsAdvertising_WithoutSettingIsDiscovering()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        // Act
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.Advertiser.IsActive);
        Assert.False(session.Discoverer.IsActive, "Advertising must not imply discovering — both sample pages toggle them separately.");
    }

    [Fact]
    public async Task StartAsync_Discover_SetsIsDiscovering_WithoutSettingIsAdvertising()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        // Act
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.Discoverer.IsActive);
        Assert.False(session.Advertiser.IsActive);
    }

    [Fact]
    public async Task StopAsync_Advertise_ClearsIsAdvertising_AndLeavesDiscoveryRunning()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await session.Advertiser.StopAsync();

        // Assert
        Assert.False(session.Advertiser.IsActive);
        Assert.True(session.Discoverer.IsActive, "Stopping one must not stop the other.");
    }

    [Fact]
    public async Task StopAsync_ClearsBothToggles()
    {
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
    public async Task StartAsync_Advertise_CalledTwice_IsNoOp()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        // Act
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.Advertiser.IsActive);
        // A second start must not reach the platform again.
        Assert.Equal(1, connections.AdvertiseCallCount);
    }

    [Fact]
    public async Task StopAllAsync_RunsToCompletion_WhileTheCallersOwnTokenIsCanceled()
    {
        // The reason the stops take no token. A page exiting cancels the token driving its own
        // stream pumps and then tears the session down. Teardown cannot observe the caller's
        // token, so a fully cancelled caller still gets advertising stopped, discovery stopped,
        // and the connection closed rather than a half-torn-down session.

        // Arrange
        var platform = new FakeNearby();
        var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        await session.Advertiser.StartAsync(token);

        var peer = Create.Device("peer");
        platform.ConnectResult = Create.Connection(peer);

        var device = await Create.DiscoveredAsync(session, platform, peer);
        var connection = await device.ConnectAsync(token);

        using var pumps = new CancellationTokenSource();
        await pumps.CancelAsync();

        // Act
        // The shape a page-exit handler has, with its own token already cancelled.
        await session.StopAllAsync();

        // Assert
        Assert.False(session.Advertiser.IsActive);
        Assert.False(session.Discoverer.IsActive);
        Assert.Equal(NearbyEndReason.SessionStopped, await connection.Closed);
    }
}
