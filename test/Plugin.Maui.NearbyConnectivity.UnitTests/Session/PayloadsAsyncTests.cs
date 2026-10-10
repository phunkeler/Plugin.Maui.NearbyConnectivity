using System.Threading.Channels;
using Microsoft.Maui.Storage;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Behavioural tests for <see cref="INearbyConnectivity.PayloadsAsync(CancellationToken)"/> — the
/// merged inbound stream that reads every connection's payloads for the consumer.
/// </summary>
/// <remarks>
/// The properties under test: every connection is read
/// (not just the first <c>ProcessorCount</c> of them), a connection that opens after the loop
/// starts is picked up, payloads buffered before a peer dropped are still handed over, and the
/// sender is attributable without reaching back to the connection.
/// </remarks>
[Trait("Category", "Session")]
public sealed class PayloadsAsyncTests
{
    static Channel<NearbyPayload> PayloadChannel()
        => Channel.CreateUnbounded<NearbyPayload>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /// <summary>
    /// Opens a connection on the session, with a channel the test writes inbound payloads to.
    /// </summary>
    static async Task<Channel<NearbyPayload>> ConnectAsync(
        NearbySession session,
        FakeNearby platform,
        NearbyDevice device,
        CancellationToken cancellationToken)
    {
        var channel = PayloadChannel();
        platform.ConnectResult = Create.Connection(device, channel);
        var discovered = await Create.DiscoveredAsync(session, platform, device);
        await discovered.ConnectAsync(cancellationToken);
        return channel;
    }

    /// <summary>
    /// Reads <paramref name="count"/> payloads, then stops. Bounded by the test's own token so a
    /// broken merge fails as a timeout rather than hanging the suite.
    /// </summary>
    static async Task<List<NearbyInboundPayload>> ReadAsync(
        IAsyncEnumerable<NearbyInboundPayload> payloads,
        int count,
        CancellationToken cancellationToken)
    {
        var read = new List<NearbyInboundPayload>();

        await foreach (var payload in payloads.WithCancellation(cancellationToken))
        {
            read.Add(payload);

            if (read.Count == count)
            {
                break;
            }
        }

        return read;
    }

    [Fact]
    public async Task PayloadsFromEveryConnection_ReachOneLoop()
    {
        // The failure the hand-written Parallel.ForEachAsync loop had: a reader parks for the life
        // of its connection, so a degree-of-parallelism bound leaves later connections unread.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        var devices = Enumerable.Range(0, 16).Select(i => Create.Device($"peer-{i}")).ToList();
        var channels = new List<Channel<NearbyPayload>>();

        foreach (var device in devices)
        {
            channels.Add(await ConnectAsync(session, platform, device, token));
        }

        foreach (var (channel, index) in channels.Select((c, i) => (c, i)))
        {
            channel.Writer.TryWrite(new NearbyBytesPayload(new byte[] { (byte)index }));
        }

        // Act
        var read = await ReadAsync(session.PayloadsAsync(token), devices.Count, token);

        // Assert
        Assert.Equal(
            devices.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal),
            read.Select(p => p.RemoteDevice.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ConnectionOpenedAfterTheLoopStarts_IsPickedUp()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        var reading = ReadAsync(session.PayloadsAsync(token), count: 1, token);

        // Act
        // The connection opens only after the enumeration is already running.
        var channel = await ConnectAsync(session, platform, Create.Device("late"), token);
        channel.Writer.TryWrite(new NearbyBytesPayload(new byte[] { 7 }));

        // Assert
        var read = await reading;
        Assert.Equal("late", Assert.Single(read).RemoteDevice.Id);
    }

    [Fact]
    public async Task ConnectionOpenBeforeTheLoopStarts_IsReplayed()
    {
        // The other half of "starting late loses nothing": the connection stream replays what is
        // already open, so its buffered payloads arrive too.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        var channel = await ConnectAsync(session, platform, Create.Device("early"), token);
        channel.Writer.TryWrite(new NearbyBytesPayload(new byte[] { 1 }));

        // Act
        var read = await ReadAsync(session.PayloadsAsync(token), count: 1, token);

        // Assert
        Assert.Equal("early", Assert.Single(read).RemoteDevice.Id);
    }

    [Fact]
    public async Task PayloadsBufferedBeforeAPeerDropped_AreStillDelivered()
    {
        // The token trap the merged stream exists to solve: a payload that arrived just before the
        // connection closed must not be discarded by the closing.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        var channel = await ConnectAsync(session, platform, Create.Device("dropped"), token);

        channel.Writer.TryWrite(new NearbyBytesPayload(new byte[] { 1 }));
        channel.Writer.TryWrite(new NearbyBytesPayload(new byte[] { 2 }));

        // The peer drops: the reader completes with payloads still buffered.
        channel.Writer.TryComplete();

        // Act
        var read = await ReadAsync(session.PayloadsAsync(token), count: 2, token);

        // Assert
        Assert.Equal(2, read.Count);
        Assert.All(read, p => Assert.Equal("dropped", p.RemoteDevice.Id));
    }

    [Fact]
    public async Task EveryPayloadShape_CarriesItsSender()
    {
        // RemoteDevice is stamped once, in the connection's write path, so it holds for all three
        // shapes rather than only the one the stamping site happened to see.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        var channel = await ConnectAsync(session, platform, Create.Device("sender"), token);

        using var stream = new MemoryStream();
        channel.Writer.TryWrite(new NearbyBytesPayload(new byte[] { 1 }));
        channel.Writer.TryWrite(new NearbyFilePayload(new FileResult("/tmp/nearby-test-file")));
#pragma warning disable NEARBY001 // Tests the experimental stream API.
        channel.Writer.TryWrite(new NearbyStreamPayload("vitals", stream));
#pragma warning restore NEARBY001

        // Act
        var read = await ReadAsync(session.PayloadsAsync(token), count: 3, token);

        // Assert
        Assert.Collection(
            read,
            p => Assert.IsType<NearbyBytesPayload>(p.Payload),
            p => Assert.IsType<NearbyFilePayload>(p.Payload),
#pragma warning disable NEARBY001 // Tests the experimental stream API.
            p => Assert.IsType<NearbyStreamPayload>(p.Payload));
#pragma warning restore NEARBY001

        Assert.All(read, p => Assert.Equal("sender", p.RemoteDevice.Id));
    }

    [Fact]
    public async Task ReadingHere_TakesThePayloadFromTheConnectionsOwnReader()
    {
        // The documented contract: this is Inbound merged, not a second copy of it. A test pins it
        // so a future "make it broadcast" change has to argue with a failing test first.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        var device = Create.Device("shared");
        var channel = await ConnectAsync(session, platform, device, token);
        channel.Writer.TryWrite(new NearbyBytesPayload(new byte[] { 1 }));

        // Act
        _ = await ReadAsync(session.PayloadsAsync(token), count: 1, token);

        // Assert
        Assert.True(platform.TryGetConnection(device.Id, out var connection));
        Assert.False(connection.Inbound.TryRead(out _), "The merged stream took the payload.");
    }

    [Fact]
    public async Task EachItem_PairsTheSenderWithThePayload()
    {
        // The pairing is the point of the type: a merged loop that must reach back to a connection
        // to learn who sent something has not saved the consumer anything.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        var channel = await ConnectAsync(session, platform, Create.Device("alice", "Alice"), token);
        channel.Writer.TryWrite(new NearbyBytesPayload(new byte[] { 1 }));

        // Act
        var read = await ReadAsync(session.PayloadsAsync(token), count: 1, token);

        // Assert
        // Deconstructs, and neither half needs a null check.
        var (device, payload) = Assert.Single(read);
        Assert.Equal("Alice", device.Presence.DisplayName);
        Assert.IsType<NearbyBytesPayload>(payload);
    }
}
