using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers <see cref="DiscoveryRefresher"/> — the duty that restarts discovery on an interval and
/// evicts the devices the new pass did not re-report. Death policy: degrade loudly — a failed
/// refresh stops the duty and reports it, while discovery itself continues.
/// </summary>
[Trait("Category", "Discovery")]
public class DiscoveryRefresherTests
{
    static readonly TimeSpan s_interval = TimeSpan.FromSeconds(30);
    static readonly TimeSpan s_settle = TimeSpan.FromSeconds(1);

    public sealed class Ticking : DiscoveryRefresherTests
    {
        [Fact]
        public async Task EachInterval_RunsTheRefreshDelegate()
        {
            // Arrange
            var refreshes = 0;
            var time = new FakeTimeProvider();
            var refresher = Create.Refresher(
                time,
                new DeviceRegistry(),
                _ => { refreshes++; return Task.FromResult(true); },
                s_interval,
                settleWindow: s_settle);
            refresher.Start();

            // Act
            time.Advance(s_interval);
            await Wait.UntilAsync(() => refreshes == 1);
            time.Advance(s_settle);
            time.Advance(s_interval);
            await Wait.UntilAsync(() => refreshes == 2);

            // Assert
            Assert.Equal(2, refreshes);

            await refresher.CancelAsync();
            await refresher.DrainAsync();
        }

        [Fact]
        public async Task RefreshReportingDiscoveryStopped_EndsTheLoop()
        {
            // Arrange
            var refreshes = 0;
            var time = new FakeTimeProvider();
            var refresher = Create.Refresher(
                time,
                new DeviceRegistry(),
                _ => { refreshes++; return Task.FromResult(false); },
                s_interval,
                settleWindow: s_settle);
            refresher.Start();

            // Act
            time.Advance(s_interval);
            await Wait.UntilAsync(() => refreshes == 1);
            await refresher.DrainAsync();
            time.Advance(s_interval + s_interval);

            // Assert
            Assert.Equal(1, refreshes);
        }

        [Fact]
        public async Task NoInterval_NeverStarts()
        {
            // Arrange
            var refreshes = 0;
            var time = new FakeTimeProvider();
            var refresher = Create.Refresher(
                time,
                new DeviceRegistry(),
                _ => { refreshes++; return Task.FromResult(true); },
                interval: null);

            // Act
            refresher.Start();
            time.Advance(TimeSpan.FromDays(1));
            await refresher.DrainAsync();

            // Assert
            Assert.Equal(0, refreshes);
        }
    }

    public sealed class Eviction : DiscoveryRefresherTests
    {
        [Fact]
        public async Task DeviceNotReconfirmed_IsEvictedAfterTheSettleWindow()
        {
            // Arrange
            var registry = new DeviceRegistry();
            registry.AddIfAbsent(Create.Device("peer-1"));
            var time = new FakeTimeProvider();
            var refresher = Create.Refresher(
                time,
                registry,
                _ => { registry.BeginGeneration(static _ => false); return Task.FromResult(true); },
                s_interval,
                settleWindow: s_settle);
            refresher.Start();

            // Act
            time.Advance(s_interval);
            await Wait.UntilAsync(() => registry.Count == 1); // refresh ran, not yet evicted
            time.Advance(s_settle);
            await Wait.UntilAsync(() => registry.Count == 0);

            // Assert
            Assert.Empty(registry);

            await refresher.CancelAsync();
            await refresher.DrainAsync();
        }
    }

    public sealed class DeathPolicy : DiscoveryRefresherTests
    {
        [Fact]
        public async Task FailingRefresh_ReportsOnceAndStops()
        {
            // Arrange
            var failures = new List<Exception>();
            var refreshes = 0;
            var time = new FakeTimeProvider();
            var refresher = Create.Refresher(
                time,
                new DeviceRegistry(),
                _ => { refreshes++; throw new InvalidOperationException("radio broke"); },
                s_interval,
                onFailed: failures.Add,
                settleWindow: s_settle);
            refresher.Start();

            // Act
            time.Advance(s_interval);
            await Wait.UntilAsync(() => failures.Count == 1);
            await refresher.DrainAsync();
            time.Advance(s_interval + s_interval);

            // Assert
            Assert.Equal(1, refreshes);
            Assert.Equal("radio broke", Assert.Single(failures).Message);
        }
    }

    public sealed class StartRacingDrain : DiscoveryRefresherTests
    {
        [Fact]
        public async Task SecondStart_DoesNotStrandTheRunningLoop()
        {
            // Arrange
            var refreshes = 0;
            var time = new FakeTimeProvider();
            var refresher = Create.Refresher(
                time,
                new DeviceRegistry(),
                _ => { refreshes++; return Task.FromResult(true); },
                s_interval,
                settleWindow: s_settle);

            // Act
            refresher.Start();
            refresher.Start();
            time.Advance(s_interval);
            await Wait.UntilAsync(() => refreshes == 1);
            await refresher.CancelAsync();
            await refresher.DrainAsync();
            time.Advance(s_interval + s_interval);

            // Assert
            Assert.Equal(1, refreshes);
        }
    }
}
