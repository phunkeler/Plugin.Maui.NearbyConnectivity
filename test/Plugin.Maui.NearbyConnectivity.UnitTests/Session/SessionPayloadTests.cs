using System.Threading.Channels;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Session")]
public sealed class SessionPayloadTests
{
    [Fact]
    public async Task PayloadsFromMultipleConnections_AllArrive()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        var alice = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        var bob = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-2", NearbyPresence.FromRemote("Bob", default)));

        var aliceChannel = Channel.CreateUnbounded<NearbyPayload>();
        var bobChannel = Channel.CreateUnbounded<NearbyPayload>();

        connections.ConnectResult = Create.Connection(alice, aliceChannel);
        var aliceConnection = await alice.ConnectAsync(TestContext.Current.CancellationToken);
        connections.ConnectResult = Create.Connection(bob, bobChannel);
        var bobConnection = await bob.ConnectAsync(TestContext.Current.CancellationToken);

        aliceChannel.Writer.TryWrite(new NearbyBytesPayload(new byte[] { 1 }));
        bobChannel.Writer.TryWrite(new NearbyBytesPayload(new byte[] { 2 }));
        aliceChannel.Writer.TryComplete();
        bobChannel.Writer.TryComplete();

        var aliceCount = 0;
        var bobCount = 0;

        // Act
        await foreach (var _ in aliceConnection.Inbound.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            aliceCount++;
        }

        await foreach (var _ in bobConnection.Inbound.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            bobCount++;
        }

        // Assert
        Assert.Equal(1, aliceCount);
        Assert.Equal(1, bobCount);
    }
}
