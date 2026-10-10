namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// The iOS peer-identity stack against real <c>MCPeerID</c> instances: key derivation is stable
/// per peer and distinct across peers (even same-named ones), the local identity is memoized for
/// the process, and the registry round-trips native handles.
/// </summary>
public sealed class PeerIdentityTests : DeviceTest
{
    [Fact]
    public void PeerKey_StablePerPeerAndDistinctAcrossPeers()
    {
        // Arrange
        var lookup = Create.PeerLookup();
        using var alice1 = new MCPeerID("Alice");
        using var alice2 = new MCPeerID("Alice");

        // Act
        var key1a = lookup.DeviceIdFor(alice1);
        var key1b = lookup.DeviceIdFor(alice1);
        var key2 = lookup.DeviceIdFor(alice2);

        // Assert
        Assert.Equal(key1a, key1b);
        Assert.NotEqual(key1a, key2);
        Assert.Equal(16, key1a.Length); // hex of 8 minted random bytes
    }

    [Fact]
    public void PeerKey_NullPeer_Throws()
    {
        // Arrange
        var lookup = Create.PeerLookup();

        // Act & Assert
        // A null peer is a caller bug, not a peer to mint an id for. Minting one
        // would key a device on nothing and hand the caller an id no callback can ever match.
        Assert.Throws<ArgumentNullException>(() => lookup.DeviceIdFor(null!));
    }

    [Fact]
    public async Task LocalPeerIdentity_MemoizedForPlatformLifetime()
    {
        // Arrange
        await using var platform = Create.NearbyTransport(Create.DefaultOptions("Alice"));

        // Act
        var first = platform.Ios().GetLocalPeerId();
        var second = platform.Ios().GetLocalPeerId();

        // Assert
        Assert.Same(first, second);
        Assert.Equal("Alice", first.DisplayName);
    }

    [Fact]
    public async Task Registry_TracksAndReleasesNativeHandle()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");

        // Act
        var device = platform.PeerLookup.Record(peerID);
        var hadHandle = platform.PeerLookup.TryGetPeerId(device.Id, out var handle);
        platform.PeerLookup.Remove(device.Id);
        var hasHandleAfterRemove = platform.PeerLookup.TryGetPeerId(device.Id, out _);

        // Assert
        Assert.True(hadHandle);
        Assert.Same(peerID, handle);
        Assert.False(hasHandleAfterRemove);
    }
}
