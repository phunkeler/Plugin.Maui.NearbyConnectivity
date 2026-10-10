namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Public-surface promises owned by this folder's types.
/// </summary>
[Trait("Category", "Session")]
public sealed class ConcurrentConnectTests
{
    // -------------------------------------------------------------------------
    // a second connect joins the handshake instead of orphaning it
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TwoConcurrentConnects_ShareOneHandshake()
    {
        // "Connecting twice is not an error." If the second caller overwrote the first caller's
        // completion source, the first would wait out OfferWindow and report TimedOut for a
        // connection that had already opened.

        // Arrange
        var platform = new FakeNearby { BlockConnect = true };
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        var device = await Create.DiscoveredAsync(session, platform, Create.Device("peer-1"));
        platform.ConnectResult = Create.Connection(device);

        var first = device.ConnectAsync(token);
        var second = device.ConnectAsync(token);

        // Act
        platform.ReleaseConnect();

        // Assert
        // Both callers get the same connection, and neither faults.
        Assert.Same(await first, await second);
    }

    // -------------------------------------------------------------------------
    // a read-now list of open connections
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Connections_ReportsWhatIsOpenRightNow()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        Assert.Empty(session.Connections);

        var device = await Create.DiscoveredAsync(session, platform, Create.Device("peer-1"));
        platform.ConnectResult = Create.Connection(device);

        // Act
        var connection = await device.ConnectAsync(token);

        // Assert
        Assert.Same(connection, Assert.Single(session.Connections));
    }

    // -------------------------------------------------------------------------
    // an unknown id is a typed, asynchronous failure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConnectingToAnUnknownId_ReportsDeviceUnknown()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);

        // Act
        // A device the session never knew, minted with a gateway the way the platform mints
        // one, so the call reaches the session and fails on the lookup rather than on the gateway.
        var unknown = platform.Mint(new NearbyDevice("nobody", NearbyPresence.FromRemote("Nobody", default)));
        var connect = unknown.ConnectAsync(TestContext.Current.CancellationToken);

        // Assert
        // The throw lands on the task, not at the call, and carries a reason like every
        // other connect outcome.
        var ex = await Assert.ThrowsAsync<NearbyConnectException>(() => connect);

        Assert.Equal(NearbyConnectFailure.DeviceUnknown, ex.Reason);
    }
}
