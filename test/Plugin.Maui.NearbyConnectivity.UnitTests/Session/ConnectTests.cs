namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Session")]
public sealed class ConnectTests
{
    [Fact]
    public async Task ConnectAsync_SetsConnectedStateAndReportsIt()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        var connection = Create.Connection(device);
        connections.ConnectResult = connection;

        await using var recorder = new ChangeRecorder(session);

        // Act
        var result = await device.ConnectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(connection, result);
        Assert.Equal(NearbyDeviceStatus.Connected, session.StatusOf("peer-1"));
        Assert.NotNull(session.ConnectionOf(device.Id));

        await Wait.UntilAsync(
            () => recorder.StatusesFor("peer-1").Contains(NearbyDeviceStatus.Connected));

        // Reaching Connected must be reported as a change, not only reflected in Devices.
        Assert.Contains(
            NearbyDeviceStatus.Connected,
            recorder.StatusesFor("peer-1"));
    }

    // Stopping discovery is how an app saves radio once it knows which device to connect to, so
    // the outbound handshake survives that. It does not survive StopAllAsync: a connect landing
    // after the registry is cleared would put a Connected device back into an empty session.
    [Fact]
    public async Task ConnectAsync_WhenDiscoveryStops_TheHandshakeContinues()
    {
        // Arrange
        var connections = new FakeNearby { BlockConnect = true };
        var session = Create.Session(connections);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        connections.ConnectResult = Create.Connection(device);
        var connect = device.ConnectAsync(TestContext.Current.CancellationToken);
        await Wait.UntilAsync(() => session.StatusOf("peer-1") == NearbyDeviceStatus.Connecting);

        // Act
        await session.Discoverer.StopAsync();
        connections.ReleaseConnect();

        // Assert
        await connect;
        Assert.Equal(NearbyDeviceStatus.Connected, session.StatusOf("peer-1"));
    }

    [Fact]
    public async Task ConnectAsync_WhenTheSessionStops_FailsAndLeavesNoDevice()
    {
        // Arrange
        var connections = new FakeNearby { BlockConnect = true };
        var session = Create.Session(connections);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        connections.ConnectResult = Create.Connection(device);
        var connect = device.ConnectAsync(TestContext.Current.CancellationToken);
        await Wait.UntilAsync(() => session.StatusOf("peer-1") == NearbyDeviceStatus.Connecting);

        // Act
        await session.StopAllAsync();

        // Assert
        // The caller's own token was never cancelled, so this is a connect failure, not
        // an OperationCanceledException the caller did not ask for.
        var ex = await Assert.ThrowsAsync<NearbyConnectException>(() => connect);
        Assert.Equal(NearbyConnectFailure.Failed, ex.Reason);
        connections.ReleaseConnect();
        Assert.Null(session.StatusOf("peer-1"));
    }

    [Fact]
    public async Task ConnectAsync_WhenAlreadyConnected_ReturnsTheExistingConnection()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        var connection = Create.Connection(device);
        connections.ConnectResult = connection;
        var first = await device.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        var second = await device.ConnectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(first, second);

        // The second call must not have run a handshake: one connect reached the platform.
        Assert.Equal(1, connections.ConnectCallCount);
    }

    // Convergence, not refusal: every state that already has a connection or is on its way to
    // one resolves to that connection. There is no busy exception to catch, which is what makes
    // connecting twice safe to write without guarding on Status first.
    [Fact]
    public async Task ConnectAsync_WhenTheDeviceHasAnUnansweredRequest_AcceptsThatRequest()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        var expected = Create.Connection(device);

        await connections.EmitRequestAsync(device, () => expected);
        await recorder.WaitForAsync("peer-1", 2);

        // Act
        var connection = await device.ConnectAsync(TestContext.Current.CancellationToken);

        // Assert
        // They asked first, so answering their request is what the caller wanted — racing a
        // second offer against it would have failed both.
        Assert.Same(expected, connection);
        Assert.Equal(0, connections.ConnectCallCount);
    }

    [Fact]
    public async Task ConnectAsync_WhenRejected_ResetsDeviceToVisible()
    {
        // Arrange
        var connections = new FakeNearby
        {
            ConnectFault = new InvalidOperationException("rejected"),
        };
        var session = Create.Session(connections);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => device.ConnectAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(NearbyDeviceStatus.Visible, device.Status);
    }

    [Fact]
    public async Task ConnectAsync_UnknownDevice_ThrowsDeviceUnknown()
    {
        // A device the session never published cannot be connected to: there is one route in,
        // and it resolves through the registry.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        var stranger = connections.Mint(new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));

        // Act
        var connecting = stranger.ConnectAsync(TestContext.Current.CancellationToken);

        // Assert
        var ex = await Assert.ThrowsAsync<NearbyConnectException>(() => connecting);
        Assert.Equal(NearbyConnectFailure.DeviceUnknown, ex.Reason);
    }
}
