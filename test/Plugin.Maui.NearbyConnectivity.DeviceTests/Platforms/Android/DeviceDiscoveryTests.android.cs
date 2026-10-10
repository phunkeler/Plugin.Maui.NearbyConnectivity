namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// Discovery callbacks on Android (<c>OnEndpointFound</c>/<c>OnEndpointLost</c>): found and lost
/// events arrive on the discover channel, and the peer registry tracks and releases the device.
/// </summary>
public sealed class DeviceDiscoveryTests : DeviceTest
{
    [Fact]
    public async Task EndpointFound_PublishesFoundEventAndTracksPeer()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        platform.Android().OnEndpointFound("endpoint-1", Create.DiscoveredEndpointInfo());

        // Assert
        var found = await platform._discoverChannel.Reader.ReadAsync(cts.Token);
        Assert.True(found.Found);
        Assert.True(platform.PeerLookup.TryGetDevice(found.Device.Id, out var tracked));
        Assert.Equal("Alice", tracked.Presence.DisplayName);
    }

    [Fact]
    public async Task EndpointLost_PublishesLostEventForTheSameDeviceAndReleasesPeer()
    {
        // Arrange
        // A device already discovered, and its Found event drained off the channel.
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        platform.Android().OnEndpointFound("endpoint-1", Create.DiscoveredEndpointInfo());
        var found = await platform._discoverChannel.Reader.ReadAsync(cts.Token);

        // Act
        platform.Android().OnEndpointLost("endpoint-1");

        // Assert
        var lost = await platform._discoverChannel.Reader.ReadAsync(cts.Token);
        Assert.False(lost.Found);
        Assert.Equal(found.Device.Id, lost.Device.Id);
        Assert.False(platform.PeerLookup.TryGetDevice(found.Device.Id, out _));
    }

    [Fact]
    public async Task EndpointLost_WhileConnected_DoesNotEmitLostEvent()
    {
        // Arrange
        // A connected peer that stops advertising is NOT lost; only its advertisement is.
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (_, _, deviceId) = await Create.ConnectedAsync(platform, "Alice", cts.Token);

        // Act
        platform.Android().OnEndpointLost("endpoint-1");

        // Assert
        Assert.True(platform.PeerLookup.TryGetDevice(deviceId, out _));
        Assert.False(platform._discoverChannel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task EndpointFound_WithAFrame_CarriesTheNameAndInfo()
    {
        // The real SDK type carries the advertisement frame in place of a plain name.

        // Arrange
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var info = "route:R17"u8.ToArray();

        // Act
        platform.Android().OnEndpointFound(
            "endpoint-1", Create.DiscoveredEndpointInfoWithFrame("Bob", info));

        // Assert
        var found = await platform._discoverChannel.Reader.ReadAsync(cts.Token);
        Assert.True(platform.PeerLookup.TryGetDevice(found.Device.Id, out var tracked));
        Assert.Equal("Bob", tracked.Presence.DisplayName);
        Assert.Equal(info, tracked.Presence.Info.ToArray());
    }

    [Fact]
    public async Task EndpointFound_WithAPlainName_TracksWithEmptyInfo()
    {
        // A peer on 1.0.0-preview.1 advertises its raw name, which must still discover.

        // Arrange
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        platform.Android().OnEndpointFound("endpoint-1", Create.DiscoveredEndpointInfo("Alice"));

        // Assert
        var found = await platform._discoverChannel.Reader.ReadAsync(cts.Token);
        Assert.True(platform.PeerLookup.TryGetDevice(found.Device.Id, out var tracked));
        Assert.Equal("Alice", tracked.Presence.DisplayName);
        Assert.True(tracked.Presence.Info.IsEmpty);
    }
}
