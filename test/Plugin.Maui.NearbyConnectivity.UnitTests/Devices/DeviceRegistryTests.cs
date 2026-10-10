namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Tests for <see cref="DeviceRegistry"/>, the session's thread-safe device store.
/// </summary>
[Trait("Category", "Devices")]
public class DeviceRegistryTests
{
    public sealed class Membership : DeviceRegistryTests
    {
        [Fact]
        public void AddIfAbsent_NewDevice_AddsIt()
        {
            // Arrange
            var registry = new DeviceRegistry();
            var device = Create.Device("a");

            // Act
            var result = registry.AddIfAbsent(device);

            // Assert
            // Equal, not Same: the registry stamps the session onto the device as it admits it, so
            // what comes back is a `with` copy of the argument rather than the argument itself.
            Assert.Equal(device, result);
            Assert.Single(registry);
        }

        // The rediscovery case: a connected device seen again by discovery must not be reset to the
        // freshly-constructed Visible snapshot the platform hands over.
        [Fact]
        public void AddIfAbsent_ExistingDevice_KeepsTheIncumbent()
        {
            // Arrange
            var registry = new DeviceRegistry();
            var connected = Create.Device("a", status: NearbyDeviceStatus.Connected);
            registry.AddIfAbsent(connected);

            // Act
            var result = registry.AddIfAbsent(Create.Device("a"));

            // Assert
            // Status is what distinguishes incumbent from newcomer here — the two compare equal,
            // because NearbyDevice equality is Id alone.
            Assert.Equal(NearbyDeviceStatus.Connected, result.Status);
            Assert.Equal(NearbyDeviceStatus.Connected, registry[0].Status);
            Assert.Single(registry);
        }

        [Fact]
        public void Remove_AbsentDevice_ReturnsFalse()
        {
            // Arrange
            var registry = new DeviceRegistry();

            // Act
            var result = registry.Remove("nope");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void RemoveWhere_RemovesOnlyMatches()
        {
            // Arrange
            var registry = new DeviceRegistry();
            registry.AddIfAbsent(Create.Device("visible"));
            registry.AddIfAbsent(Create.Device("connected", status: NearbyDeviceStatus.Connected));

            // Act
            registry.RemoveWhere(d => d.Status is NearbyDeviceStatus.Visible);

            // Assert
            Assert.Single(registry);
            Assert.Equal("connected", registry[0].Id);
        }

        [Fact]
        public void Update_AbsentDevice_ReturnsNull()
        {
            // Arrange
            var registry = new DeviceRegistry();

            // Act
            var result = registry.Update("nope", d => d.With(NearbyDeviceStatus.Connected));

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Update_ReplacesTheStoredSnapshot()
        {
            // Arrange
            var registry = new DeviceRegistry();
            registry.AddIfAbsent(Create.Device("a"));

            // Act
            var result = registry.Update("a", d => d.With(NearbyDeviceStatus.Connected));

            // Assert
            Assert.NotNull(result);
            Assert.Equal(NearbyDeviceStatus.Connected, result.Status);
            Assert.Equal(NearbyDeviceStatus.Connected, registry[0].Status);
        }

        // Enumeration must never throw for concurrent modification — the whole reason reads go
        // through an immutable snapshot rather than the dictionary.
        [Fact]
        public void Enumerating_WhileMutating_DoesNotThrow()
        {
            // Arrange
            var registry = new DeviceRegistry();
            registry.AddIfAbsent(Create.Device("a"));

            // Act
            var enumerated = 0;

            foreach (var _ in registry)
            {
                registry.AddIfAbsent(Create.Device($"added-{enumerated}"));
                enumerated++;
            }

            // Assert
            // The snapshot taken at loop start must not grow underneath it.
            Assert.Equal(1, enumerated);
            Assert.Equal(2, registry.Count);
        }
    }

    public sealed class Changes : DeviceRegistryTests
    {
        [Fact]
        public void Add_PublishesAdded()
        {
            // Arrange
            var watch = new ChangeRecorder();
            var registry = new DeviceRegistry(watch.Record);

            // Act
            registry.AddIfAbsent(Create.Device("a"));

            // Assert
            var change = Assert.Single(watch.Changes);
            Assert.Equal(NearbyDeviceChangeAction.Added, change.Action);
            Assert.Equal("a", change.Device.Id);
        }

        [Fact]
        public void EveryChange_IsPublishedAfterTheSnapshotHoldsIt()
        {
            // Arrange
            var seen = new List<NearbyDeviceStatus?>();
            DeviceRegistry? registry = null;
            registry = new DeviceRegistry(change => seen.Add(
                registry?.FirstOrDefault(d => d.Id == change.Device.Id)?.Status));

            // Act
            registry.AddIfAbsent(Create.Device("a"));
            registry.Update("a", d => d.With(NearbyDeviceStatus.Connected));
            registry.Remove("a");

            // Assert
            Assert.Equal([NearbyDeviceStatus.Visible, NearbyDeviceStatus.Connected, null], seen);
        }

        [Fact]
        public void RemoveThenReAdd_PublishesRemovedThenAdded()
        {
            // Arrange
            var watch = new ChangeRecorder();
            var registry = new DeviceRegistry(watch.Record);
            registry.AddIfAbsent(Create.Device("a"));

            // Act
            registry.Remove("a");
            registry.AddIfAbsent(Create.Device("a"));

            // Assert
            Assert.Equal(
                [NearbyDeviceChangeAction.Added, NearbyDeviceChangeAction.Removed, NearbyDeviceChangeAction.Added],
                watch.For("a").Select(c => c.Action));
        }

        [Fact]
        public void AddIfAbsent_WhenAlreadyPresent_PublishesNothing()
        {
            // Arrange
            var watch = new ChangeRecorder();
            var registry = new DeviceRegistry(watch.Record);
            registry.AddIfAbsent(Create.Device("a"));

            // Act
            registry.AddIfAbsent(Create.Device("a"));

            // Assert
            Assert.Single(watch.Changes);
        }

        // Suppressing no-op updates is what keeps a bound row from re-rendering on every redundant
        // platform callback; the platforms re-report unchanged state routinely.
        [Fact]
        public void Update_ThatChangesNothing_PublishesNothing()
        {
            // Arrange
            var watch = new ChangeRecorder();
            var registry = new DeviceRegistry(watch.Record);
            registry.AddIfAbsent(Create.Device("a"));

            // Act
            registry.Update("a", d => d);

            // Assert
            Assert.Single(watch.Changes);
        }

        [Fact]
        public void RemoveWhere_PublishesOneChangePerDevice()
        {
            // Arrange
            var watch = new ChangeRecorder();
            var registry = new DeviceRegistry(watch.Record);
            registry.AddIfAbsent(Create.Device("a"));
            registry.AddIfAbsent(Create.Device("b"));

            // Act
            registry.Clear();

            // Assert
            var removals = watch.Changes.Skip(2).ToList();
            Assert.Equal(2, removals.Count);
            Assert.True(removals.All(c => c.Action is NearbyDeviceChangeAction.Removed));
        }
    }

    /// <summary>
    /// Tests for the discovery-generation pair, <see cref="DeviceRegistry.BeginGeneration"/> and
    /// <see cref="DeviceRegistry.EvictUnconfirmed"/>.
    /// </summary>
    public sealed class Generations : DeviceRegistryTests
    {
        [Fact]
        public void EvictUnconfirmed_VisibleDeviceNotReReported_RemovesIt()
        {
            // Arrange
            var registry = new DeviceRegistry();
            registry.AddIfAbsent(Create.Device("a"));

            // Act
            registry.BeginGeneration(static _ => false);
            registry.EvictUnconfirmed();

            // Assert
            Assert.Empty(registry);
        }

        [Fact]
        public void EvictUnconfirmed_VisibleDeviceReReported_KeepsIt()
        {
            // Arrange
            var registry = new DeviceRegistry();
            registry.AddIfAbsent(Create.Device("a"));

            // Act
            registry.BeginGeneration(static _ => false);
            registry.AddIfAbsent(Create.Device("a"));
            registry.EvictUnconfirmed();

            // Assert
            Assert.Single(registry);
        }

        [Fact]
        public void EvictUnconfirmed_ConnectedDeviceWithLiveConnection_KeepsIt()
        {
            // Arrange
            var registry = new DeviceRegistry();
            registry.AddIfAbsent(Create.Device("a", status: NearbyDeviceStatus.Connected));

            // Act
            registry.BeginGeneration(static _ => true);
            registry.EvictUnconfirmed();

            // Assert
            Assert.Single(registry);
        }

        [Fact]
        public void EvictUnconfirmed_ConnectedDeviceWithNoLiveConnection_RemovesIt()
        {
            // Arrange
            var registry = new DeviceRegistry();
            registry.AddIfAbsent(Create.Device("a", status: NearbyDeviceStatus.Connected));

            // Act
            registry.BeginGeneration(static _ => false);
            registry.EvictUnconfirmed();

            // Assert
            Assert.Empty(registry);
        }

        [Theory]
        [InlineData(NearbyDeviceStatus.Connecting)]
        [InlineData(NearbyDeviceStatus.RequestReceived)]
        public void EvictUnconfirmed_MidHandshakeDevice_KeepsIt(NearbyDeviceStatus status)
        {
            // Arrange
            var registry = new DeviceRegistry();
            registry.AddIfAbsent(Create.Device("a", status: status));

            // Act
            registry.BeginGeneration(static _ => false);
            registry.EvictUnconfirmed();

            // Assert
            Assert.Single(registry);
        }
    }
}
