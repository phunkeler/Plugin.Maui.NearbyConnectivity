using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Session")]
public sealed class InboundRequestTests
{
    [Fact]
    public async Task RequestArriving_ReportsRequestReceived_AndSurfacesDevice()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);

        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));

        // Act
        await connections.EmitRequestAsync(device, () => Create.Connection(device));
        await recorder.WaitForAsync("peer-1", 2);

        // Assert
        Assert.Equal(NearbyDeviceStatus.RequestReceived, session.StatusOf("peer-1"));
        Assert.Contains(device, session.Devices);

        // Added before Updated: a consumer must never see a status change for a device it has
        // not been told about.
        Assert.Equal(
            new[] { NearbyDeviceChangeAction.Added, NearbyDeviceChangeAction.Updated },
            recorder.For("peer-1").Select(c => c.Action).ToArray());
    }

    // AcceptAsync used to run transport-level delegates until the session swapped in wrappers, so
    // a request accepted before that swap reached the platform without touching session state.
    // Answering now routes through the device's gateway, so there is no unwired window.
    [Fact]
    public async Task AcceptAsync_RunsSessionBookkeeping_NotJustThePlatformAccept()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        await connections.EmitRequestAsync(device, () => Create.Connection(device));
        var request = await Take.RequestAsync(session, TestContext.Current.CancellationToken);

        // Act
        var connection = await request.AcceptAsync(TestContext.Current.CancellationToken);

        // Assert
        // The session claimed the request and moved the device, rather than the
        // platform accept running behind its back.
        Assert.Equal(NearbyDeviceStatus.Connected, session.StatusOf("peer-1"));
        Assert.False(request.IsPending);
        var tracked = session.ConnectionOf("peer-1");
        Assert.NotNull(tracked);
        Assert.Same(connection, tracked);
    }

    [Fact]
    public async Task AutoAccept_ConnectsWithoutEverReportingRequestReceived()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        session.Advertiser.SetConsent(NearbyConsentPolicy.AcceptAll);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));

        // Act
        await connections.EmitRequestAsync(device, () => Create.Connection(device));
        await recorder.WaitForAsync("peer-1", 2);

        // Assert
        // The documented contract of auto-accept: the state is skipped, not merely
        // unreported.
        Assert.Equal(NearbyDeviceStatus.Connected, session.StatusOf("peer-1"));
        // RequestReceived must not be observable when requests are auto-accepted.
        Assert.DoesNotContain(
            NearbyDeviceStatus.RequestReceived,
            recorder.StatusesFor("peer-1"));
    }

    [Fact]
    public async Task AutoAccept_WhenTheAcceptNeverSettles_DisposalCancelsIt()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        session.Advertiser.SetConsent(NearbyConsentPolicy.AcceptAll);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        var accept = connections.CaptureNextAcceptToken();
        await connections.EmitRequestThatOnlyCancellationEndsAsync(new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));

        // Act
        await session.DisposeAsync();

        // Assert
        // Auto-accept is started by a callback, so no caller's token reaches it. The
        // session passes its own disposal token instead, which is what ends an accept the
        // platform never settles. Nothing awaits auto-accept today, so this is the only
        // observable effect; it is also what keeps a future disposal drain from hanging here.
        await Wait.UntilAsync(() => accept.Task.IsCompleted);
        Assert.True(
            accept.Task.IsCanceled,
            "Disposal must cancel a pending auto-accept, not leave it awaiting forever.");
    }

    // An inbound request belongs to the advertising activity that received it, so stopping that
    // activity ends its handshake too, not only the requests still held for a decision.
    [Fact]
    public async Task AutoAccept_WhenAdvertisingStops_TheAcceptIsCancelled()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        session.Advertiser.SetConsent(NearbyConsentPolicy.AcceptAll);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        var accept = connections.CaptureNextAcceptToken();
        await connections.EmitRequestThatOnlyCancellationEndsAsync(new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        await Wait.UntilAsync(() => session.StatusOf("peer-1") == NearbyDeviceStatus.Connecting);

        // Act
        await session.Advertiser.StopAsync();

        // Assert
        await Wait.UntilAsync(() => accept.Task.IsCompleted);
        Assert.True(accept.Task.IsCanceled);
        await Wait.UntilAsync(() => session.StatusOf("peer-1") is null);
    }

    // The auto-accept handshake is bounded by OfferWindowPolicy's own value, not by the offer
    // window the peer declared: a policy that answers for us removes the person deciding, so what
    // is left is radio work. A peer declaring the 30-minute maximum still gets 30 seconds here.
    [Fact]
    public async Task AutoAccept_BoundsTheHandshake_IndependentlyOfTheDeclaredWindow()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var connections = new FakeNearby();
        var session = Create.Session(connections, timeProvider: time);
        session.Advertiser.SetConsent(NearbyConsentPolicy.AcceptAll);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        var accept = connections.CaptureNextAcceptToken();
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));

        // A deadline far beyond the auto-accept bound: the peer declared the longest window it can.
        await connections.EmitRequestThatOnlyCancellationEndsAsync(
            device,
            time.GetUtcNow() + TimeSpan.FromMinutes(30));

        // Act
        time.Advance(TimeSpan.FromSeconds(31));

        // Assert
        await Wait.UntilAsync(() => accept.Task.IsCompleted);
        Assert.True(
            accept.Task.IsCanceled,
            "The auto-accept handshake must be abandoned at its own bound, not at the declared offer window.");

        // The abandoned handshake returns the device to Visible, so the row is connectable again.
        await Wait.UntilAsync(() => session.StatusOf("peer-1") == NearbyDeviceStatus.Visible);
    }

    [Fact]
    public async Task AutoAccept_NeverReportsRequestReceived()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        session.Advertiser.SetConsent(NearbyConsentPolicy.AcceptAll);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await using var recorder = new ChangeRecorder(session);
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));

        // Act
        await connections.EmitRequestAsync(device, () => Create.Connection(device));
        await Wait.UntilAsync(() => recorder.StatusesFor("peer-1").Contains(NearbyDeviceStatus.Connected));

        // Assert
        Assert.DoesNotContain(NearbyDeviceStatus.RequestReceived, recorder.StatusesFor("peer-1"));
        Assert.Null(session.Current("peer-1")?.Request);
    }

    [Fact]
    public async Task AutoAccept_WhenAcceptFails_ResetsDeviceToVisible()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        session.Advertiser.SetConsent(NearbyConsentPolicy.AcceptAll);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));

        // Act
        await connections.EmitRequestAsync(
            device,
            () => throw new NearbyException("Handshake failed."));

        // A failed auto-accept must not strand the row on Connecting, and must not escape into
        // the advertise pump and stop advertising.

        // Assert
        Assert.Equal(NearbyDeviceStatus.Visible, device.Status);
        Assert.True(session.Advertiser.IsActive);
    }

    [Theory]
    [InlineData(86400, false, TestDisplayName = "a distant deadline is cut to the assumed window")]
    [InlineData(10, true, TestDisplayName = "a nearer deadline tightens the bound further")]
    public async Task AutoAccept_IsBoundedByMinOfRemainingWindowAndAssumedWindow(
        int declaredWindowSeconds, bool deadlineIsNearer)
    {
        // An unconsented accept honors a stranger's declared window only up to the assumed
        // window: the handshake wait ends at min(remaining window, OfferWindowPolicy.s_default).
        var bound = deadlineIsNearer
            ? TimeSpan.FromSeconds(declaredWindowSeconds)
            : OfferWindowPolicy.s_default;

        // Arrange
        var time = new FakeTimeProvider();
        var connections = new FakeNearby();
        var session = Create.Session(connections, timeProvider: time);
        session.Advertiser.SetConsent(NearbyConsentPolicy.AcceptAll);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        connections.CaptureNextAcceptToken();
        await connections.EmitRequestThatOnlyCancellationEndsAsync(
            device,
            deadline: time.GetUtcNow() + TimeSpan.FromSeconds(declaredWindowSeconds));
        await recorder.WaitForAsync("peer-1", 2);

        // Act
        time.Advance(bound);
        await recorder.WaitForAsync("peer-1", 3);

        // Assert
        // Advertising only: nothing vouches for the device once the request is gone.
        Assert.Null(session.StatusOf("peer-1"));
    }

    [Fact]
    public async Task RejectedRequest_ReportsItsReasonOnTheChangeStream()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        await connections.EmitRequestAsync(device, () => Create.Connection(device));
        await using var recorder = new ChangeRecorder(session);

        // Act
        var request = await Take.RequestAsync(session, TestContext.Current.CancellationToken);
        await request.DeclineAsync(TestContext.Current.CancellationToken);
        await Wait.UntilAsync(() => recorder.For("peer-1").Count > 0);

        // Assert
        Assert.Contains(
            recorder.For("peer-1"),
            c => c.Reason == NearbyEndReason.Declined);
    }

    [Fact]
    public async Task StopAsync_CompletesDisconnectedWithSessionStopped()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        connections.ConnectResult = Create.Connection(device);
        var connection = await device.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        await session.StopAllAsync();
        var reason = await connection.Closed;

        // Assert
        Assert.Equal(NearbyEndReason.SessionStopped, reason);
    }

    [Fact]
    public async Task StopAsync_EndsDisconnectWatchers_WithoutWaitingOutTheJoinBound()
    {
        // The disconnect watcher runs on the session stop token, not on connection disposal
        // alone. On a clock that never advances, a watcher that ignored the token would leave
        // the task set live and this stop would never return.

        // Arrange
        // A connection the platform does not track, so disposal cannot reach it and
        // complete its Disconnected. Only the stop token can end its watcher.
        var time = new FakeTimeProvider();
        var connections = new FakeNearby();
        var session = Create.Session(connections, timeProvider: time);
        var device = await Create.DiscoveredAsync(session, connections, new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default)));
        connections.ConnectResult = Create.Connection(device);
        var connection = await device.ConnectAsync(TestContext.Current.CancellationToken);
        connections.ForgetConnection(device.Id);

        // Act
        await session.StopAllAsync();

        // Assert
        // The stop returned on a clock that never advanced, so the join went quiet
        // rather than timing out, and the watcher left no device behind.
        Assert.False(connection.Closed.IsCompleted);
        Assert.Empty(session.Devices);
    }

    [Fact]
    public async Task StopAsync_CancelsAPendingAutoAccept_SoItCannotWriteIntoTheNextSession()
    {
        // Stop promises a return to the initial state. An auto-accept that survived a stop
        // could resolve later and resurrect a registry row (section 3, decided item 1).

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        session.Advertiser.SetConsent(NearbyConsentPolicy.AcceptAll);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        var accept = connections.CaptureNextAcceptToken();
        await connections.EmitRequestThatOnlyCancellationEndsAsync(device);

        // Act
        // Stop, then try to complete the old accept as a straggler would.
        await session.StopAllAsync();
        var resurrected = accept.TrySetResult(Create.Connection(device));
        await Task.Yield();

        // Assert
        // The stop token already settled the accept, so the straggler cannot land.
        Assert.False(resurrected);
        Assert.Empty(session.Devices);
    }

    [Fact]
    public async Task UnansweredRequest_WhenTimeoutElapses_RejectsAndDropsDevice()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var window = TimeSpan.FromSeconds(30);
        var connections = new FakeNearby();
        var session = Create.Session(connections, new NearbyOptions(), time);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        var rejected = false;

        await connections.EmitRequestAsync(
            device,
            () => Create.Connection(device),
            onReject: () => rejected = true,
            deadline: time.GetUtcNow() + window);
        await recorder.WaitForAsync("peer-1", 2);

        // Act
        time.Advance(window);
        await recorder.WaitForAsync("peer-1", 3);

        // Assert
        Assert.True(rejected, "An expired request must be rejected, to release the platform's handle.");
        // Advertising only: the session never discovered this device, so once the request is
        // gone nothing says it is still in range, and a phantom Visible row is worse than none.
        Assert.Null(session.StatusOf("peer-1"));
    }

    [Fact]
    public async Task UnansweredRequest_WhenDiscovering_ReturnsDeviceToVisible()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var window = TimeSpan.FromSeconds(30);
        var connections = new FakeNearby();
        var session = Create.Session(connections, new NearbyOptions(), time);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));

        await connections.EmitRequestAsync(
            device,
            () => Create.Connection(device),
            deadline: time.GetUtcNow() + window);
        await recorder.WaitForAsync("peer-1", 2);

        // Act
        time.Advance(window);
        await recorder.WaitForAsync("peer-1", 3);

        // Assert
        // Discovery vouches for the device, so it stays listed as an ordinary Visible peer.
        Assert.Equal(NearbyDeviceStatus.Visible, session.StatusOf("peer-1"));
    }

    [Fact]
    public async Task ExpiredRequest_CannotBeAccepted()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var window = TimeSpan.FromSeconds(30);
        var connections = new FakeNearby();
        var session = Create.Session(connections, new NearbyOptions(), time);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        await connections.EmitRequestAsync(device, () => Create.Connection(device), deadline: time.GetUtcNow() + window);
        await recorder.WaitForAsync("peer-1", 2);

        var request = await Take.RequestAsync(session, TestContext.Current.CancellationToken);

        // Act
        // Past the offer's deadline: the dead tail. An accept that arrives now loses the
        // claim race and throws, exactly as any late accept does.
        time.Advance(window);
        await recorder.WaitForAsync("peer-1", 3);

        // Assert
        await request.Expired.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<NearbyConnectException>(
            () => request.AcceptAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PendingRequest_PublishesExpiryForAConsumerCountdown()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var window = TimeSpan.FromSeconds(30);
        var connections = new FakeNearby();
        var session = Create.Session(connections, new NearbyOptions(), time);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        var expected = time.GetUtcNow() + window;

        // Act
        await connections.EmitRequestAsync(device, () => Create.Connection(device), deadline: expected);
        await recorder.WaitForAsync("peer-1", 2);

        // Assert
        // The deadline reached through the device is the offer's own, not a local
        // guess. It lives on the request now: one owner for one fact.
        Assert.Equal(expected, session.Current("peer-1")?.Request?.ExpiresAt);
    }

    [Fact]
    public async Task AcceptedRequest_ClearsTheExpiry()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var connections = new FakeNearby();
        var session = Create.Session(connections, new NearbyOptions(), time);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        await connections.EmitRequestAsync(device, () => Create.Connection(device), deadline: time.GetUtcNow() + TimeSpan.FromSeconds(30));
        await recorder.WaitForAsync("peer-1", 2);
        var request = await Take.RequestAsync(session, TestContext.Current.CancellationToken);

        // Act
        await request.AcceptAsync(TestContext.Current.CancellationToken);

        // Assert
        // Answered, so the device no longer resolves a request at all.
        Assert.Null(session.Current("peer-1")?.Request);
    }

    [Fact]
    public async Task AcceptedRequest_IsNotRejectedWhenTheOriginalTimeoutWouldHaveElapsed()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var window = TimeSpan.FromSeconds(30);
        var connections = new FakeNearby();
        var session = Create.Session(connections, new NearbyOptions(), time);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        var rejected = false;

        await connections.EmitRequestAsync(
            device,
            () => Create.Connection(device),
            onReject: () => rejected = true,
            deadline: time.GetUtcNow() + window);
        await recorder.WaitForAsync("peer-1", 2);
        var request = await Take.RequestAsync(session, TestContext.Current.CancellationToken);
        await request.AcceptAsync(TestContext.Current.CancellationToken);

        // Act
        time.Advance(window * 2);

        // Assert
        Assert.False(rejected, "A disarmed countdown must not reject an accepted request.");
        Assert.Equal(NearbyDeviceStatus.Connected, session.StatusOf("peer-1"));
    }

    [Fact]
    public async Task DistantDeadline_IsClampedToTheTrustBound()
    {
        // The deadline is remote-declared, untrusted input: however large the declared window,
        // the request expires at the local trust bound. (There is no infinite case — every
        // tracked request has a finite deadline.)

        // Arrange
        var time = new FakeTimeProvider();
        var connections = new FakeNearby();
        var session = Create.Session(connections, new NearbyOptions(), time);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        await using var recorder = new ChangeRecorder(session);
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        var rejected = false;

        await connections.EmitRequestAsync(
            device,
            () => Create.Connection(device),
            onReject: () => rejected = true,
            deadline: DateTimeOffset.MaxValue);
        await recorder.WaitForAsync("peer-1", 2);

        // Act
        // Just short of the bound the request is still outstanding; past it, it expires.
        time.Advance(OfferWindowPolicy.s_max - TimeSpan.FromSeconds(1));
        Assert.Equal(NearbyDeviceStatus.RequestReceived, session.StatusOf("peer-1"));
        time.Advance(TimeSpan.FromSeconds(1));
        await recorder.WaitForAsync("peer-1", 3);

        // Assert
        Assert.True(rejected);
        Assert.Null(session.StatusOf("peer-1"));
    }

    [Fact]
    public async Task AcceptAsync_ConnectsAndReportsConnected()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        var connection = Create.Connection(device);
        await connections.EmitRequestAsync(device, () => connection);

        await using var recorder = new ChangeRecorder(session);
        var request = await Take.RequestAsync(session, TestContext.Current.CancellationToken);

        // Act
        var result = await request.AcceptAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(connection, result);
        Assert.Equal(NearbyDeviceStatus.Connected, session.StatusOf("peer-1"));
        var lookedUp = session.ConnectionOf(device.Id);
        Assert.NotNull(lookedUp);
        Assert.Same(connection, lookedUp);

        // Wait for the Connected change specifically. A plain count reaches 1 as soon as any
        // change lands, which may be an earlier transition in the handshake.
        await Wait.UntilAsync(
            () => recorder.StatusesFor("peer-1").Contains(NearbyDeviceStatus.Connected));

        Assert.Contains(NearbyDeviceStatus.Connected, recorder.StatusesFor("peer-1"));
    }

    [Fact]
    public async Task RejectAsync_DoesNotConnect()
    {
        // Security-relevant: rejecting must never produce a connection.

        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        await connections.EmitRequestAsync(device, () => Create.Connection(device));

        await using var recorder = new ChangeRecorder(session);
        var request = await Take.RequestAsync(session, TestContext.Current.CancellationToken);

        // Act
        await request.DeclineAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(NearbyDeviceStatus.Connected, recorder.StatusesFor("peer-1"));
        Assert.Null(session.ConnectionOf(device.Id));
        Assert.Null(session.StatusOf("peer-1"));
    }

    [Fact]
    public async Task AcceptAsync_AfterReject_Throws()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        // Act
        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        await connections.EmitRequestAsync(device, () => Create.Connection(device));
        var request = await Take.RequestAsync(session, TestContext.Current.CancellationToken);
        await request.DeclineAsync(TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<NearbyConnectException>(
            () => request.AcceptAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AcceptAsync_WhenPlatformFails_DropsDevice()
    {
        // Arrange
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        await connections.EmitRequestAsync(
            device,
            () => throw new InvalidOperationException("handshake failed"));
        var request = await Take.RequestAsync(session, TestContext.Current.CancellationToken);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => request.AcceptAsync(TestContext.Current.CancellationToken));

        // Assert
        // A failed handshake must not strand the row on Connecting; with no discovery running
        // the device goes rather than lingering as Visible.
        Assert.Null(session.StatusOf("peer-1"));
    }

    // A second offer from a device whose first is still outstanding replaces it in the registry,
    // but the device's status did not change, so nothing republished the snapshot: a consumer
    // reading Request off it kept the old deadline, and the replaced request never settled —
    // IsPending stayed true and Expired never completed. Seen in NearbyChat: a discoverer that
    // gave up and retried before the advertiser's own expiry fired left the advertiser's card
    // draining the stale window and then leaving, while the live offer was never shown.
    [Fact]
    public async Task SecondRequestFromSameDevice_ReplacesTheFirst_AndRepublishesTheDevice()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var connections = new FakeNearby();
        var session = Create.Session(connections);
        await session.Advertiser.StartAsync(token);

        await using var recorder = new ChangeRecorder(session);

        var device = new NearbyDevice("peer-1", NearbyPresence.FromRemote("Alice", default));
        var firstRejected = false;

        await connections.EmitRequestAsync(
            device,
            () => Create.Connection(device),
            onReject: () => firstRejected = true,
            deadline: DateTimeOffset.UtcNow.AddSeconds(30));
        var first = await Take.RequestAsync(session, token);
        await recorder.WaitForAsync("peer-1", 2);

        // Act
        await connections.EmitRequestAsync(
            device,
            () => Create.Connection(device),
            deadline: DateTimeOffset.UtcNow.AddSeconds(60));
        await recorder.WaitForAsync("peer-1", 3);

        // Assert
        // The replaced request is settled without answering the platform: the platform
        // already swapped the offer, so a reject now would answer the new one.
        Assert.False(first.IsPending);
        Assert.True(first.Expired.IsCompleted);
        Assert.False(firstRejected);

        Assert.Equal(NearbyDeviceStatus.RequestReceived, session.StatusOf("peer-1"));
        var current = session.Current("peer-1");
        Assert.NotNull(current);
        Assert.NotNull(current.Request);
        Assert.NotSame(first, current.Request);
        Assert.True(current.Request.IsPending);

        // The snapshot went round again, so a row bound to it re-reads the new deadline.
        Assert.Equal(
            new[] { NearbyDeviceChangeAction.Added, NearbyDeviceChangeAction.Updated, NearbyDeviceChangeAction.Updated },
            recorder.For("peer-1").Select(c => c.Action).ToArray());
    }
}
