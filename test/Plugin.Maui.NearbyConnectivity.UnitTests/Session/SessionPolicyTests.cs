namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// The loop, lifetime and policy shapes the library absorbs so an application does not rebuild
/// them.
/// </summary>
[Trait("Category", "Session")]
public class SessionPolicyTests
{
    /// <summary>
    /// The availability preflight the start verbs absorb, so no consumer writes it.
    /// </summary>
    public sealed class GuardedStart : SessionPolicyTests
    {
        [Fact]
        public async Task Available_Starts()
        {
            // Arrange
            var connections = new FakeNearby { Availability = NearbyAvailability.Available };
            var session = Create.Session(connections);

            // Act
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            // Assert
            Assert.True(session.Discoverer.IsActive);
        }

        // The classification a consumer most often gets wrong: Bluetooth alone still carries a
        // connection, so Wi-Fi being off must not refuse a working setup.
        [Fact]
        public async Task WifiDisabled_StillStarts()
        {
            // Arrange
            var connections = new FakeNearby { Availability = NearbyAvailability.WifiDisabled };
            var session = Create.Session(connections);

            // Act
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            // Assert
            Assert.True(session.Discoverer.IsActive);
            Assert.Equal(NearbyAvailability.WifiDisabled, session.Availability);
        }

        [Fact]
        public async Task Blocked_ThrowsAndDoesNotStart()
        {
            // Arrange
            var connections = new FakeNearby { Availability = NearbyAvailability.MissingPermissions };
            var session = Create.Session(connections);

            // Act
            var ex = await Assert.ThrowsAsync<NearbyException>(
                () => session.Discoverer.StartAsync(TestContext.Current.CancellationToken));

            // Assert
            Assert.Contains(nameof(NearbyAvailability.MissingPermissions), ex.Message, StringComparison.Ordinal);
            Assert.False(session.Discoverer.IsActive);
            Assert.Equal(NearbyAvailability.MissingPermissions, session.Availability);
        }
    }

    public sealed class Consent : SessionPolicyTests
    {
        [Fact]
        public async Task AcceptAll_ConnectsWithoutPublishingARequest()
        {
            // Arrange
            var connections = new FakeNearby();
            var session = Create.Session(connections);
            session.Advertiser.SetConsent(NearbyConsentPolicy.AcceptAll);
            await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
            var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));

            // Act
            await connections.EmitRequestAsync(device, () => Create.Connection(device));

            // Assert
            await Wait.UntilAsync(() => session.StatusOf("peer-1") == NearbyDeviceStatus.Connected);
            Assert.Null(session.Current("peer-1")?.Request);
        }

        // The escape hatch: a null decision hands the request back to a human rather than
        // forcing the policy on it.
        [Fact]
        public async Task DecideReturningNull_HoldsTheRequest()
        {
            // Arrange
            var connections = new FakeNearby();
            var session = Create.Session(connections);
            session.Advertiser.SetConsent(
                NearbyConsentPolicy.Hold,
                decide: (_, _) => ValueTask.FromResult<bool?>(null));
            await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
            var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));

            // Act
            await connections.EmitRequestAsync(device, () => Create.Connection(device));

            // Assert
            var request = await Take.RequestAsync(session, TestContext.Current.CancellationToken);
            Assert.True(request.IsPending);
        }

        // A throwing predicate must not lose the request: holding it is the outcome that keeps
        // the human in the loop.
        [Fact]
        public async Task DecideThrowing_HoldsTheRequest()
        {
            // Arrange
            var connections = new FakeNearby();
            var session = Create.Session(connections);
            session.Advertiser.SetConsent(
                NearbyConsentPolicy.Hold,
                decide: (_, _) => throw new InvalidOperationException("rule broke"));
            await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
            var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));

            // Act
            await connections.EmitRequestAsync(device, () => Create.Connection(device));

            // Assert
            var request = await Take.RequestAsync(session, TestContext.Current.CancellationToken);
            Assert.True(request.IsPending);
        }
    }

}
