namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers <see cref="NearbyOptions.Snapshot"/> — the copy <c>AddNearbyConnectivity</c> captures after
/// validation, so a caller who kept the configured instance cannot mutate the session's
/// configuration.
/// </summary>
[Trait("Category", "Options")]
public class NearbyOptionsSnapshotTests
{
    public sealed class Scalars : NearbyOptionsSnapshotTests
    {
        [Fact]
        public void Snapshot_CopiesEveryScalar()
        {
            // Arrange
            var options = new NearbyOptions
            {
                Presence = new() { DisplayName = "kiosk-7" },
                ServiceId = "nearbychat",
                DiscoveryRefreshInterval = TimeSpan.FromSeconds(5),
                OfferWindow = TimeSpan.FromSeconds(1),
                FileTransferInactivityTimeout = TimeSpan.FromSeconds(4),
            };

            // Act
            var snapshot = options.Snapshot();

            // Assert
            Assert.Equal("kiosk-7", snapshot.Presence.DisplayName);
            Assert.Equal("nearbychat", snapshot.ServiceId);
            Assert.Equal(TimeSpan.FromSeconds(5), snapshot.DiscoveryRefreshInterval);
            Assert.Equal(TimeSpan.FromSeconds(1), snapshot.OfferWindow);
            Assert.Equal(TimeSpan.FromSeconds(4), snapshot.FileTransferInactivityTimeout);
        }

        [Fact]
        public void Snapshot_IsUnaffectedByLaterMutation()
        {
            // Arrange
            var options = new NearbyOptions { ServiceId = "before" };
            var snapshot = options.Snapshot();

            // Act
            options.ServiceId = "after";

            // Assert
            Assert.Equal("before", snapshot.ServiceId);
        }
    }

    public sealed class PlatformScopes : NearbyOptionsSnapshotTests
    {
        [Fact]
        public void Snapshot_CopiesThePlatformScope()
        {
            // Arrange
            var options = new NearbyOptions();
            options.Android.Topology = NearbyTopology.Star;
            options.Android.UseLowPower = true;
            options.Android.ConnectionType = NearbyConnectionType.HighBandwidth;

            // Act
            var snapshot = options.Snapshot();

            // Assert
            Assert.Equal(NearbyTopology.Star, snapshot.Android.Topology);
            Assert.True(snapshot.Android.UseLowPower);
            Assert.Equal(NearbyConnectionType.HighBandwidth, snapshot.Android.ConnectionType);
        }

        [Fact]
        public void Snapshot_DoesNotShareScopeInstances()
        {
            // Arrange
            var options = new NearbyOptions();
            var snapshot = options.Snapshot();

            // Act
            options.Android.Topology = NearbyTopology.PointToPoint;

            // Assert
            // The snapshot's scope objects must be copies, not the caller's instances.
            Assert.Equal(NearbyTopology.Cluster, snapshot.Android.Topology);
        }
    }
}
