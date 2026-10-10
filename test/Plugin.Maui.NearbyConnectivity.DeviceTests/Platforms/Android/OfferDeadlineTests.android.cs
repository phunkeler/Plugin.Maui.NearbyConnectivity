namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// The accept path's bound on Android is the offer's remaining window — the deadline the initiator
/// declared in the connect-request frame. <c>AcceptAsync</c> awaits a
/// <see cref="TaskCompletionSource{TResult}"/> that only a terminal GMS callback resolves, and a
/// device that leaves range mid-handshake produces no such callback. Without the offer's deadline
/// the await never returns.
/// </summary>
/// <remarks>
/// Real time, not a fake clock: these run against the real platform partial, so the declared
/// windows are deliberately short rather than injected. The 100 ms window is the shortest value
/// whose frame bytes survive <see cref="Create.ConnectionInfoWithFrame"/>'s string round-trip.
/// <para>
/// <strong>Requires a real peer, so skipped in the unattended CI/local run.</strong>
/// <c>AcceptAsync</c> calls the real GMS <c>AcceptConnectionAsync</c>, which validates against
/// GMS's own live per-endpoint connection state. That state only exists once GMS has itself
/// processed a genuine <c>requestConnection</c> from a second device — a synthetic
/// <c>OnConnectionInitiatedAsync</c> call does not create it, so on a single, radio-isolated
/// emulator GMS always rejects the accept with <c>STATUS_OUT_OF_ORDER_API_CALL</c> (8009),
/// verified against a real advertising session (<see cref="INearbyTransport.AdvertiseAsync"/>)
/// and not just a channel write. Same "call the real GMS-backed API, tag what needs a live
/// environment" pattern as dotnet/maui's own device tests
/// (<c>Geolocation_Tests.cs</c>, <c>Traits.InteractionType</c>/<c>Human</c>), which is why these
/// are excluded by trait rather than deleted or faked. The deadline mechanics themselves
/// (<c>AwaitHandshakeAsync</c>) still have unit coverage that runs everywhere.
/// </para>
/// </remarks>
public sealed class OfferDeadlineTests : DeviceTest
{
    /// <summary>
    /// Marks a test that needs a real second device or radio and so cannot run against a single,
    /// radio-isolated emulator/simulator. Excluded from the unattended device-test run via
    /// <c>dotnet test --filter</c> in <c>scripts/device-tests.ps1</c>.
    /// </summary>
    const string RequiresRealPeerTrait = "RequiresRealPeer";

    static readonly TimeSpan s_shortWindow = TimeSpan.FromMilliseconds(100);

    [Fact]
    [Trait("Category", RequiresRealPeerTrait)]
    public async Task AcceptedRequest_WithNoTerminalCallback_TimesOutAtTheOfferDeadline()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await platform.Android().OnConnectionInitiatedAsync(
            "endpoint-1", Create.ConnectionInfoWithFrame(s_shortWindow));
        var request = await platform._advertiseChannel.Reader.ReadAsync(cts.Token);

        // Act
        // Accept, then never deliver OnConnectionResult.
        var pending = request.AcceptAsync(CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<NearbyConnectException>(() => pending);
    }

    [Fact]
    [Trait("Category", RequiresRealPeerTrait)]
    public async Task OfferDeadline_ClearsThePendingHandshakeEntry()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await platform.Android().OnConnectionInitiatedAsync(
            "endpoint-1", Create.ConnectionInfoWithFrame(s_shortWindow));
        var request = await platform._advertiseChannel.Reader.ReadAsync(cts.Token);

        // Act
        await Assert.ThrowsAsync<NearbyConnectException>(
            () => request.AcceptAsync(CancellationToken.None));

        // Assert
        // A stranded entry would leak, and would make a later attempt to the same endpoint
        // resolve the wrong handshake.
        Assert.True(platform.Handshakes.IsEmpty);
    }

    [Fact]
    [Trait("Category", RequiresRealPeerTrait)]
    public async Task AcceptedRequest_WhenResultArrivesFirst_ReturnsTheConnection()
    {
        // Arrange
        // The default 30s window (no frame): the deadline must not fire on a handshake
        // that completes normally.
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await platform.Android().OnConnectionInitiatedAsync("endpoint-1", Create.ConnectionInfo());
        var request = await platform._advertiseChannel.Reader.ReadAsync(cts.Token);

        // Act
        var pending = request.AcceptAsync(cts.Token);
        platform.Android().OnConnectionResult("endpoint-1", Create.Resolution());

        // Assert
        var connection = await pending;
        Assert.Equal("endpoint-1", connection.RemoteDevice.Id);
    }
}
