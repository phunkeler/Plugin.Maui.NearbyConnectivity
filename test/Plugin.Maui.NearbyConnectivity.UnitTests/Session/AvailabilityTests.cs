namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Session")]
public sealed class AvailabilityTests
{
    [Fact]
    public async Task Always_DelegatesToThePlatform()
    {
        // Arrange
        var connections = new FakeNearby { Availability = NearbyAvailability.Available };
        var session = Create.Session(connections);

        // Act
        var result = await session.CheckAvailabilityAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(NearbyAvailability.Available, result);
        Assert.Equal(1, connections.CheckAvailabilityCallCount);
    }

    [Fact]
    public async Task MultipleProblems_AreReportedTogether()
    {
        // The whole reason this is a [Flags] enum: a user with Bluetooth off AND permissions
        // denied should be told both at once, not made to fix one and retry to discover the
        // other.

        // Arrange
        var connections = new FakeNearby
        {
            Availability = NearbyAvailability.BluetoothDisabled | NearbyAvailability.MissingPermissions,
        };
        var session = Create.Session(connections);

        // Act
        var result = await session.CheckAvailabilityAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.HasFlag(NearbyAvailability.BluetoothDisabled));
        Assert.True(result.HasFlag(NearbyAvailability.MissingPermissions));
        Assert.False(result.HasFlag(NearbyAvailability.PlayServicesUnavailable));
    }

    [Fact]
    public async Task ProblemFlags_DoNotCompareEqualToReady()
    {
        // Ready = 0 is what makes `result is NearbyAvailability.Available` a valid readiness test.
        // If Ready ever gained a non-zero value, or a problem flag were assigned 0, that idiom
        // would silently start reporting a broken device as usable.

        // Arrange
        var connections = new FakeNearby
        {
            Availability = NearbyAvailability.MissingPermissions,
        };
        var session = Create.Session(connections);

        // Act
        var result = await session.CheckAvailabilityAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(NearbyAvailability.Available, result);
    }

    [Fact]
    public async Task DoesNotStartAdvertisingOrDiscovery()
    {
        // A preflight check must not have side effects: it reports state, it does not mutate it.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);

        // Act
        await session.CheckAvailabilityAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(session.Advertiser.IsActive);
        Assert.False(session.Discoverer.IsActive);
        Assert.Equal(0, connections.AdvertiseCallCount);
        Assert.Equal(0, connections.DiscoverCallCount);
    }

    [Fact]
    public async Task CanceledToken_Throws()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => session.CheckAvailabilityAsync(cts.Token));
    }
}
