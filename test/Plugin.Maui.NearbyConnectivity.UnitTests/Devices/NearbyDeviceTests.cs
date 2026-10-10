namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Devices")]
public class NearbyDeviceTests
{
    public sealed class EqualsMethod : NearbyDeviceTests
    {
        [Fact]
        public void SameId_ReturnsTrue()
        {
            // Arrange
            var left = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));
            var right = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));

            // Act
            var areEqual = left.Equals(right);

            // Assert
            Assert.True(areEqual);
        }

        [Fact]
        public void SameId_DifferentDisplayName_ReturnsTrue()
        {
            // Arrange
            var left = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));
            var right = new NearbyDevice("ep1", NearbyPresence.FromRemote("Bob", default));

            // Act
            var areEqual = left.Equals(right);

            // Assert
            Assert.True(areEqual);
        }

        [Fact]
        public void DifferentId_ReturnsFalse()
        {
            // Arrange
            var left = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));
            var right = new NearbyDevice("ep2", NearbyPresence.FromRemote("Alice", default));

            // Act
            var areEqual = left.Equals(right);

            // Assert
            Assert.False(areEqual);
        }

        [Fact]
        public void SameReference_ReturnsTrue()
        {
            // Arrange
            var left = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));

            // Act
            var areEqual = left.Equals(left);

            // Assert
            Assert.True(areEqual);
        }

        [Fact]
        public void Null_ReturnsFalse()
        {
            // Arrange
            var left = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));

            // Act
            var areEqual = left.Equals(null);

            // Assert
            Assert.False(areEqual);
        }

        [Fact]
        public void NonDeviceObject_ReturnsFalse()
        {
            // Arrange
            var left = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));

            // Act
            var areEqual = left.Equals((object)"ep1");

            // Assert
            Assert.False(areEqual);
        }
    }

    public sealed class EqualityOperator : NearbyDeviceTests
    {
        [Fact]
        public void SameId_ReturnsTrue()
        {
            // Arrange
            var left = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));
            var right = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));

            // Act
            var areEqual = left == right;

            // Assert
            Assert.True(areEqual);
        }

        [Fact]
        public void DifferentId_ReturnsFalse()
        {
            // Arrange
            var left = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));
            var right = new NearbyDevice("ep2", NearbyPresence.FromRemote("Alice", default));

            // Act
            var areEqual = left == right;

            // Assert
            Assert.False(areEqual);
        }

    }

    public sealed class HashCode : NearbyDeviceTests
    {
        [Fact]
        public void SameId_ReturnsSameHashCode()
        {
            // Arrange
            var left = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));
            var right = new NearbyDevice("ep1", NearbyPresence.FromRemote("Bob", default));

            // Act
            var leftHash = left.GetHashCode();
            var rightHash = right.GetHashCode();

            // Assert
            Assert.Equal(leftHash, rightHash);
        }

        [Fact]
        public void DifferentId_ReturnsDifferentHashCode()
        {
            // Arrange
            var left = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));
            var right = new NearbyDevice("ep2", NearbyPresence.FromRemote("Alice", default));

            // Act
            var leftHash = left.GetHashCode();
            var rightHash = right.GetHashCode();

            // Assert
            Assert.NotEqual(leftHash, rightHash);
        }
    }

    public sealed class Identity : NearbyDeviceTests
    {
        // The load-bearing guarantee. Id-only equality replaces the record's generated member-wise
        // equality, so a device that merely changed status stays the same device: registries and
        // the platform's connection table key on id, and an identity that shifted mid-lifecycle would strand
        // every existing entry. Generated equality would break every assertion below.
        [Fact]
        public void HashCodeAndEquality_AreStable_AcrossStateTransitions()
        {
            // Arrange
            var device = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));
            var sameId = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));
            var originalHash = device.GetHashCode();

            var dictionary = new Dictionary<NearbyDevice, string> { [device] = "tracked" };

            // Act
            // Walk the full lifecycle, which for a snapshot means producing new instances.
            // The display name is not part of the walk: only the session renames a device, and
            // identity is Id alone either way.
            var connected = device
                .With(NearbyDeviceStatus.RequestReceived)
                .With(NearbyDeviceStatus.Connecting)
                .With(NearbyDeviceStatus.Connected);

            // Assert
            // Identity never moved, so the entry is still reachable
            Assert.Equal(originalHash, connected.GetHashCode());
            Assert.True(connected.Equals(sameId));
            Assert.True(connected == sameId);
            Assert.True(dictionary.TryGetValue(connected, out var tracked));
            Assert.Equal("tracked", tracked);
            Assert.True(dictionary.ContainsKey(sameId));
        }

        [Fact]
        public void Constructor_NullId_Throws()
            => Assert.Throws<ArgumentNullException>(
                () => new NearbyDevice(null!, NearbyPresence.FromRemote("Alice", default)));

        [Fact]
        public void NewDevice_StartsVisible()
        {
            // Arrange
            var device = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));

            // Assert
            Assert.Equal(NearbyDeviceStatus.Visible, device.Status);
        }
    }

    public sealed class Snapshots : NearbyDeviceTests
    {
        // A device is a value handed out by the session; nothing may write to it afterwards. If any
        // of these properties ever regained a public setter, a consumer could mutate a snapshot the
        // session still holds, and the thread-safety the value type exists to provide would be gone.
        [Fact]
        public void MutableProperties_AreInitOnly()
        {
            // Arrange
            var properties = new[]
            {
                nameof(NearbyDevice.Id),
                nameof(NearbyDevice.Presence),
                nameof(NearbyDevice.Status),
            };

            // Act
            var settable = properties
                .Select(name => typeof(NearbyDevice).GetProperty(name) ?? throw new InvalidOperationException(name))
                .Where(p => p.SetMethod is { ReturnParameter: var ret }
                    && !ret.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"))
                .Select(p => p.Name)
                .ToArray();

            // Assert
            // NearbyDevice is a snapshot: every property must be init-only.
            Assert.Empty(settable);
        }

        [Fact]
        public void ToString_ReportsNameIdAndStatus()
        {
            // Arrange
            var device = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default)) { Status = NearbyDeviceStatus.Connected };

            // Act
            var text = device.ToString();

            // Assert
            Assert.Equal("Alice [ep1] Connected", text);
        }

        [Fact]
        public void ToString_UnnamedDevice_PrintsTheIdAlone()
        {
            // Arrange
            // No placeholder is invented for a peer that sent no name.
            var device = new NearbyDevice("ep1", NearbyPresence.FromRemote(null, default));

            // Act
            var text = device.ToString();

            // Assert
            Assert.Equal("[ep1] Visible", text);
        }
    }

    public sealed class Verbs : NearbyDeviceTests
    {
        [Fact]
        public async Task ConnectAsync_NoSession_Throws()
        {
            // Arrange
            var device = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));

            // Act
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => device.ConnectAsync(TestContext.Current.CancellationToken));

            // Assert
            Assert.Contains("was not produced by a session", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DisconnectAsync_NoSession_Throws()
        {
            // Arrange
            var device = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));

            // Act
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => device.DisconnectAsync());

            // Assert
            Assert.Contains("was not produced by a session", ex.Message, StringComparison.Ordinal);
        }

        // Null rather than throwing: "is there a connection" has an honest answer for a device
        // with no session behind it.
        [Fact]
        public void Connection_NoSession_ReturnsNull()
        {
            // Arrange
            var device = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));

            // Act
            var connection = device.Connection;

            // Assert
            Assert.Null(connection);
        }

        [Fact]
        public async Task ConnectAsync_FromDeviceSet_ConnectsThatDevice()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
            var found = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));
            await platform.EmitDeviceFoundAsync(found);
            platform.ConnectResult = Create.Connection(found);
            var row = session.Devices[0];

            // Act
            var connection = await row.ConnectAsync(TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("ep1", connection.RemoteDevice.Id);
            Assert.Equal(1, platform.ConnectCallCount);
        }

        // The point of routing through the session by id: a row bound before the status changed
        // still resolves to what the session knows now.
        [Fact]
        public async Task Connection_AfterConnecting_ResolvesLiveFromAStaleRow()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
            var found = new NearbyDevice("ep1", NearbyPresence.FromRemote("Alice", default));
            await platform.EmitDeviceFoundAsync(found);
            platform.ConnectResult = Create.Connection(found);
            var staleRow = session.Devices[0];

            // Act
            await staleRow.ConnectAsync(TestContext.Current.CancellationToken);

            // Assert
            Assert.NotNull(staleRow.Connection);
            Assert.Equal(NearbyDeviceStatus.Visible, staleRow.Status);
        }
    }

}
