namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// Discovery callbacks on iOS (<c>FoundPeer</c>/<c>LostPeer</c>): found and lost events arrive on
/// the discover channel, and the peer registry tracks and releases the device.
/// </summary>
public sealed class DeviceDiscoveryTests : DeviceTest
{
    [Fact]
    public async Task FoundPeer_PublishesFoundEventAndTracksPeer()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        var id = platform.PeerLookup.DeviceIdFor(peerID);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        platform.Ios().FoundPeer(browser: null!, peerID: peerID, info: null);

        // Assert
        var found = await platform._discoverChannel.Reader.ReadAsync(cts.Token);
        Assert.True(found.Found);
        Assert.True(platform.PeerLookup.TryGetDevice(id, out var tracked));
        Assert.Equal("Alice", tracked.Presence.DisplayName);
    }

    [Fact]
    public async Task LostPeer_PublishesLostEventForTheSameDeviceAndReleasesPeer()
    {
        // Arrange
        // A peer already discovered, and its Found event drained off the channel.
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        var id = platform.PeerLookup.DeviceIdFor(peerID);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        platform.Ios().FoundPeer(browser: null!, peerID: peerID, info: null);
        var found = await platform._discoverChannel.Reader.ReadAsync(cts.Token);

        // Act
        platform.Ios().LostPeer(browser: null!, peerID: peerID);

        // Assert
        var lost = await platform._discoverChannel.Reader.ReadAsync(cts.Token);
        Assert.False(lost.Found);
        Assert.Equal(found.Device.Id, lost.Device.Id);
        Assert.False(platform.PeerLookup.TryGetDevice(id, out _));
    }

    [Fact]
    public async Task LostPeer_WhileConnected_DoesNotEmitLostEvent()
    {
        // Arrange
        // A connected peer that stops advertising is NOT lost; only its advertisement is.
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (_, id) = await Create.ConnectedAsync(platform, peerID, cts.Token);

        // Act
        platform.Ios().LostPeer(browser: null!, peerID: peerID);

        // Assert
        Assert.True(platform.PeerLookup.TryGetDevice(id, out _));
        Assert.False(platform._discoverChannel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task FoundPeer_WithBase64DiscoveryInfo_CarriesTheInfo()
    {
        // Arrange
        // The name rides MCPeerID; only the info needs the Bonjour TXT record.
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        var id = platform.PeerLookup.DeviceIdFor(peerID);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var info = "route:R17"u8.ToArray();

        using var discoveryInfo = NSDictionary.FromObjectAndKey(
            (NSString)Convert.ToBase64String(info), (NSString)"info");

        // Act
        platform.Ios().FoundPeer(browser: null!, peerID: peerID, info: discoveryInfo);

        // Assert
        await platform._discoverChannel.Reader.ReadAsync(cts.Token);
        Assert.True(platform.PeerLookup.TryGetDevice(id, out var tracked));
        Assert.Equal("Alice", tracked.Presence.DisplayName);
        Assert.Equal(info, tracked.Presence.Info.ToArray());
    }

    [Theory]
    [InlineData("not base64 at all !!", TestDisplayName = "a value that is not base64")]
    [InlineData("", TestDisplayName = "an empty value")]
    public async Task FoundPeer_WithUnusableDiscoveryInfo_KeepsTheNameAndDropsTheInfo(string value)
    {
        // Remote input: an unusable value must not throw inside the platform callback.

        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        var id = platform.PeerLookup.DeviceIdFor(peerID);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var discoveryInfo = NSDictionary.FromObjectAndKey((NSString)value, (NSString)"info");

        // Act
        platform.Ios().FoundPeer(browser: null!, peerID: peerID, info: discoveryInfo);

        // Assert
        await platform._discoverChannel.Reader.ReadAsync(cts.Token);
        Assert.True(platform.PeerLookup.TryGetDevice(id, out var tracked));
        Assert.Equal("Alice", tracked.Presence.DisplayName);
        Assert.True(tracked.Presence.Info.IsEmpty);
    }
}
