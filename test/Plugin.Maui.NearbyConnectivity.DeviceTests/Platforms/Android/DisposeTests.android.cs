namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// Disposing the platform mid-handshake settles the pending accept — the handshake's TCS is
/// cancelled rather than left dangling, so a consumer awaiting <c>AcceptAsync</c> unblocks. Runs
/// the real <c>Dispose</c> on-device, unlike the <c>net10.0</c> unit equivalent.
/// </summary>
/// <remarks>
/// In the <see cref="StagingTests.Name"/> collection: <c>DisposeAsync</c> sweeps the shared static
/// staging directory, so this must not run in parallel with other classes that stage files there.
/// <para>
/// <strong>Requires a real peer, so skipped in the unattended CI/local run.</strong> Reaching a
/// pending accept means calling <c>AcceptAsync</c>, which calls the real GMS
/// <c>AcceptConnectionAsync</c>. GMS validates that against its own live per-endpoint state, which
/// a synthetic <c>OnConnectionInitiatedAsync</c> does not create, so a single radio-isolated
/// emulator rejects the accept with <c>STATUS_OUT_OF_ORDER_API_CALL</c> (8009) before the handshake
/// ever parks. Same reasoning and trait as <see cref="OfferDeadlineTests"/>. The mechanism itself —
/// dispose cancelling a registered handshake — has net10.0 unit coverage in
/// <c>NearbyTransportTests.ScriptedSeam.DisposeMidHandshake_CancelsThePendingHandshake</c>, which runs everywhere;
/// what only this test adds is the real on-device <c>Dispose</c>.
/// </para>
/// </remarks>
[Collection(StagingTests.Name)]
public sealed class DisposeTests : DeviceTest
{
    [Fact]
    [Trait("Category", "RequiresRealPeer")]
    public async Task DisposeMidHandshake_CancelsPendingAccept()
    {
        // Arrange
        // A real inbound request accepted, so AcceptAsync is awaiting a handshake that
        // will never complete. The accept is what registers the handshake: between "request
        // surfaced" and "accepted" the ledger is deliberately empty, because the offer's deadline
        // lives on the request rather than on a ledger entry.
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await platform.Android().OnConnectionInitiatedAsync("endpoint-1", Create.ConnectionInfo());
        var request = await platform._advertiseChannel.Reader.ReadAsync(cts.Token);
        var acceptTask = request.AcceptAsync(cts.Token);
        await Create.WaitForPendingHandshakeAsync(platform, cts.Token);

        // Act
        await platform.DisposeAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptTask);
    }
}
