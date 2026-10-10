using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers a connection request that arrives from a device the session is already connected to.
/// </summary>
/// <remarks>
/// A connected peer chooses when to send a request. Holding it moved the device out of
/// <see cref="NearbyDeviceStatus.Connected"/>, and declining it or letting it expire then removed
/// the device from <see cref="INearbyConnectivity.Devices"/> while its connection stayed open.
/// </remarks>
[Trait("Category", "Session")]
public sealed class RequestFromConnectedDeviceTests
{
    [Fact]
    public async Task IsDeclinedWithoutSurfacing()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        await session.Advertiser.StartAsync(token);

        var device = Create.Device("asker");
        await platform.EmitRequestAsync(device, () => Create.Connection(device));

        var request = await Take.RequestAsync(session, token);
        var connection = await request.AcceptAsync(token);
        await Wait.UntilAsync(() => session.StatusOf(device.Id) == NearbyDeviceStatus.Connected);

        var declined = false;

        // Act
        await platform.EmitRequestAsync(
            device,
            () => Create.Connection(device),
            onReject: () => declined = true);

        await Wait.UntilAsync(() => declined);

        // Assert
        Assert.True(declined);
        Assert.Equal(NearbyDeviceStatus.Connected, session.StatusOf(device.Id));
        Assert.Null(session.Current(device.Id)?.Request);
        Assert.True(connection.IsOpen);
        Assert.Same(connection, session.ConnectionOf(device.Id));
    }

    [Fact]
    public async Task AHeldRequest_IsSettledWhenTheOutboundHandshakeConnects()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var platform = new FakeNearby { BlockConnect = true };
        await using var session = Create.Session(platform, timeProvider: time);
        var token = TestContext.Current.CancellationToken;

        var device = await Create.DiscoveredAsync(session, platform, Create.Device("both-tap"));
        platform.ConnectResult = Create.Connection(device);
        var connect = device.ConnectAsync(token);
        await Wait.UntilAsync(() => session.StatusOf(device.Id) == NearbyDeviceStatus.Connecting);

        await session.Advertiser.StartAsync(token);
        await platform.EmitRequestAsync(
            device,
            () => Create.Connection(device),
            deadline: time.GetUtcNow() + TimeSpan.FromSeconds(30));
        var request = await Take.RequestAsync(session, token);

        // Act
        platform.ReleaseConnect();
        var connection = await connect;
        time.Advance(TimeSpan.FromSeconds(31));

        // Assert
        await Wait.UntilAsync(() => request.Expired.IsCompleted);
        Assert.Equal(NearbyDeviceStatus.Connected, session.StatusOf(device.Id));
        Assert.True(connection.IsOpen);
    }
}
