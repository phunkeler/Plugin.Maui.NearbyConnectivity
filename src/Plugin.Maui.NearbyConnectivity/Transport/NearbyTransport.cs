using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Plugin.Maui.NearbyConnectivity;

sealed class NearbyTransport : INearbyTransport
{
    static readonly TimeSpan s_drainTimeout = TimeSpan.FromSeconds(5);

    readonly Lock _presenceLock = new();

    // A presence set while something was running, waiting for the span to end. Null when nothing is
    // pending.
    NearbyPresence? _pendingPresence;
    int _disposed;

    // One of the three terms of IsIdle.
    int _runs;

    internal Channel<NearbyConnectionRequest> _advertiseChannel;
    internal Channel<NearbyDeviceEvent> _discoverChannel;

    internal NearbyTransport(
        TimeProvider timeProvider,
        NearbyOptions options,
        ILogger logger,
        PeerLookup peerLookup,
        Func<NearbyTransport, IPlatformAdapter> createAdapter)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(peerLookup);
        ArgumentNullException.ThrowIfNull(createAdapter);

        TimeProvider = timeProvider;
        Options = options;
        Logger = logger;
        PeerLookup = peerLookup;
        Presence = options.Presence;

        _advertiseChannel = Channel.CreateUnbounded<NearbyConnectionRequest>();
        _discoverChannel = Channel.CreateUnbounded<NearbyDeviceEvent>();
        Handshakes = new HandshakeLedger(() => Volatile.Read(ref _disposed) != 0, ReleaseIfIdle);
        Connections = new ConnectionTable(logger, timeProvider, s_drainTimeout);
        Staging = new InboundStaging(logger);
        Adapter = createAdapter(this);
        Adapter.SweepStaging();
    }

    // Owned here rather than read from the options snapshot at each site: the platforms want it at
    // different moments — iOS once per local peer, Android once per advertise and again per connect
    // request. One owner is what lets every read inside a span see the same value.
    public NearbyPresence Presence { get; private set; }

    internal PeerLookup PeerLookup { get; }
    internal TimeProvider TimeProvider { get; }
    internal ILogger Logger { get; }
    internal IPlatformAdapter Adapter { get; }
    internal NearbyOptions Options { get; }
    internal HandshakeLedger Handshakes { get; }
    internal ConnectionTable Connections { get; }
    internal InboundStaging Staging { get; }

    // The iOS start-failure grace window awaits this.
    internal Task AdvertiseChannelCompletion => _advertiseChannel.Reader.Completion;

    internal Task DiscoverChannelCompletion => _discoverChannel.Reader.Completion;

    public void SetPresence(NearbyPresence presence)
    {
        ArgumentNullException.ThrowIfNull(presence);

        lock (_presenceLock)
        {
            if (IsIdle)
            {
                Presence = presence;
                _pendingPresence = null;

                return;
            }

            _pendingPresence = presence;
        }
    }

    // The edge is what makes the two platforms agree. iOS cannot rename a live MCPeerID at all,
    // and Android would otherwise advertise under one presence and send a connect request under
    // another within the same run. Moving the effective value only here gives both one presence
    // per span.
    void ApplyPendingPresence()
    {
        lock (_presenceLock)
        {
            if (_pendingPresence is { } pending)
            {
                Presence = pending;
                _pendingPresence = null;
            }
        }
    }

    public IAsyncEnumerable<NearbyConnectionRequest> AdvertiseAsync(
        TaskCompletionSource started,
        CancellationToken cancellationToken = default)
        => Step<NearbyConnectionRequest>(
            channel => Interlocked.Exchange(ref _advertiseChannel, channel),
            Adapter.StartAdvertisingAsync,
            Adapter.StopAdvertising,
            started,
            cancellationToken);

    public IAsyncEnumerable<NearbyDeviceEvent> DiscoverAsync(
        TaskCompletionSource started,
        CancellationToken cancellationToken = default)
        => Step<NearbyDeviceEvent>(
            channel => Interlocked.Exchange(ref _discoverChannel, channel),
            Adapter.StartDiscoveryAsync,
            Adapter.StopDiscovering,
            started,
            cancellationToken);

    public async Task<NearbyConnection> ConnectAsync(NearbyDevice device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        cancellationToken.ThrowIfCancellationRequested();

        var window = OfferWindowPolicy.Clamp(Options.OfferWindow);
        var deadline = TimeProvider.GetUtcNow() + window;
        var tcs = Handshakes.Register(device.Id, out var joined, deadline, attributedTo: cancellationToken);

        if (joined)
        {
            try
            {
                return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                throw TranslateForeignCancellation(ex, device.Id, cancellationToken);
            }
        }

        return await AwaitHandshakeAsync(
            device,
            tcs,
            isInitiator: true,
            window,
            beforeAwait: token => Adapter.InitiateConnectAsync(device, token),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<NearbyAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        => Adapter.CheckAvailabilityAsync(cancellationToken);

    // Relayed straight from the adapter: the transport holds no availability state of its own, so
    // there is nothing here to keep in step with the signal.
    public event Action? AvailabilityChanged
    {
        add => Adapter.AvailabilityChanged += value;
        remove => Adapter.AvailabilityChanged -= value;
    }

    public event Action? Suspended
    {
        add => Adapter.Suspended += value;
        remove => Adapter.Suspended -= value;
    }

    public void AttachDeviceGateway(INearbyDeviceGateway gateway) => PeerLookup.Gateway = gateway;

    public bool TryGetConnection(string deviceId, [NotNullWhen(true)] out NearbyConnection? connection)
    {
        ArgumentNullException.ThrowIfNull(deviceId);

        return Connections.TryGet(deviceId, out connection);
    }

    public NearbyConnection[] SnapshotConnections() => Connections.Snapshot();

    public bool TryJoinHandshake(string deviceId, [NotNullWhen(true)] out Task<NearbyConnection>? handshake)
        => Handshakes.TryJoin(deviceId, out handshake);

    public bool TryGetConnectDeadline(string deviceId, [NotNullWhen(true)] out DateTimeOffset? deadline)
        => Handshakes.TryGetDeadline(deviceId, out deadline);

    // The one definition of the end of an idle span. All three terms are required: a pending
    // handshake has no entry in the connection table yet and still needs whatever the platform
    // holds, and a run with no peers is still a reason to hold it.
    //
    // The count is of ATTEMPTED runs, taken before the platform start is called and given back if
    // that start fails: iOS mints the local peer and builds the advertiser or browser on it before
    // its start returns, so a release landing during the start's grace window would retire the peer
    // under a live SDK object. A start that failed or was canceled gives the count back before
    // re-testing the span, so it never leaves the span open.
    internal bool IsIdle
        => Volatile.Read(ref _runs) == 0 && Connections.IsEmpty && Handshakes.IsEmpty;

    // The one chokepoint. Called from every path that can drop the last of the three terms: a run
    // ending in Step, a connection released, and every removal from the handshake ledger.
    internal void ReleaseIfIdle()
    {
        if (!IsIdle)
        {
            return;
        }

        Adapter.ReleaseIdleState();

        // Peer mappings are span state too: a handle recorded while browsing is meaningless once
        // the browse is over, and on iOS only removal disposes the MCPeerID behind it.
        PeerLookup.Clear();

        // After the platform released what it held: a presence set mid-span takes effect at the
        // same edge, so the next start mints its identity from the new value.
        ApplyPendingPresence();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Adapter.StopAdvertising();
        Adapter.StopDiscovering();

        _advertiseChannel.Writer.TryComplete();
        _discoverChannel.Writer.TryComplete();

        foreach (var pending in Handshakes.ClaimAll())
        {
            pending.Tcs.TrySetCanceled(pending.AttributedTo);
        }

        foreach (var connection in Connections.ClaimAll())
        {
            try
            {
                await connection.CloseForAsync(NearbyEndReason.SessionStopped).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NearbyLog.ConnectionDisposeFailed(Logger, connection.RemoteDevice.Id, ex);
            }
        }

        await Connections.DrainAllAsync().ConfigureAwait(false);

        Adapter.SweepStaging();
        Adapter.Dispose();
        PeerLookup.Clear();
    }

    // The duration is a parameter rather than a role-based options lookup so an accept is bounded
    // by the window the initiator actually declared, not a local guess.
    internal async Task<NearbyConnection> AwaitHandshakeAsync(
        NearbyDevice device,
        TaskCompletionSource<NearbyConnection> tcs,
        bool isInitiator,
        TimeSpan window,
        Func<CancellationToken, Task> beforeAwait,
        CancellationToken cancellationToken)
    {
        using var deadlineCts = new CancellationTokenSource(window, TimeProvider);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, deadlineCts.Token);

        try
        {
            await beforeAwait(linkedCts.Token).ConfigureAwait(false);

            return await tcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (deadlineCts.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
        {
            Handshakes.Drop(device.Id);
            await Adapter.AbandonConnectAsync(device).ConfigureAwait(false);
            var seconds = window.TotalSeconds;

            throw new NearbyConnectException(
                NearbyConnectFailure.TimedOut,
                isInitiator
                    ? $"The connection request to device '{device.Id}' was not answered within {seconds:0.#}s."
                    : $"The request from device '{device.Id}' expired before the handshake completed — {seconds:0.#}s remained when accepting it.");
        }
        catch (OperationCanceledException)
        {
            await AbandonAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (!isInitiator)
        {
            await AbandonAsync().ConfigureAwait(false);
            throw ex is NearbyConnectException connectException
                ? connectException
                : new NearbyConnectException(NearbyConnectFailure.Failed, ex.Message, ex);
        }
        catch
        {
            await AbandonAsync().ConfigureAwait(false);
            throw;
        }

        async Task AbandonAsync()
        {
            Handshakes.Drop(device.Id);

            try
            {
                await Adapter.AbandonConnectAsync(device).ConfigureAwait(false);
            }
            catch (Exception abandonEx)
            {
                NearbyLog.EventWriteFailed(Logger, nameof(IPlatformAdapter.AbandonConnectAsync), device.Id, abandonEx);
            }
        }
    }

    internal DateTimeOffset ComputeOfferDeadline(string deviceId, TimeSpan declaredWindow)
    {
        var window = OfferWindowPolicy.Clamp(declaredWindow);

        if (window != declaredWindow)
        {
            NearbyLog.OfferWindowClamped(
                Logger,
                deviceId,
                declaredWindow.TotalSeconds,
                window.TotalSeconds);
        }

        return TimeProvider.GetUtcNow() + window;
    }

    internal TimeSpan TransferInactivityTimeout
        => Options.FileTransferInactivityTimeout ?? Timeout.InfiniteTimeSpan;

    // For SDK callbacks, which cannot await. See ReleaseConnectionAsync for the forgetPeer rule.
    internal void ReleaseConnectionFromCallback(string deviceId, bool forgetPeer = false)
    {
        var release = ReleaseConnectionAsync(deviceId, forgetPeer);

        if (release.IsCompletedSuccessfully)
        {
            return;
        }

        _ = Await(release, deviceId);

        async Task Await(ValueTask pending, string id)
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NearbyLog.EventWriteFailed(Logger, nameof(ReleaseConnectionFromCallback), id, ex);
            }
        }
    }

    async IAsyncEnumerable<T> Step<T>(
        Action<Channel<T>> publish,
        Func<CancellationToken, Task> start,
        Action stop,
        TaskCompletionSource started,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<T>();
        publish(channel);

        Interlocked.Increment(ref _runs);

        try
        {
            await start(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stop();
            channel.Writer.TryComplete();

            Interlocked.Decrement(ref _runs);
            ReleaseIfIdle();

            if (ex is OperationCanceledException canceled && cancellationToken.IsCancellationRequested)
            {
                started.TrySetCanceled(canceled.CancellationToken);
            }
            else
            {
                started.TrySetException(ex);
            }

            throw;
        }

        started.TrySetResult();

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            stop();
            channel.Writer.TryComplete();
            Interlocked.Decrement(ref _runs);
            ReleaseIfIdle();
        }
    }

    internal NearbyDevice? ForgetPeer(string deviceId)
    {
        if (PeerLookup.Remove(deviceId) is not { } device)
        {
            return null;
        }

        NearbyLog.DeviceLost(Logger, deviceId);
        WriteDeviceEvent(device, found: false, nameof(ForgetPeer));

        return device;
    }

    internal void OnDeviceFound(NearbyDevice device)
    {
        NearbyLog.DeviceFound(Logger, device.Id);
        WriteDeviceEvent(device, found: true, nameof(OnDeviceFound));
    }

    internal void OnDeviceLost(string deviceId)
    {
        if (Connections.IsConnected(deviceId))
        {
            if (PeerLookup.TryGetDevice(deviceId, out var existingDevice))
            {
                NearbyLog.ConnectedDeviceStoppedAdvertising(Logger, existingDevice.Id);
            }

            return;
        }

        ForgetPeer(deviceId);
    }

    void WriteDeviceEvent(NearbyDevice device, bool found, string writer)
    {
        try
        {
            var channel = _discoverChannel;

            if (!channel.Writer.TryWrite(new NearbyDeviceEvent(device, found)))
            {
                NearbyLog.EventStreamCompleted(Logger, writer, device.Id);
            }
        }
        catch (Exception ex)
        {
            NearbyLog.EventWriteFailed(Logger, writer, device.Id, ex);
        }
    }

    // Faults the current advertise channel so the grace window or the pump observes it. False when
    // the fault was dropped — log that.
    internal bool TryFaultAdvertiseChannel(Exception exception)
        => _advertiseChannel.Writer.TryComplete(exception);

    internal bool TryFaultDiscoverChannel(Exception exception)
        => _discoverChannel.Writer.TryComplete(exception);

    // The inbound offer both platforms surface: one deadline, registration deferred to the accept,
    // and a reject that drops the registration before answering the platform.
    //
    // Registering inside accept rather than on arrival is what lets the handshake be attributed to
    // the accepting caller's token from the start. A registration made when the request arrives has
    // no caller token to name, so a cancellation raised on it reaches the awaiter as an
    // OperationCanceledException it cannot correlate to its own operation.
    //
    // sendAccept and sendReject are the platform's answer to the offer and nothing else. The
    // deadline, the ledger entry and the failure unwind are the same on both platforms and live
    // here; what differs is only how the answer is delivered.
    internal NearbyConnectionRequest CreateInboundRequest(
        NearbyDevice device,
        TimeSpan declaredWindow,
        Func<Task> sendAccept,
        Func<Task> sendReject,
        Action? onAcceptFailed = null)
    {
        var deviceId = device.Id;
        var deadline = ComputeOfferDeadline(deviceId, declaredWindow);

        return new NearbyConnectionRequest(
            device,
            deadline,
            accept: async ct =>
            {
                var tcs = Handshakes.Register(deviceId, out _, deadline: null, attributedTo: ct);

                try
                {
                    return await AwaitHandshakeAsync(
                        device,
                        tcs,
                        isInitiator: false,
                        OfferWindowPolicy.Clamp(deadline - TimeProvider.GetUtcNow()),
                        beforeAwait: _ => sendAccept(),
                        ct).ConfigureAwait(false);
                }
                catch
                {
                    onAcceptFailed?.Invoke();
                    throw;
                }
            },
            reject: ct =>
            {
                Handshakes.Drop(deviceId);
                return sendReject();
            },
            TimeProvider);
    }

    internal void WriteConnectionRequest(NearbyConnectionRequest request)
    {
        try
        {
            var channel = _advertiseChannel;
            var written = channel.Writer.TryWrite(request);

            if (!written)
            {
                NearbyLog.EventStreamCompleted(Logger, nameof(WriteConnectionRequest), request.RemoteDevice.Id);

                _ = Connections.WorkQueue.Enqueue(
                    $"reject:{request.RemoteDevice.Id}",
                    () => RejectUnroutableRequestAsync(request));
            }
        }
        catch (Exception ex)
        {
            NearbyLog.EventWriteFailed(Logger, nameof(WriteConnectionRequest), request.RemoteDevice.Id, ex);
        }
    }

    // Answers a request the session will never see, because the advertise channel was already
    // completed when it arrived. The platform half is called directly rather than through
    // DeclineAsync: the gateway answers only requests the session published, and claims one before
    // rejecting it. This request was never tracked, so that claim would fail and the platform would
    // be left holding an offer nobody answers.
    async Task RejectUnroutableRequestAsync(NearbyConnectionRequest request)
    {
        request.MarkExpired();

        await request.TryRejectAsync(
            ex => NearbyLog.EventWriteFailed(Logger, nameof(WriteConnectionRequest), request.RemoteDevice.Id, ex),
            CancellationToken.None).ConfigureAwait(false);
    }

    // A joined handshake is one shared source with several awaiters, so a cancellation raised on it
    // is attributed to whichever token the owner registered, which is not this caller's. Returned
    // unchanged when the caller's own token is what fired; otherwise the handshake ended for a
    // reason outside this call, which is a connect failure rather than this caller's cancellation,
    // and reporting it as cancellation would tell the caller it had canceled something it had not.
    static Exception TranslateForeignCancellation(
        OperationCanceledException ex,
        string deviceId,
        CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested
            ? ex
            : new NearbyConnectException(
                NearbyConnectFailure.Failed,
                $"The handshake with device '{deviceId}' was abandoned before it completed.",
                ex);

    // The shared connection assembly, written once. Both platforms' terminal success callbacks
    // route through this.
    // authenticationCode is null on a platform that has not derived one yet; iOS supplies it once
    // the certificate exchange lands.
    internal void CompleteHandshake(
        NearbyDevice device,
        IPlatformConnection platform,
        string? authenticationCode = null)
    {
        var receiveChannel = Channel.CreateUnbounded<NearbyPayload>(new UnboundedChannelOptions { SingleReader = true });

        var connection = new NearbyConnection(device, receiveChannel, platform, authenticationCode);

        ResolveConnectionTcs(device.Id, connection);
    }

    internal void ResolveConnectionTcs(string deviceId, NearbyConnection connection)
    {
        try
        {
            Connections.Add(deviceId, connection);

            if (Handshakes.TryClaim(deviceId, out var entry))
            {
                entry.Tcs.TrySetResult(connection);
            }
            else
            {
                Connections.Withdraw(deviceId);
                _ = CloseOrphanAsync(connection);
            }
        }
        catch (Exception ex)
        {
            NearbyLog.EventWriteFailed(Logger, nameof(ResolveConnectionTcs), deviceId, ex);
        }
    }

    // A link whose handshake nobody awaits any more. Closing it is what tells the peer, which
    // otherwise holds an open connection whose sends go nowhere.
    async Task CloseOrphanAsync(NearbyConnection orphan)
    {
        try
        {
            await orphan.CloseForAsync(NearbyEndReason.ClosedLocally).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NearbyLog.ConnectionDisposeFailed(Logger, orphan.RemoteDevice.Id, ex);
        }
    }

    // Anything that is not already a NearbyConnectException — including whatever the platform SDK
    // threw — is wrapped as NearbyConnectFailure.Failed, because ConnectAsync documents that type
    // and tells the caller to branch on Reason. A caller must never have to catch the SDK's own
    // exception type to learn a connect failed.
    internal void FaultConnectionTcs(string deviceId, Exception ex)
    {
        try
        {
            if (Handshakes.TryClaim(deviceId, out var entry))
            {
                entry.Tcs.TrySetException(ex as NearbyConnectException
                    ?? new NearbyConnectException(NearbyConnectFailure.Failed, ex.Message, ex));

                ReleaseIfIdle();
            }
        }
        catch (Exception innerEx)
        {
            NearbyLog.EventWriteFailed(Logger, nameof(FaultConnectionTcs), deviceId, innerEx);
        }
    }

    // Releasing a connection and losing a peer are different events, and only the caller knows
    // which happened. A remote drop, a failed handshake or an abandoned one means the peer is gone:
    // leaving its endpoint mapping behind grows the platform's peer tables for the life of the
    // process, and on iOS strands an MCPeerID that only removal disposes. A local close or a peer's
    // own Disconnect frame means the opposite — the device is still there and still reconnectable,
    // so forgetting it would take a visible device off the list.
    //
    // Removal runs AFTER the platform's release: that release frees payload and observer state
    // only, but forgetting a peer disposes its iOS handle, so nothing needing the handle may run
    // after this point.
    internal async ValueTask ReleaseConnectionAsync(string deviceId, bool forgetPeer = false)
    {
        await Connections.ReleaseAsync(deviceId, Adapter.ReleaseConnection).ConfigureAwait(false);

        if (forgetPeer)
        {
            ForgetPeer(deviceId);
        }

        // After the peer is forgotten, not before: forgetting disposes the iOS handle, and the
        // release below disposes the session that handle was built on. Both are span state, and
        // this can be the last connection the span was being held for.
        ReleaseIfIdle();
    }
}
