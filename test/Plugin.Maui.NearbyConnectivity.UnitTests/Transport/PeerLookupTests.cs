using Microsoft.Extensions.Logging.Abstractions;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Connections")]
public class PeerLookupTests
{
    readonly PeerLookup _sut;

    public PeerLookupTests()
    {
        _sut = new PeerLookup { Logger = NullLogger.Instance };
    }

    public sealed class Record : PeerLookupTests
    {
        [Fact]
        public void NewPeer_ReturnsDeviceWithKeyAndDisplayName()
        {
            // Arrange
            var key = "peer-1";
            var displayName = "Alice";

            // Act
            var device = _sut.Record(key, displayName);

            // Assert
            Assert.Equal(key, device.Id);
            Assert.Equal(displayName, device.Presence.DisplayName);
        }

        [Fact]
        public void ExistingPeer_ReturnsSameDeviceInstance()
        {
            // Arrange
            var key = "peer-1";
            var displayName = "Alice";
            var first = _sut.Record(key, displayName);

            // Act
            var second = _sut.Record(key, displayName);

            // Assert
            Assert.Same(first, second);
        }

        [Fact]
        public void ExistingPeer_DoesNotAdoptNewDisplayName()
        {
            // Arrange
            // A rediscovery re-records the same endpoint; the incumbent must survive.
            var key = "peer-1";
            var original = _sut.Record(key, "Alice");

            // Act
            var rediscovered = _sut.Record(key, "Alice (renamed)");

            // Assert
            Assert.Same(original, rediscovered);
            Assert.Equal("Alice", rediscovered.Presence.DisplayName);
        }
    }

    public sealed class Presence : PeerLookupTests
    {
        [Fact]
        public void NameAndInfo_AreRecordedAsSent()
        {
            // Arrange
            // No filtering of any kind: a control character and a bidi override survive,
            // because rendering policy belongs to the application.
            var displayName = "Alice\r\n\u202EPhone";
            var info = "route:R17"u8.ToArray();

            // Act
            var device = _sut.Record("peer-1", displayName, info);

            // Assert
            Assert.Equal(displayName, device.Presence.DisplayName);
            Assert.Equal(info, device.Presence.Info.ToArray());
        }

        [Fact]
        public void NullName_BecomesEmpty_NotAPlaceholder()
        {
            // Arrange & Act
            // An unnamed peer reads as empty; inventing a label is the app's call.
            var device = _sut.Record("peer-1", null);

            // Assert
            Assert.Empty(device.Presence.DisplayName);
            Assert.True(device.Presence.Info.IsEmpty);
        }

        [Fact]
        public void NoInfo_IsEmpty()
            => Assert.True(_sut.Record("peer-1", "Alice").Presence.Info.IsEmpty);
    }

    public sealed class TryGetDevice : PeerLookupTests
    {
        [Fact]
        public void KnownKey_ReturnsTrueAndDevice()
        {
            // Arrange
            var key = "peer-1";
            _sut.Record(key, "Alice");

            // Act
            var found = _sut.TryGetDevice(key, out var device);

            // Assert
            Assert.True(found);
            Assert.NotNull(device);
            Assert.Equal(key, device.Id);
        }

        [Fact]
        public void UnknownKey_ReturnsFalseAndNullOut()
        {
            // Arrange
            var key = "peer-unknown";

            // Act
            var found = _sut.TryGetDevice(key, out var device);

            // Assert
            Assert.False(found);
            Assert.Null(device);
        }
    }

    public sealed class Remove : PeerLookupTests
    {
        [Fact]
        public void KnownKey_ReturnsRemovedDevice()
        {
            // Arrange
            var key = "peer-1";
            _sut.Record(key, "Alice");

            // Act
            var removed = _sut.Remove(key);

            // Assert
            Assert.NotNull(removed);
            Assert.Equal(key, removed.Id);
        }

        [Fact]
        public void KnownKey_IsNoLongerResolvable()
        {
            // Arrange
            var key = "peer-1";
            _sut.Record(key, "Alice");

            // Act
            _sut.Remove(key);

            // Assert
            Assert.False(_sut.TryGetDevice(key, out _));
        }

        [Fact]
        public void UnknownKey_ReturnsNull()
        {
            // Arrange
            var key = "peer-unknown";

            // Act
            var removed = _sut.Remove(key);

            // Assert
            Assert.Null(removed);
        }
    }

    public sealed class Clear : PeerLookupTests
    {
        [Fact]
        public void RemovesAllTrackedPeers()
        {
            // Arrange
            _sut.Record("peer-1", "Alice");
            _sut.Record("peer-2", "Bob");

            // Act
            _sut.Clear();

            // Assert
            Assert.False(_sut.TryGetDevice("peer-1", out _));
            Assert.False(_sut.TryGetDevice("peer-2", out _));
        }

        [Fact]
        public void OnEmptyRegistry_DoesNotThrow()
        {
            // Arrange
            // Registry is created empty in constructor

            // Act
            _sut.Clear();

            // Assert
            Assert.False(_sut.TryGetDevice("peer-1", out _));
        }
    }
}
