namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Pins the stream grammars. A grammar covers one stream's item sequence only; it never promises ordering across
/// streams.
/// </summary>
[Trait("Category", "Grammar")]
public sealed class StreamGrammarTests
{
    [Fact]
    public async Task DeviceChanged_CarriesEachRequest()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        var first = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        var second = new NearbyDevice("peer-2", NearbyPresence.FromRemote("Bob", default));
        await connections.EmitRequestAsync(first, () => Create.Connection(first));
        var held = await Take.RequestAsync(session, TestContext.Current.CancellationToken);
        await using var recorder = new ChangeRecorder(session);

        // Act
        await connections.EmitRequestAsync(second, () => Create.Connection(second));
        await Wait.UntilAsync(() => recorder.StatusesFor("peer-2").Contains(NearbyDeviceStatus.RequestReceived));

        // Assert
        var live = recorder.For("peer-2").First(c => c.Device.Status == NearbyDeviceStatus.RequestReceived).Device;
        Assert.Equal("peer-1", held.RemoteDevice.Id);
        Assert.NotNull(live.Request);
        Assert.NotSame(held, live.Request);
    }

    [Fact]
    public async Task DeviceChanged_ConnectedAlwaysCarriesTheConnection()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        connections.ConnectResult = Create.Connection(device);
        await using var recorder = new ChangeRecorder(session);

        // Act
        var connected = await device.ConnectAsync(TestContext.Current.CancellationToken);
        await Wait.UntilAsync(() => recorder.StatusesFor("peer-1").Contains(NearbyDeviceStatus.Connected));

        // Assert
        var change = recorder.For("peer-1").First(c => c.Device.Status == NearbyDeviceStatus.Connected);
        Assert.Same(connected, change.Device.Connection);
    }

    [Fact]
    public async Task Connections_YieldsTheSameInstanceConnectAsyncReturns()
    {
        // Connections := replayed* live*   — the same instance ConnectAsync / AcceptAsync return.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        connections.ConnectResult = Create.Connection(device);

        // Act
        // Connect first, enumerate second: the connection arrives by replay.
        var connected = await device.ConnectAsync(TestContext.Current.CancellationToken);
        var replayed = await Take.FirstAsync(session.ConnectionsAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(connected, replayed);
    }

    [Fact]
    public async Task ReceiveAsync_IsPayloadStarEnd_TheBufferedTailSurvivesTheDisconnect()
    {
        // ReceiveAsync := payload* end   — end = disconnect, after the buffered tail.

        // Arrange
        var connection = Create.Connection();
        connection.WritePayload(new NearbyBytesPayload(new byte[] { 1 }));
        connection.WritePayload(new NearbyBytesPayload(new byte[] { 2 }));
        var received = new List<NearbyPayload>();

        // Act
        // Disconnect with two payloads still buffered, then enumerate with no token.
        connection.CompleteReceive();

        await foreach (var payload in connection.Inbound.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            received.Add(payload);
        }

        // Assert
        // Both buffered payloads arrive, then the stream ends cleanly.
        Assert.Equal(2, received.Count);
    }
}
