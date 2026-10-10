namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// The availability preflight against real Google Play Services and real radio/permission state.
/// The result is environment-dependent (emulator image, granted permissions), so the contract
/// pinned here is the documented one: the check completes, never throws, and never prompts.
/// </summary>
public sealed class AvailabilityTests : DeviceTest
{
    [Fact]
    public async Task CheckAvailability_CompletesWithoutThrowing()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        var result = await platform.CheckAvailabilityAsync(cts.Token);

        // Assert
        // Every reported flag is a defined NearbyAvailability value.
        var allKnown = NearbyAvailability.Available
            | NearbyAvailability.MissingPermissions
            | NearbyAvailability.BluetoothDisabled
            | NearbyAvailability.WifiDisabled
            | NearbyAvailability.PlayServicesUnavailable
            | NearbyAvailability.Unsupported
            | NearbyAvailability.InvalidConfiguration;
        Assert.Equal(result, result & allKnown);
    }

    /// <summary>
    /// The one real start: a maximum-sized presence produces a 131-byte endpoint-info frame, and
    /// GMS accepts it.
    /// </summary>
    /// <remarks>
    /// This verifies the reading that Android's documented 131-byte pre-connection limit is
    /// inclusive, which is what <see cref="NearbyPresence.MaxInfoBytes"/> is derived from. If GMS
    /// rejects the frame, the cap drops to 56 and the docs say 56.
    /// </remarks>
    [Fact]
    public async Task StartAdvertising_WithAMaxSizedPresence_IsAcceptedByGms()
    {
        // Arrange
        var options = Create.DefaultOptions(
            displayName: new string('x', NearbyPresence.MaxDisplayNameBytes),
            info: new byte[NearbyPresence.MaxInfoBytes]);

        await using var platform = Create.NearbyTransport(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        Assert.Equal(
            ControlMessage.EndpointInfoBudget,
            ControlMessage.EncodeConnectRequest(options.OfferWindow, options.Presence).Length);

        // Act & Assert
        // A NearbyException here means GMS refused the 131-byte frame.
        await platform.Android().StartAdvertisingAsync(cts.Token);

        platform.Android().StopAdvertising();
    }
}
