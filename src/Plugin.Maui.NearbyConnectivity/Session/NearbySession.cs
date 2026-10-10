using Microsoft.Extensions.Logging.Abstractions;

namespace Plugin.Maui.NearbyConnectivity;

[SuppressMessage(
    "Usage",
    "CA2213:Disposable fields should be disposed",
    Justification = "_disposing, _stopCts and _stateGate stay readable after DisposeAsync for late callbacks " +
        "and racing stops; none of them holds a timer or a wait handle.")]
sealed partial class NearbySession : INearbyConnectivity, INearbyDeviceGateway, IAsyncDisposable
{
    static readonly TimeSpan s_sessionTaskJoinBound = TimeSpan.FromSeconds(2);

    readonly SessionAdvertiser _advertiser;
    readonly Broadcast<NearbyConnection> _connectionDeliveries;
    readonly INearbyTransport _nearbyTransport;
    readonly SessionDiscoverer _discoverer;
    readonly CancellationTokenSource _disposing = new();
    readonly EventLoop _events;
    readonly ILogger _logger;
    readonly DiscoveryRefresher _refresher;
    readonly DeviceRegistry _deviceRegistry;
    readonly RequestRegistry _requests;
    readonly SemaphoreSlim _stateGate = new(1, 1);
    readonly SessionTaskSet _tasks;
    readonly TimeProvider _timeProvider;

    volatile Consent _consent = new(NearbyConsentPolicy.Hold, null);
    CancellationTokenSource _stopCts = new();
    int _availabilityValue = (int)NearbyAvailability.Available;
    int _disposeGuard;
    Task? _suspension;

    internal NearbySession(
        INearbyTransport nearbyTransport,
        ILogger logger,
        NearbyOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(nearbyTransport);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _nearbyTransport = nearbyTransport;
        _nearbyTransport.AttachDeviceGateway(this);
        _logger = logger;
        _timeProvider = timeProvider;
        _events = new EventLoop(logger);
        _deviceRegistry = new DeviceRegistry(change => _events.Raise(DeviceChanged, this, change));
        _requests = new RequestRegistry(_timeProvider, RunRequestExpiryAsync);
        _tasks = new SessionTaskSet(_timeProvider);

        _connectionDeliveries = new Broadcast<NearbyConnection>(() => _nearbyTransport.SnapshotConnections());
        _refresher = new DiscoveryRefresher(
            options.DiscoveryRefreshInterval,
            _timeProvider,
            _deviceRegistry,
            RefreshDiscoveryOnceAsync,
            onException: ex => NearbyLog.DiscoveryRefreshFailed(_logger, ex));

        _advertiser = new SessionAdvertiser(this);
        _discoverer = new SessionDiscoverer(this);

        _nearbyTransport.AvailabilityChanged += OnPlatformAvailabilityChanged;
        _nearbyTransport.Suspended += OnPlatformSuspended;
    }

    public event EventHandler<NearbyDeviceChange>? DeviceChanged;

    public event EventHandler? AvailabilityChanged;

    public IReadOnlyList<NearbyDevice> Devices => _deviceRegistry;

    public IReadOnlyList<NearbyConnection> Connections => _nearbyTransport.SnapshotConnections();

    public NearbyAvailability Availability => (NearbyAvailability)Volatile.Read(ref _availabilityValue);

    public NearbyPresence Presence => _nearbyTransport.Presence;

    bool INearbyDeviceGateway.TryGetRequest(string deviceId, [NotNullWhen(true)] out NearbyConnectionRequest? request)
        => _requests.TryGet(deviceId, out request);

    bool INearbyDeviceGateway.TryGetConnectDeadline(string deviceId, [NotNullWhen(true)] out DateTimeOffset? deadline)
        => _nearbyTransport.TryGetConnectDeadline(deviceId, out deadline);

    internal IAsyncEnumerable<NearbyConnection> ConnectionsAsync(CancellationToken cancellationToken = default)
        => _connectionDeliveries.Replaying(cancellationToken);

    public IAsyncEnumerable<NearbyInboundPayload> PayloadsAsync(CancellationToken cancellationToken = default)
        => new MergedPayloadStream(ConnectionsAsync(cancellationToken));

    public async Task<NearbyAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var availability = await _nearbyTransport.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false);

        if (Interlocked.Exchange(ref _availabilityValue, (int)availability) != (int)availability)
        {
            _events.Raise(AvailabilityChanged, this);
        }

        return availability;
    }

    // Relayed straight to the transport, which owns the presence and the idle span that decides
    // when a change takes effect. The session holds no copy that could disagree with what peers
    // see.
    public void SetPresence(NearbyPresence presence) => _nearbyTransport.SetPresence(presence);

    internal static ILogger LoggerFor(INearbyConnectivity nearby)
        => (nearby as NearbySession)?._logger ?? NullLogger.Instance;

    void OnPlatformAvailabilityChanged() => _tasks.Add(RefreshAvailabilityAsync());

    void OnPlatformSuspended()
        => Volatile.Write(ref _suspension, Task.WhenAll(Volatile.Read(ref _suspension) ?? Task.CompletedTask, SuspendAsync()));

    async Task SuspendAsync()
    {
        try
        {
            await StopAllCoreAsync(
                NearbyEndReason.AppSuspended,
                NearbyActivityStopReason.Suspended).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NearbyLog.SuspendTeardownFailed(_logger, ex);
        }
    }

    async Task RefreshAvailabilityAsync()
    {
        try
        {
            await CheckAvailabilityAsync(_disposing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The session is being disposed.
        }
        catch (Exception ex)
        {
            NearbyLog.AvailabilityRefreshFailed(_logger, ex);
        }
    }

    async Task StartAsync(SessionActivity activity, CancellationToken cancellationToken)
    {
        // Before the availability probe: that probe is a main-thread permission marshal on Android,
        // and a start on an already-running pump is a no-op that should not pay for it. The
        // authoritative check is the one under the gate below; this only skips the wasted work.
        if (activity.IsActive)
        {
            return;
        }

        var availability = await CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false);

        if (!availability.PermitsStart())
        {
            NearbyLog.StartBlocked(_logger, availability);

            throw new NearbyException(
                $"Cannot start: {availability}. Check {nameof(INearbyConnectivity)}." +
                $"{nameof(INearbyConnectivity.Availability)} and resolve it before starting.");
        }

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (activity.IsActive)
            {
                return;
            }

            EnsureStopTokenArmed();

            await activity.StartConfirmedAsync(cancellationToken).ConfigureAwait(false);

            activity.Refresher?.Start();
        }
        finally
        {
            _stateGate.Release();
        }
    }

    async Task StopOneAsync(SessionActivity activity)
    {
        if (activity.Refresher is { } refresher)
        {
            await refresher.CancelAsync().ConfigureAwait(false);
        }

        await _stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            if (activity.IsActive)
            {
                await activity.StopPumpAsync().ConfigureAwait(false);
                await activity.ClearAfterStopAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _stateGate.Release();
        }

        // After the gate: draining joins the refresh loop, which needs the gate to finish.
        if (activity.Refresher is { } draining)
        {
            await draining.DrainAsync().ConfigureAwait(false);
        }
    }

    async Task RefreshDiscoveryAsync(CancellationToken cancellationToken)
    {
        if (!_discoverer.IsActive)
        {
            return;
        }

        if (!await RefreshDiscoveryOnceAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _refresher.SettleAndEvictAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAllAsync()
        => StopAllCoreAsync(NearbyEndReason.SessionStopped, NearbyActivityStopReason.Requested);

    internal async Task StopAllCoreAsync(
        NearbyEndReason closeReason,
        NearbyActivityStopReason stopReason)
    {
        await _refresher.CancelAsync().ConfigureAwait(false);
        var stopping = Volatile.Read(ref _stopCts);
        await stopping.CancelAsync().ConfigureAwait(false);
        await _stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            if (_advertiser.IsActive)
            {
                await _advertiser.StopPumpAsync(reason: stopReason).ConfigureAwait(false);
            }

            if (_discoverer.IsActive)
            {
                await _discoverer.StopPumpAsync(reason: stopReason).ConfigureAwait(false);
            }

            await Task.WhenAll(_nearbyTransport.SnapshotConnections().Select(async connection =>
            {
                try
                {
                    await connection.CloseForAsync(closeReason).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    NearbyLog.StopDisconnectFailed(_logger, connection.RemoteDevice.Id, ex);
                }
            })).ConfigureAwait(false);

            await DeclineOutstandingRequestsAsync().ConfigureAwait(false);
        }
        finally
        {
            _stateGate.Release();
        }

        // Outside the gate: a joined task may need facade state. Cancellation already ran, so
        // the bound is the backstop, not the plan.
        if (!await _tasks.JoinAsync(s_sessionTaskJoinBound).ConfigureAwait(false))
        {
            NearbyLog.SessionTaskJoinTimedOut(_logger, s_sessionTaskJoinBound.TotalSeconds);
        }

        if (ReferenceEquals(Volatile.Read(ref _stopCts), stopping))
        {
            _deviceRegistry.Clear(NearbyEndReason.SessionStopped);
        }

        await _refresher.DrainAsync().ConfigureAwait(false);
    }

    async Task<NearbyConnection> INearbyDeviceGateway.ConnectAsync(string deviceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);

        if (!_deviceRegistry.TryGet(deviceId, out var device))
        {
            throw new NearbyConnectException(
                NearbyConnectFailure.DeviceUnknown,
                $"Cannot connect: device '{deviceId}' is not currently known. It may have gone out of range or stopped advertising.");
        }

        return await ConnectCoreAsync(device, cancellationToken).ConfigureAwait(false);
    }

    async Task<NearbyConnection> ConnectCoreAsync(NearbyDevice device, CancellationToken cancellationToken)
    {
        switch (device.Status)
        {
            case NearbyDeviceStatus.Connected
                when _nearbyTransport.TryGetConnection(device.Id, out var existing):
                return existing;

            case NearbyDeviceStatus.Connecting when _nearbyTransport.TryJoinHandshake(device.Id, out var pending):
                return await pending.WaitAsync(cancellationToken).ConfigureAwait(false);

            case NearbyDeviceStatus.RequestReceived when _requests.TryGet(device.Id, out var inbound):
                return await inbound.AcceptAsync(cancellationToken).ConfigureAwait(false);

            default:
                break;
        }

        Transition(device, NearbyDeviceStatus.Connecting);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, StopToken);

        try
        {
            var connection = await _nearbyTransport.ConnectAsync(device, linkedCts.Token).ConfigureAwait(false);
            OnConnected(device, connection);
            return connection;
        }
        catch (OperationCanceledException ex) when (linkedCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            EndHandshake(device, NearbyEndReason.SessionStopped);

            throw new NearbyConnectException(
                NearbyConnectFailure.Failed,
                $"The session stopped before the handshake with device '{device.Id}' completed.",
                ex);
        }
        catch (Exception ex)
        {
            EndHandshake(device, ReasonFor(ex));
            throw;
        }
    }

    async Task<NearbyConnection> INearbyDeviceGateway.AcceptRequestAsync(
        NearbyConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var device = request.RemoteDevice;

        if (!_requests.TryClaim(request))
        {
            throw new NearbyConnectException(
                NearbyConnectFailure.TimedOut,
                $"The request from device '{device.Id}' is no longer outstanding — it expired or was already answered.");
        }

        request.MarkAnswered();

        Transition(device, NearbyDeviceStatus.Connecting);

        try
        {
            var connection = await request.AcceptCore(cancellationToken).ConfigureAwait(false);
            OnConnected(device, connection);
            return connection;
        }
        catch (Exception ex)
        {
            EndHandshake(device, ReasonFor(ex));
            throw;
        }
    }

    async Task INearbyDeviceGateway.DeclineRequestAsync(
        NearbyConnectionRequest request,
        CancellationToken cancellationToken)
    {

        var device = request.RemoteDevice;

        if (!_requests.TryClaim(request))
        {
            return;
        }

        request.MarkAnswered();

        await request.RejectCore(cancellationToken).ConfigureAwait(false);
        EndHandshake(device, NearbyEndReason.Declined);
    }

    bool INearbyDeviceGateway.TryGetConnection(string deviceId, [NotNullWhen(true)] out NearbyConnection? connection)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);

        return _nearbyTransport.TryGetConnection(deviceId, out connection);
    }

    async Task INearbyDeviceGateway.DisconnectAsync(string deviceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);

        if (!_nearbyTransport.TryGetConnection(deviceId, out var connection))
        {
            return;
        }

        await connection.CloseAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeGuard, 1) != 0)
        {
            return;
        }

        await _disposing.CancelAsync().ConfigureAwait(false);
        _nearbyTransport.AvailabilityChanged -= OnPlatformAvailabilityChanged;
        _nearbyTransport.Suspended -= OnPlatformSuspended;

        if (Volatile.Read(ref _suspension) is { } suspending)
        {
            await suspending.ConfigureAwait(false);
        }

        try
        {
            await StopAllAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NearbyLog.DisposeFailed(_logger, ex);
        }

        await _nearbyTransport.DisposeAsync().ConfigureAwait(false);
        _events.Complete();
    }

    sealed record Consent(NearbyConsentPolicy Policy, Func<NearbyDevice, CancellationToken, ValueTask<bool?>>? Decide);
}
