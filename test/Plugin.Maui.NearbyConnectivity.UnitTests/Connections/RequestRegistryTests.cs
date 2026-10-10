using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers <see cref="RequestRegistry"/> — the one owner of "an inbound request is outstanding for
/// device X". The atomic claim is the arbiter between accept, reject, and expiry: exactly one wins.
/// Expiry is driven by the request's own offer deadline, computed at receipt from the window the
/// initiator declared.
/// </summary>
[Trait("Category", "Connections")]
public class RequestRegistryTests
{
    static readonly TimeSpan s_window30 = TimeSpan.FromSeconds(30);

    public sealed class Tracking : RequestRegistryTests
    {
        [Fact]
        public async Task Track_ArmsTheCountdownForTheRequestsOwnDeadline()
        {
            // Arrange
            var expiredRequests = new List<NearbyConnectionRequest>();
            var time = new FakeTimeProvider();
            var registry = Create.RequestRegistry(
                time,
                onExpired: r => { expiredRequests.Add(r); return Task.CompletedTask; });
            var request = Create.Request(deadline: time.GetUtcNow() + s_window30);

            // Act
            registry.Track(request);

            // Assert
            Assert.True(registry.Contains("peer-1"));

            time.Advance(s_window30 - TimeSpan.FromSeconds(1));
            Assert.Empty(expiredRequests);

            time.Advance(TimeSpan.FromSeconds(1));
            await Wait.UntilAsync(() => expiredRequests.Count > 0);
            Assert.Same(request, Assert.Single(expiredRequests));
        }

        [Fact]
        public async Task Track_ClampsADistantDeadlineToTheTrustBound()
        {
            // Every tracked request has a finite countdown: the registry re-clamps to
            // OfferWindowPolicy.Max even when a caller hands it a deadline beyond the bound, so a
            // remote initiator cannot pin a row open indefinitely.

            // Arrange
            var expiredRequests = new List<NearbyConnectionRequest>();
            var time = new FakeTimeProvider();
            var registry = Create.RequestRegistry(
                time,
                onExpired: r => { expiredRequests.Add(r); return Task.CompletedTask; });
            var request = Create.Request(deadline: DateTimeOffset.MaxValue);

            // Act
            registry.Track(request);

            // Assert
            // The declared window is unbounded, the armed one is Max.
            time.Advance(OfferWindowPolicy.s_max - TimeSpan.FromSeconds(1));
            Assert.Empty(expiredRequests);

            time.Advance(TimeSpan.FromSeconds(1));
            await Wait.UntilAsync(() => expiredRequests.Count > 0);
            Assert.Same(request, Assert.Single(expiredRequests));
        }

        [Fact]
        public async Task Track_WithADeadlineAlreadyPast_ExpiresImmediately()
        {
            // A degenerate declared window is honored: a zero or elapsed window lands
            // already-expired and auto-rejects. It only hurts the sender.

            // Arrange
            var expiredRequests = new List<NearbyConnectionRequest>();
            var time = new FakeTimeProvider();
            var registry = Create.RequestRegistry(
                time,
                onExpired: r => { expiredRequests.Add(r); return Task.CompletedTask; });
            var request = Create.Request(deadline: time.GetUtcNow() - TimeSpan.FromSeconds(1));

            // Act
            // No clock movement: a zero remaining window arms an already-elapsed timer.
            registry.Track(request);
            await Wait.UntilAsync(() => expiredRequests.Count > 0);

            // Assert
            Assert.Same(request, Assert.Single(expiredRequests));
        }
    }

    public sealed class Claiming : RequestRegistryTests
    {
        [Fact]
        public void TryClaim_WinsExactlyOnce()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var registry = Create.RequestRegistry(time);
            var request = Create.Request(deadline: time.GetUtcNow() + s_window30);
            registry.Track(request);

            // Act
            var first = registry.TryClaim(request);
            var second = registry.TryClaim(request);

            // Assert
            Assert.True(first);
            Assert.False(second);
            Assert.False(registry.Contains("peer-1"));
        }

        [Fact]
        public async Task ClaimedRequest_NeverExpires()
        {
            // The accept side of the race: once claimed, the timer must be a no-op.

            // Arrange
            var expired = 0;
            var time = new FakeTimeProvider();
            var registry = Create.RequestRegistry(
                time,
                onExpired: _ => { expired++; return Task.CompletedTask; });
            var request = Create.Request(deadline: time.GetUtcNow() + s_window30);
            registry.Track(request);

            // Act
            registry.TryClaim(request);
            time.Advance(s_window30 + TimeSpan.FromSeconds(1));
            await Task.Yield();

            // Assert
            Assert.Equal(0, expired);
        }

        [Fact]
        public async Task ExpiredRequest_CannotBeClaimed()
        {
            // The expiry side of the race, and the dead tail pinned as intended behavior: the
            // advertiser's deadline lags the initiator's by the transit time, so an accept inside
            // that tail loses the claim — the session gateway then throws
            // NearbyConnectException, exactly as any late accept does.

            // Arrange
            var expiredRequests = new List<NearbyConnectionRequest>();
            var time = new FakeTimeProvider();
            var registry = Create.RequestRegistry(
                time,
                onExpired: r => { expiredRequests.Add(r); return Task.CompletedTask; });
            var request = Create.Request(deadline: time.GetUtcNow() + s_window30);
            registry.Track(request);

            // Act
            time.Advance(s_window30 + TimeSpan.FromSeconds(1));
            await Wait.UntilAsync(() => expiredRequests.Count > 0);
            var claimedAfterExpiry = registry.TryClaim(request);

            // Assert
            Assert.Same(request, Assert.Single(expiredRequests));
            Assert.False(claimedAfterExpiry);
            Assert.False(registry.Contains("peer-1"));
        }

        [Fact]
        public async Task NewRequestForTheSameDevice_ReplacesTheOldTimer()
        {
            // The old timer must not stay armed against the replacement request. Each request
            // carries its own deadline, so the replacement expires at its own instant.

            // Arrange
            var expiredRequests = new List<NearbyConnectionRequest>();
            var time = new FakeTimeProvider();
            var registry = Create.RequestRegistry(
                time,
                onExpired: r => { expiredRequests.Add(r); return Task.CompletedTask; });
            var first = Create.Request(deadline: time.GetUtcNow() + s_window30);

            // Act
            // The second Track replaces the first; only the second's deadline fires.
            registry.Track(first);
            time.Advance(TimeSpan.FromSeconds(15));
            var second = Create.Request(deadline: time.GetUtcNow() + s_window30);
            registry.Track(second);
            time.Advance(s_window30 + TimeSpan.FromSeconds(1));
            await Wait.UntilAsync(() => expiredRequests.Count > 0);

            // Assert
            Assert.Same(second, Assert.Single(expiredRequests));
        }
    }

    public sealed class Teardown : RequestRegistryTests
    {
        [Fact]
        public async Task ClaimAll_ClaimsEverythingAndCancelsEveryTimer()
        {
            // Arrange
            var expired = 0;
            var time = new FakeTimeProvider();
            var registry = Create.RequestRegistry(
                time,
                onExpired: _ => { expired++; return Task.CompletedTask; });
            registry.Track(Create.Request(Create.Device("peer-1"), time.GetUtcNow() + s_window30));
            registry.Track(Create.Request(Create.Device("peer-2"), time.GetUtcNow() + s_window30));

            // Act
            var claimed = registry.ClaimAll();
            time.Advance(s_window30 + TimeSpan.FromSeconds(1));
            await Task.Yield();

            // Assert
            Assert.Equal(2, claimed.Length);
            Assert.Equal(0, expired);
            Assert.False(registry.Contains("peer-1"));
            Assert.False(registry.Contains("peer-2"));
        }
    }
}
