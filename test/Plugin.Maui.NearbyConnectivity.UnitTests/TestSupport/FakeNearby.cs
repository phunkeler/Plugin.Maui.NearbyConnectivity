using System.Threading.Channels;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// A controllable <see cref="INearbyTransport"/> for driving <see cref="NearbySession"/> from
/// tests: emit discovery events and inbound requests on demand, and decide what connecting does.
/// </summary>
/// <remarks>
/// One shared harness, replacing the five that were re-declared across the two deleted mirror test
/// files. Deliberately hand-written rather than a mock: the session consumes
/// <see cref="IAsyncEnumerable{T}"/> streams whose completion and fault timing are the thing under
/// test, and that is clearer to drive through a channel than to configure on a mock.
/// </remarks>
sealed class FakeNearby : INearbyTransport
{
    Channel<NearbyConnectionRequest> _requests = Channel.CreateUnbounded<NearbyConnectionRequest>();
    Channel<NearbyDeviceEvent> _deviceEvents = Channel.CreateUnbounded<NearbyDeviceEvent>();

    /// <summary>
    /// The fake's connection table — the platform side owns "device X has a live connection", so
    /// the fake tracks what its own <see cref="ConnectAsync"/> and emitted accepts produced.
    /// Mirrors the real platform: registered before the caller observes the connection, removed
    /// when the connection's <see cref="NearbyConnection.Closed"/> completes (the fake's
    /// stand-in for the release path).
    /// </summary>
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, NearbyConnection> _connections
        = new(StringComparer.Ordinal);

    public bool TryGetConnection(string deviceId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NearbyConnection? connection)
        => _connections.TryGetValue(deviceId, out connection);

    public NearbyConnection[] SnapshotConnections() => [.. _connections.Values];

    /// <summary>
    /// The gateway the session hands down, so devices this fake mints act on the real session —
    /// the same wiring NearbyTransport does for PeerLookup.
    /// </summary>
    INearbyDeviceGateway? _deviceGateway;

    public void AttachDeviceGateway(INearbyDeviceGateway gateway) => _deviceGateway = gateway;

    /// <summary>
    /// The device as the platform would hand it up: acting through the attached gateway. Tests
    /// construct bare devices, so this is where they gain their verbs, exactly as
    /// <c>PeerLookup.Record</c> does on a real platform.
    /// </summary>
    internal NearbyDevice Mint(NearbyDevice device) => device.Gateway is null && _deviceGateway is { } gateway
        ? new NearbyDevice(device.Id, device.Presence) { Gateway = gateway, Status = device.Status }
        : device;

    /// <summary>
    /// The fake completes connects synchronously, so nothing is ever mid-handshake to join. A test
    /// exercising the convergent path parks the connect with <see cref="BlockConnect"/> instead.
    /// </summary>
    public bool TryJoinHandshake(
        string deviceId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Task<NearbyConnection>? handshake)
    {
        handshake = null;
        return false;
    }

    /// <summary>
    /// The deadline <see cref="TryGetConnectDeadline"/> reports, so a test can drive
    /// <see cref="NearbyDevice.ConnectExpiresAt"/> without parking a real handshake.
    /// </summary>
    public DateTimeOffset? ConnectDeadline { get; set; }

    public bool TryGetConnectDeadline(
        string deviceId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DateTimeOffset? deadline)
    {
        deadline = ConnectDeadline;
        return deadline is not null;
    }

    /// <summary>
    /// Drops a connection from the fake's table without completing its
    /// <see cref="NearbyConnection.Closed"/>, so a teardown cannot reach it. Models a
    /// connection whose watcher only the session stop token can end.
    /// </summary>
    /// <param name="deviceId">The device whose connection to forget.</param>
    public void ForgetConnection(string deviceId) => _connections.TryRemove(deviceId, out _);

    void RegisterConnection(string deviceId, NearbyConnection connection)
    {
        _connections[deviceId] = connection;
        _ = RemoveOnDisconnectAsync(deviceId, connection);
    }

    async Task RemoveOnDisconnectAsync(string deviceId, NearbyConnection connection)
    {
        await connection.Closed.ConfigureAwait(false);
        _connections.TryRemove(new KeyValuePair<string, NearbyConnection>(deviceId, connection));
    }

    /// <summary>Gets how many times advertising was started, to assert repeat starts are no-ops.</summary>
    public int AdvertiseCallCount { get; private set; }

    /// <summary>Gets how many times discovery was started.</summary>
    public int DiscoverCallCount { get; private set; }

    /// <summary>Set to make the advertise stream fault, simulating a platform start failure.</summary>
    public Exception? AdvertiseFault { get; init; }

    /// <summary>Set to make the discover stream fault, simulating a platform start failure.</summary>
    public Exception? DiscoverFault { get; init; }

    /// <summary>The connection <see cref="ConnectAsync"/> resolves to.</summary>
    public NearbyConnection? ConnectResult { get; set; }

    /// <summary>Set to make <see cref="ConnectAsync"/> throw, simulating a rejected connection.</summary>
    public Exception? ConnectFault { get; init; }

    /// <summary>What <see cref="CheckAvailabilityAsync"/> reports.</summary>
    public NearbyAvailability Availability { get; set; } = NearbyAvailability.Available;

    /// <summary>Gets how many times availability was checked.</summary>
    public int CheckAvailabilityCallCount { get; private set; }

    /// <summary>
    /// Set to make <see cref="CheckAvailabilityAsync"/> throw, simulating a platform probe that
    /// fails — the shape a broadcast-driven refresh has to survive.
    /// </summary>
    public Exception? AvailabilityFault { get; set; }

    public Task<NearbyAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckAvailabilityCallCount++;

        return AvailabilityFault is { } fault
            ? Task.FromException<NearbyAvailability>(fault)
            : Task.FromResult(Availability);
    }

    /// <inheritdoc/>
    public event Action? AvailabilityChanged;

    /// <inheritdoc/>
    public event Action? Suspended;

    /// <summary>
    /// Raises <see cref="AvailabilityChanged"/>, standing in for an Android radio broadcast.
    /// </summary>
    public void EmitAvailabilityChanged() => AvailabilityChanged?.Invoke();

    /// <summary>
    /// Raises <see cref="Suspended"/>, standing in for iOS backgrounding the app.
    /// </summary>
    public void EmitSuspended() => Suspended?.Invoke();

    /// <summary>Gets whether anything is still subscribed to either platform signal.</summary>
    public bool HasSignalSubscribers => AvailabilityChanged is not null || Suspended is not null;

    public async IAsyncEnumerable<NearbyConnectionRequest> AdvertiseAsync(
        TaskCompletionSource started,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        AdvertiseCallCount++;

        if (AdvertiseFault is not null)
        {
            started.SetException(AdvertiseFault);
            throw AdvertiseFault;
        }

        // A fresh channel per call, mirroring NearbyTransport.cs's Interlocked.Exchange: a
        // restart after a prior FaultAdvertiseStream must reach a channel that isn't already faulted.
        var requests = Channel.CreateUnbounded<NearbyConnectionRequest>();
        _requests = requests;

        started.TrySetResult();

        await foreach (var request in requests.Reader.ReadAllAsync(cancellationToken))
        {
            yield return request;
        }
    }

    public async IAsyncEnumerable<NearbyDeviceEvent> DiscoverAsync(
        TaskCompletionSource started,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        DiscoverCallCount++;

        if (DiscoverFault is not null)
        {
            started.SetException(DiscoverFault);
            throw DiscoverFault;
        }

        var deviceEvents = Channel.CreateUnbounded<NearbyDeviceEvent>();
        _deviceEvents = deviceEvents;

        started.TrySetResult();

        await foreach (var deviceEvent in deviceEvents.Reader.ReadAllAsync(cancellationToken))
        {
            yield return deviceEvent;
        }
    }

    /// <summary>
    /// Faults the advertise stream after a successful start, simulating a later platform failure
    /// (e.g. the radio drops mid-session) rather than a start failure.
    /// </summary>
    public void FaultAdvertiseStream(Exception exception) => _requests.Writer.TryComplete(exception);

    /// <summary>Faults the discover stream after a successful start. See <see cref="FaultAdvertiseStream"/>.</summary>
    public void FaultDiscoverStream(Exception exception) => _deviceEvents.Writer.TryComplete(exception);

    /// <summary>How many times <see cref="ConnectAsync"/> reached this fake.</summary>
    /// <remarks>
    /// Lets a test assert that a second connect was short-circuited above the platform rather than
    /// merely returning the same instance.
    /// </remarks>
    public int ConnectCallCount { get; private set; }

    /// <summary>
    /// When set before a call, <see cref="ConnectAsync"/> parks until <see cref="ReleaseConnect"/>,
    /// so a test can observe the in-flight handshake state.
    /// </summary>
    public bool BlockConnect { get; set; }

    readonly TaskCompletionSource _connectGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Releases a connect parked by <see cref="BlockConnect"/>.</summary>
    public void ReleaseConnect() => _connectGate.TrySetResult();

    public async Task<NearbyConnection> ConnectAsync(NearbyDevice device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        ConnectCallCount++;

        if (BlockConnect)
        {
            await _connectGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await ConnectCoreAsync(device, cancellationToken).ConfigureAwait(false);
    }

    Task<NearbyConnection> ConnectCoreAsync(NearbyDevice device, CancellationToken cancellationToken)
    {
        if (ConnectFault is not null)
        {
            return Task.FromException<NearbyConnection>(ConnectFault);
        }

        var connection = ConnectResult ?? throw new InvalidOperationException(
            $"{nameof(ConnectResult)} was not set on the fake.");

        // Registered before the caller observes it, mirroring ResolveConnectionTcs.
        RegisterConnection(device.Id, connection);

        return Task.FromResult(connection);
    }

    /// <summary>The effective presence, as the real transport reports it.</summary>
    public NearbyPresence Presence { get; private set; } = new() { DisplayName = "Fake" };

    /// <summary>
    /// Whether the fake reports an idle span, so a test can drive the "apply now versus hold"
    /// branch without a real transport. A fake of a fact: the transport-level test is the one that
    /// pins how the real span is computed.
    /// </summary>
    public bool IsIdle { get; set; } = true;

    /// <summary>The presence set while not idle, still waiting.</summary>
    NearbyPresence? _pendingPresence;

    public void SetPresence(NearbyPresence presence)
    {
        ArgumentNullException.ThrowIfNull(presence);

        if (IsIdle)
        {
            Presence = presence;
            _pendingPresence = null;

            return;
        }

        _pendingPresence = presence;
    }

    /// <summary>Applies a held presence, standing in for the real transport's idle edge.</summary>
    public void GoIdle()
    {
        IsIdle = true;

        if (_pendingPresence is { } pending)
        {
            Presence = pending;
            _pendingPresence = null;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Emits a device-found event and waits for the session to apply it. Returns the device as the
    /// session published it — carrying a gateway, so it can act — which is what a consumer holds
    /// and therefore what a test connects through.
    /// </summary>
    public async Task<NearbyDevice> EmitDeviceFoundAsync(NearbyDevice device)
    {
        var minted = Mint(device);
        _deviceEvents.Writer.TryWrite(new NearbyDeviceEvent(minted, Found: true));
        await DrainAsync();
        return minted;
    }

    /// <summary>Emits a device-lost event and waits for the session to apply it.</summary>
    public async Task EmitDeviceLostAsync(NearbyDevice device)
    {
        _deviceEvents.Writer.TryWrite(new NearbyDeviceEvent(Mint(device), Found: false));
        await DrainAsync();
    }

    /// <summary>
    /// Emits an inbound connection request and waits for the session to surface it.
    /// </summary>
    /// <param name="device">The requesting device.</param>
    /// <param name="onAccept">What accepting produces — return a connection or throw.</param>
    /// <param name="onReject">Optional callback invoked when the request is rejected.</param>
    /// <param name="deadline">
    /// The offer's deadline, as the platform would compute it at receipt. Defaults to the far
    /// future, which the registry clamps to the trust bound — effectively "does not expire during
    /// the test".
    /// </param>
    public async Task EmitRequestAsync(
        NearbyDevice device,
        Func<NearbyConnection> onAccept,
        Action? onReject = null,
        DateTimeOffset? deadline = null)
    {
        _requests.Writer.TryWrite(new NearbyConnectionRequest(
            Mint(device),
            deadline ?? DateTimeOffset.MaxValue,
            accept: _ =>
            {
                try
                {
                    var connection = onAccept();
                    RegisterConnection(device.Id, connection);
                    return Task.FromResult(connection);
                }
                catch (Exception ex)
                {
                    return Task.FromException<NearbyConnection>(ex);
                }
            },
            reject: _ =>
            {
                onReject?.Invoke();
                return Task.CompletedTask;
            },
            TimeProvider.System));

        await DrainAsync();
    }

    TaskCompletionSource<NearbyConnection>? _capturedAccept;

    /// <summary>
    /// Hands back the source the next emitted accept will await, so a test can observe how that
    /// accept ended. Pair with <see cref="EmitRequestThatOnlyCancellationEndsAsync"/>.
    /// </summary>
    public TaskCompletionSource<NearbyConnection> CaptureNextAcceptToken()
        => _capturedAccept = new TaskCompletionSource<NearbyConnection>(
            TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Emits an inbound request whose accept never completes on its own, so only the token the
    /// session passes can release it.
    /// </summary>
    /// <remarks>
    /// The platform makes no promise that a handshake callback ever arrives, and auto-accept is
    /// started by a callback rather than a caller, so no caller's token reaches it. This is the
    /// shape that hangs disposal when the session has no disposal token of its own to pass.
    /// </remarks>
    /// <param name="device">The requesting device.</param>
    /// <param name="deadline">The offer's deadline. See <see cref="EmitRequestAsync"/>.</param>
    public async Task EmitRequestThatOnlyCancellationEndsAsync(NearbyDevice device, DateTimeOffset? deadline = null)
    {
        _requests.Writer.TryWrite(new NearbyConnectionRequest(
            Mint(device),
            deadline ?? DateTimeOffset.MaxValue,
            accept: ct =>
            {
                var tcs = _capturedAccept ?? new TaskCompletionSource<NearbyConnection>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                ct.Register(() => tcs.TrySetCanceled(ct));

                return tcs.Task;
            },
            reject: _ => Task.CompletedTask,
            TimeProvider.System));

        await DrainAsync();
    }

    /// <summary>
    /// Yields until the session's pump has drained what was just written. The pump reads on a
    /// background task, so a test that asserted immediately would race it.
    /// </summary>
    static async Task DrainAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            await Task.Yield();
        }

        await Task.Delay(10);
    }
}
