using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers the session half of the platform suspension signal: what iOS backgrounding the app stops,
/// and how the reasons distinguish it from a stop the application asked for.
/// </summary>
/// <remarks>
/// The <c>UIApplication</c> notification that raises this is device-only. What is testable on
/// <c>net10.0</c> is that the signal reaches the session at all and produces the suspension reasons
/// rather than the requested ones — the distinction an application re-arming on foreground reads.
/// </remarks>
[Trait("Category", "Session")]
public sealed class SuspendTests
{
    [Fact]
    public async Task ASuspensionSignal_StopsAdvertisingAndDiscovery()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);

        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Act
        platform.EmitSuspended();

        // Assert
        await Wait.UntilAsync(() => !session.Advertiser.IsActive && !session.Discoverer.IsActive);

        Assert.False(session.Advertiser.IsActive);
        Assert.False(session.Discoverer.IsActive);
    }

    [Fact]
    public async Task ASuspensionSignal_ReportsSuspendedNotRequested()
    {
        // The whole reason the signal carries its own reasons: an app watching activity can tell a
        // backgrounding from its own Stop and re-arm on return.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);

        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        using var stops = Observe.Stops(session.Advertiser);

        // Act
        platform.EmitSuspended();

        // Assert
        await stops.WaitForAsync(1);
        Assert.Equal(NearbyActivityStopReason.Suspended, stops.Seen[^1]?.Reason);
    }

    [Fact]
    public async Task ASuspensionSignal_ClosesLiveConnectionsAsSuspended()
    {
        // A Closed awaiter has to be able to tell a suspension from a peer dropping.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);

        var device = Create.Device();
        var connection = Create.Connection(device);
        platform.ConnectResult = connection;

        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        device = await platform.EmitDeviceFoundAsync(device);
        await Wait.UntilAsync(() => session.Current(device.Id) is not null);

        await device.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        platform.EmitSuspended();

        // Assert
        var reason = await connection.Closed.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NearbyEndReason.AppSuspended, reason);
    }

    [Fact]
    public async Task ASuspensionSignalAfterDisposal_DoesNothing()
    {
        // Disposal unsubscribes before teardown, so a notification landing late cannot restart work
        // the session has already joined.

        // Arrange
        var platform = new FakeNearby();
        var session = Create.Session(platform);

        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await session.DisposeAsync();

        // Act
        // The platform raises after disposal. Nothing is subscribed, so nothing runs.
        platform.EmitSuspended();

        // Assert
        Assert.False(platform.HasSignalSubscribers);
        Assert.False(session.Advertiser.IsActive);
    }

    [Fact]
    public async Task ASuspension_CompletesWithoutWaitingOutTheJoinBound()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var platform = new FakeNearby();
        await using var session = Create.Session(platform, timeProvider: time);

        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        // Act
        platform.EmitSuspended();

        // Assert
        // The clock never moves, so only a join that does not wait on itself can finish.
        await Wait.UntilAsync(() => !session.Advertiser.IsActive);
        Assert.False(session.Advertiser.IsActive);
    }
}
