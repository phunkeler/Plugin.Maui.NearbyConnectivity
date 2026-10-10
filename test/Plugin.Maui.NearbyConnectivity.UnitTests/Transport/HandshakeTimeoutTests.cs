using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Session")]
public sealed class HandshakeTimeoutTests
{
    [Fact]
    public async Task ConnectAsync_TimeoutIsArmedFromOptions()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var platform = Create.NearbyTransport(time, new NearbyOptions { ServiceId = "test-service", OfferWindow = TimeSpan.FromSeconds(5) });

        // Assert
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => platform.ConnectAsync(Create.Device("peer-1", "Alice"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConnectAsync_WhenPlatformFails_DoesNotStrandThePendingEntry()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var platform = Create.NearbyTransport(time, new NearbyOptions { ServiceId = "test-service" });
        var device = Create.Device("peer-1", "Alice");

        // Act
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => platform.ConnectAsync(device, TestContext.Current.CancellationToken));

        // Assert
        // No pending handshake and no connection: the failed attempt left nothing behind.
        Assert.True(platform.IsIdle);
    }

    [Fact]
    public async Task ConnectAsync_CallerCancellation_ReportsCancellationNotTimeout()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var platform = Create.NearbyTransport(time, new NearbyOptions { ServiceId = "test-service" });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(
            () => platform.ConnectAsync(Create.Device("peer-1", "Alice"), cts.Token));

        // Assert
        Assert.IsNotAssignableFrom<NearbyConnectException>(ex);
    }

    // The duration is a caller-supplied parameter — the offer's one deadline, not a role-based
    // options lookup. The tests below set OfferWindow to a different value on purpose: a
    // wire-up that read the option instead of the parameter would still pass otherwise.
    [Fact]
    public async Task AwaitHandshake_IsBoundedByTheSuppliedDuration_NotAnOption()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var supplied = TimeSpan.FromSeconds(5);
        var platform = Create.NearbyTransport(time, new NearbyOptions
        {
            ServiceId = "test-service",
            OfferWindow = TimeSpan.FromSeconds(30),
        });
        var device = Create.Device("peer-1", "Alice");
        var tcs = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: CancellationToken.None);

        // Act
        var handshake = platform.AwaitHandshakeAsync(
            device,
            tcs,
            isInitiator: false,
            supplied,
            beforeAwait: _ => Task.CompletedTask,
            CancellationToken.None);

        time.Advance(supplied);

        // Assert
        // The supplied window governs, well before OfferWindow would fire.
        await Assert.ThrowsAsync<NearbyConnectException>(() => handshake);
    }

    [Fact]
    public async Task AwaitHandshake_DoesNotFireBeforeTheSuppliedDuration()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var supplied = TimeSpan.FromSeconds(30);
        var platform = Create.NearbyTransport(time, new NearbyOptions
        {
            ServiceId = "test-service",
            OfferWindow = TimeSpan.FromSeconds(5),
        });
        var device = Create.Device("peer-1", "Alice");
        var tcs = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: CancellationToken.None);

        // Act
        var handshake = platform.AwaitHandshakeAsync(
            device,
            tcs,
            isInitiator: true,
            supplied,
            beforeAwait: _ => Task.CompletedTask,
            CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(5));

        // Assert
        // Past the (smaller) option value, the supplied window must still be waiting.
        // Awaited with a real deadline so a regression that fires early fails here rather than
        // hanging the suite.
        var settled = await Task.WhenAny(
            handshake,
            Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.NotSame(handshake, settled);
        time.Advance(supplied - TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<NearbyConnectException>(() => handshake);
    }

    [Fact]
    public async Task AwaitHandshake_AsAcceptor_ReportsTheOfferDeadlineInItsMessage()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var remaining = TimeSpan.FromSeconds(5);
        var platform = Create.NearbyTransport(time, new NearbyOptions { ServiceId = "test-service" });
        var device = Create.Device("peer-1", "Alice");
        var tcs = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: CancellationToken.None);

        // Act
        var handshake = platform.AwaitHandshakeAsync(
            device,
            tcs,
            isInitiator: false,
            remaining,
            beforeAwait: _ => Task.CompletedTask,
            CancellationToken.None);

        time.Advance(remaining);
        var ex = await Assert.ThrowsAsync<NearbyConnectException>(() => handshake);

        // Assert
        // The message must quote the window that actually fired, and name the offer.
        Assert.Contains("expired", ex.Message);
        Assert.Contains("5s", ex.Message);
    }

    [Fact]
    public async Task AwaitHandshake_WhenTheHandshakeIsCancelledByTeardown_DoesNotReportATimeout()
    {
        // Arrange
        // DisposeAsync settles a pending handshake by cancelling its TCS. That is neither
        // the caller's token nor the deadline, and it must not be reported as an elapsed deadline.
        var time = new FakeTimeProvider();
        var platform = Create.NearbyTransport(time, new NearbyOptions { ServiceId = "test-service" });
        var device = Create.Device("peer-1", "Alice");
        var tcs = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: CancellationToken.None);

        var handshake = platform.AwaitHandshakeAsync(
            device,
            tcs,
            isInitiator: false,
            TimeSpan.FromSeconds(15),
            beforeAwait: _ => Task.CompletedTask,
            CancellationToken.None);

        // Act
        // The clock never advances, so no deadline elapses.
        tcs.TrySetCanceled(CancellationToken.None);

        // Assert
        // A teardown is neither of the deadline exceptions. The acceptor's deadline
        // raises NearbyConnectException, so guarding only the timeout type would let a
        // regression through on this path.
        var ex = await Assert.ThrowsAsync<TaskCanceledException>(() => handshake);
        Assert.IsNotAssignableFrom<NearbyConnectException>(ex);
        Assert.IsNotAssignableFrom<NearbyConnectException>(ex);
    }

    // The accept path's deadline, exercised through AwaitHandshakeAsync directly. The platform
    // accept lambdas that call it are unreachable on net10.0, but the deadline itself is shared
    // code, and it is the part that regressed: before it was extracted, both platforms awaited the
    // caller's token alone, so an accepted handshake that never completed hung forever.
    [Fact]
    public async Task AwaitHandshake_WhenNoTerminalCallbackArrives_TimesOutInsteadOfHanging()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var timeout = TimeSpan.FromSeconds(5);
        var platform = Create.NearbyTransport(time, new NearbyOptions { ServiceId = "test-service" });
        var device = Create.Device("peer-1", "Alice");
        var tcs = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: CancellationToken.None);

        // Act
        var handshake = platform.AwaitHandshakeAsync(
            device,
            tcs,
            isInitiator: true,
            timeout,
            beforeAwait: _ => Task.CompletedTask,
            CancellationToken.None);

        time.Advance(timeout);

        // Assert
        await Assert.ThrowsAsync<NearbyConnectException>(() => handshake);
    }

    [Fact]
    public async Task AwaitHandshake_WhenTimeoutElapses_DoesNotStrandThePendingEntry()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var timeout = TimeSpan.FromSeconds(5);
        var platform = Create.NearbyTransport(time, new NearbyOptions { ServiceId = "test-service" });
        var device = Create.Device("peer-1", "Alice");
        var tcs = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: CancellationToken.None);

        // Act
        var handshake = platform.AwaitHandshakeAsync(
            device,
            tcs,
            isInitiator: true,
            timeout,
            beforeAwait: _ => Task.CompletedTask,
            CancellationToken.None);

        time.Advance(timeout);
        await Assert.ThrowsAsync<NearbyConnectException>(() => handshake);

        // Assert
        // No pending handshake and no connection: the failed attempt left nothing behind.
        Assert.True(platform.IsIdle);
    }

    [Fact]
    public async Task AwaitHandshake_WhenCallbackResolvesFirst_ReturnsTheConnection()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var platform = Create.NearbyTransport(time, new NearbyOptions { ServiceId = "test-service" });
        var device = Create.Device("peer-1", "Alice");
        var expected = Create.Connection(device);
        var tcs = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: CancellationToken.None);

        // Act
        var handshake = platform.AwaitHandshakeAsync(
            device,
            tcs,
            isInitiator: true,
            TimeSpan.FromSeconds(5),
            beforeAwait: _ => Task.CompletedTask,
            CancellationToken.None);

        platform.ResolveConnectionTcs(device.Id, expected);

        // Assert
        Assert.Same(expected, await handshake);
    }

    [Fact]
    public async Task AwaitHandshake_CallerCancellation_ReportsCancellationNotTimeout()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var platform = Create.NearbyTransport(time, new NearbyOptions { ServiceId = "test-service" });
        var device = Create.Device("peer-1", "Alice");
        var tcs = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: CancellationToken.None);
        using var cts = new CancellationTokenSource();

        // Act
        var handshake = platform.AwaitHandshakeAsync(
            device,
            tcs,
            isInitiator: true,
            TimeSpan.FromSeconds(5),
            beforeAwait: _ => Task.CompletedTask,
            cts.Token);

        await cts.CancelAsync();

        // Assert
        var ex = await Assert.ThrowsAsync<TaskCanceledException>(() => handshake);
        Assert.IsNotAssignableFrom<NearbyConnectException>(ex);
    }
}
