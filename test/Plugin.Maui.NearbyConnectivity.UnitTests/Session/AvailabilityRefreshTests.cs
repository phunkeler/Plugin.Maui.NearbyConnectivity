namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers the session half of the platform availability signal: what a radio state broadcast
/// triggers, and what reaches <see cref="INearbyConnectivity.AvailabilityChanged"/> as a result.
/// </summary>
/// <remarks>
/// The Android receiver that calls this is device-only. What is testable on <c>net10.0</c> is that
/// a platform-side change raises the event at all, without the application polling for it.
/// </remarks>
[Trait("Category", "Session")]
public sealed class AvailabilityRefreshTests
{
    [Fact]
    public async Task APlatformStateChange_RaisesAvailabilityChangedWithoutPolling()
    {
        // The event must be raised without the consumer calling CheckAvailabilityAsync, so a
        // status line reacts to the user turning Bluetooth off.

        // Arrange
        var platform = new FakeNearby { Availability = NearbyAvailability.Available };
        await using var session = Create.Session(platform);
        using var seen = Observe.Availability(session);

        // Act
        // The platform's state moves, then it raises its signal, with no consumer call.
        platform.Availability = NearbyAvailability.BluetoothDisabled;
        platform.EmitAvailabilityChanged();
        await seen.WaitForAsync(1);

        // Assert
        Assert.Equal([NearbyAvailability.BluetoothDisabled], seen.Seen);
    }

    [Fact]
    public async Task ARefreshThatFindsNoChange_RaisesNothing()
    {
        // A radio toggle while permissions are missing does not change the worst-first value, and
        // raising it again would wake every handler for nothing.

        // Arrange
        var platform = new FakeNearby { Availability = NearbyAvailability.MissingPermissions };
        await using var session = Create.Session(platform);
        await session.CheckAvailabilityAsync(TestContext.Current.CancellationToken);
        using var seen = Observe.Availability(session);

        // Act
        // The platform reports the same value again, then a real change.
        platform.EmitAvailabilityChanged();
        await Wait.UntilAsync(() => platform.CheckAvailabilityCallCount >= 2);
        platform.Availability = NearbyAvailability.Available;
        await session.CheckAvailabilityAsync(TestContext.Current.CancellationToken);
        await seen.WaitForAsync(1);

        // Assert
        Assert.Equal([NearbyAvailability.Available], seen.Seen);
    }

    [Fact]
    public async Task ARefreshUpdatesTheCachedValue_ForALaterReader()
    {
        // Availability is the read-now half of the pair; a push that moved only the stream would
        // leave a consumer reading the property with a stale answer.

        // Arrange
        var platform = new FakeNearby { Availability = NearbyAvailability.Available };
        await using var session = Create.Session(platform);

        await session.CheckAvailabilityAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NearbyAvailability.Available, session.Availability);

        // Act
        platform.Availability = NearbyAvailability.WifiDisabled;
        platform.EmitAvailabilityChanged();

        // Assert
        await Wait.UntilAsync(() => session.Availability == NearbyAvailability.WifiDisabled);

        Assert.Equal(NearbyAvailability.WifiDisabled, session.Availability);
    }

    [Fact]
    public async Task ARefreshThatThrows_LeavesTheSessionUsable()
    {
        // The signal fires from a platform broadcast, so a failing probe must not surface as an
        // unobserved task exception or take the session with it.

        // Arrange
        var platform = new FakeNearby { AvailabilityFault = new InvalidOperationException("probe failed") };
        await using var session = Create.Session(platform);

        // Act
        platform.EmitAvailabilityChanged();

        // Assert
        // The session still answers, and the throw was swallowed into the log.
        platform.AvailabilityFault = null;
        platform.Availability = NearbyAvailability.Available;

        Assert.Equal(
            NearbyAvailability.Available,
            await session.CheckAvailabilityAsync(TestContext.Current.CancellationToken));
    }
}
