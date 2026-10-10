using Android.Content;
using Android.Gms.Extensions;
using AndroidUri = Android.Net.Uri;

namespace Plugin.Maui.NearbyConnectivity;

// The Android backend: maps IPlatformAdapter onto Google Nearby Connections. Outbound operations
// go through the interface; inbound GMS callbacks call the transport's internal methods directly —
// the surface the device tests drive.
sealed partial class AndroidAdapter : IPlatformAdapter
{
    readonly NearbyTransport _transport;
    readonly ILogger _logger;
    readonly Lock _availabilityGate = new();

    // UKEY2 hands the authentication token to OnConnectionInitiated, but the connection is not
    // built until OnConnectionResult, so the derived code waits here in between. Keyed by device id
    // because that is what both callbacks resolve to. An entry outlives a failed handshake, which
    // OnConnectionResult and OnDisconnected both clear.
    readonly ConcurrentDictionary<string, string> _authenticationCodes = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<long, (string DeviceId, Payload Payload)> _incomingPayloads = [];
    readonly ConcurrentDictionary<long, OutgoingTransfer> _outgoingTransfers = [];

    // The name frame and the stream payload race: GMS completes the bytes frame through the work
    // queue while the stream payload arrives directly.
    readonly StreamPairing _streamPairing = new();

    // Created on first subscription, released at disposal. Registering broadcast receivers is the
    // watcher's constructor, so an adapter nobody subscribes to registers nothing.
    AvailabilityWatcher? _availabilityWatcher;
    Action? _availabilityChanged;

    IConnectionsClient? _advertiseClient;
    IConnectionsClient? _discoverClient;
    IConnectionsClient? _client;

    internal AndroidAdapter(NearbyTransport transport)
    {
        _transport = transport;
        _logger = transport.Logger;
    }

    public event Action? AvailabilityChanged
    {
        add
        {
            lock (_availabilityGate)
            {
                _availabilityChanged += value;
                _availabilityWatcher ??= new AvailabilityWatcher(() => _availabilityChanged?.Invoke(), _logger);
            }
        }
        remove
        {
            lock (_availabilityGate)
            {
                _availabilityChanged -= value;
            }
        }
    }

    // Never raised. Android apps are not suspended the way iOS suspends them, and Nearby
    // Connections keeps its connections across a backgrounding.
    public event Action? Suspended
    {
        add { }
        remove { }
    }

    // For everything other than starting advertising and discovery. Held for the adapter's life,
    // so a send does not build a client per call. Advertising and discovery keep their OWN clients
    // because each is released by its own stop, and disposing a shared one there would take the
    // other half down with it.
    IConnectionsClient Client => _client ??= NearbyClass.GetConnectionsClient(Platform.AppContext);

    string RequireEndpoint(string deviceId, string operation)
        => _transport.PeerLookup.TryGetEndpointId(deviceId, out var endpointId)
            ? endpointId
            : throw new NearbyException(
                $"Cannot {operation}: no active connection for device '{deviceId}'.");

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Java peers handed to Google Play services are owned by the Java side; disposing the " +
            "managed peer would break its callbacks.")]
    public async Task StartAdvertisingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _advertiseClient ??= NearbyClass.GetConnectionsClient(Platform.AppContext);

        try
        {
            var presence = _transport.Presence;

            await (presence.Info.IsEmpty
                ? _advertiseClient.StartAdvertisingAsync(
                    presence.DisplayName,
                    _transport.Options.ServiceId,
                    new LifecycleCallback(this),
                    NewAdvertisingOptions())
                : _advertiseClient.StartAdvertising(
                    ControlMessage.EncodeAdvertisement(presence),
                    _transport.Options.ServiceId,
                    new LifecycleCallback(this),
                    NewAdvertisingOptions()).AsAsync()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            NearbyLog.AdvertisingStartFailed(_logger, ex);
            throw new NearbyException("Failed to start advertising.", ex);
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Java peers handed to Google Play services are owned by the Java side; disposing the " +
            "managed peer would break its callbacks.")]
    AdvertisingOptions NewAdvertisingOptions()
        => new AdvertisingOptions.Builder()
            .SetStrategy(ToStrategy(_transport.Options.Android.Topology))
            .SetLowPower(_transport.Options.Android.UseLowPower)
            .SetConnectionType(ToConnectionType(_transport.Options.Android.ConnectionType))
            .Build();

    public void StopAdvertising()
    {
        _advertiseClient?.StopAdvertising();
        _advertiseClient?.Dispose();
        _advertiseClient = null;
    }

    internal async Task OnConnectionInitiatedAsync(string endpointId, ConnectionInfo connectionInfo)
    {
        try
        {
            var deviceId = _transport.PeerLookup.DeviceIdFor(endpointId);
            var endpointInfo = connectionInfo.GetEndpointInfo();
            var declaredWindow = OfferWindowPolicy.s_default;

            if (ControlMessage.TryDecodeConnectRequest(
                    endpointInfo ?? [], out var framedWindow, out _, out _))
            {
                declaredWindow = framedWindow;
            }
            else if (connectionInfo.IsIncomingConnection
                && !ControlMessage.TryDecodeAdvertisement(endpointInfo ?? [], out _, out _))
            {
                NearbyLog.RequestFrameMissing(_logger, deviceId);
            }

            var device = RecordPeer(deviceId, endpointInfo, connectionInfo.EndpointName);

            if (connectionInfo.GetRawAuthenticationToken() is { Length: > 0 } token)
            {
                _authenticationCodes[device.Id] = AuthenticationCodeRules.FromToken(token);
            }
            else
            {
                NearbyLog.AuthenticationCodeMissing(_logger, device.Id);
            }

            if (connectionInfo.IsIncomingConnection)
            {
                NearbyLog.RequestReceived(_logger, device.Id);

                var request = _transport.CreateInboundRequest(
                    device,
                    declaredWindow,
                    sendAccept: () => RespondToConnectionAsync(device, accept: true),
                    sendReject: () => RespondToConnectionAsync(device, accept: false));

                _transport.WriteConnectionRequest(request);
            }
            else
            {
                await RespondToConnectionAsync(device, accept: true).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            var deviceId = _transport.PeerLookup.DeviceIdFor(endpointId);

            NearbyLog.PlatformCallbackFailed(_logger, nameof(OnConnectionInitiatedAsync), deviceId, ex);
            _transport.FaultConnectionTcs(deviceId, ex);
        }
    }

    internal void OnConnectionResult(string endpointId, ConnectionResolution resolution)
        => Callback(nameof(OnConnectionResult), endpointId, () =>
        {
            var deviceId = _transport.PeerLookup.DeviceIdFor(endpointId);

            NearbyLog.ConnectionResult(
                _logger,
                deviceId,
                resolution.Status.StatusCode,
                resolution.Status.StatusMessage ?? "Unknown",
                resolution.Status.IsSuccess);

            if (resolution.Status.IsSuccess)
            {
                if (!_transport.PeerLookup.TryGetDevice(deviceId, out var device))
                {
                    _transport.FaultConnectionTcs(deviceId, new NearbyConnectException(
                        NearbyConnectFailure.DeviceUnknown,
                        $"Device not found in manager for device '{deviceId}' after successful connection."));
                    return;
                }

                _authenticationCodes.TryRemove(deviceId, out var authenticationCode);

                _transport.CompleteHandshake(
                    device,
                    new AndroidConnection(this, deviceId),
                    authenticationCode);
            }
            else
            {
                _authenticationCodes.TryRemove(deviceId, out _);

                // The handshake failed, not the peer. GMS documents no link between a failed
                // connection result and the endpoint's discovery lifetime: OnEndpointLost is
                // specified only for an endpoint that is "no longer discoverable", which a peer
                // that merely declined still is, and an endpoint id stays valid for a later
                // requestConnection — Google's own connectivity sample retries against the id
                // that just failed. Forgetting the peer here would strand a Visible row whose
                // mapping is gone, so a retry could never be sent. OnEndpointLost is the one
                // signal that the peer itself went away; a dropped live connection
                // (OnDisconnected) is the other.
                _transport.FaultConnectionTcs(deviceId, new NearbyConnectException(
                    NearbyConnectFailure.Failed,
                    $"Connection to device '{deviceId}' failed: {resolution.Status.StatusMessage} (code {resolution.Status.StatusCode})."));
            }
        });

    internal void OnDisconnected(string endpointId)
        => Callback(nameof(OnDisconnected), endpointId, () =>
        {
            var deviceId = _transport.PeerLookup.DeviceIdFor(endpointId);
            NearbyLog.DeviceDisconnected(_logger, deviceId);

            _authenticationCodes.TryRemove(deviceId, out _);

            // GMS dropped the endpoint, so the peer is gone with it. Forget it here rather than
            // waiting for an EndpointLost that does not follow a disconnect.
            _transport.ReleaseConnectionFromCallback(deviceId, forgetPeer: true);
        });

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Java peers handed to Google Play services are owned by the Java side; disposing the " +
            "managed peer would break its callbacks.")]
    public async Task StartDiscoveryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _discoverClient ??= NearbyClass.GetConnectionsClient(Platform.AppContext);

        try
        {
            await _discoverClient.StartDiscoveryAsync(
                _transport.Options.ServiceId,
                new DiscoveryCallback(this),
                new DiscoveryOptions.Builder()
                    .SetStrategy(ToStrategy(_transport.Options.Android.Topology))
                    .SetLowPower(_transport.Options.Android.UseLowPower)
                    .Build()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            NearbyLog.DiscoveryStartFailed(_logger, ex);
            throw new NearbyException("Failed to start discovery.", ex);
        }
    }

    public void StopDiscovering()
    {
        _discoverClient?.StopDiscovery();
        _discoverClient?.Dispose();
        _discoverClient = null;
    }

    // Nothing to release: GMS has no shared session handle. The per-run clients are released by
    // their own stops, and _client is deliberately held for the process.
    public void ReleaseIdleState()
    {
        // Nothing is held for the idle span on this platform.
    }

    NearbyDevice RecordPeer(string deviceId, byte[]? endpointInfo, string endpointName)
    {
        var frame = endpointInfo ?? [];

        return ControlMessage.TryDecodeConnectRequest(frame, out _, out var name, out var info)
            || ControlMessage.TryDecodeAdvertisement(frame, out name, out info)
            ? _transport.PeerLookup.Record(deviceId, name, info)
            : _transport.PeerLookup.Record(deviceId, NearbyText.Truncate(endpointName));
    }

    internal void OnEndpointFound(string endpointId, DiscoveredEndpointInfo info)
        => Callback(nameof(OnEndpointFound), endpointId, () =>
        {
            var device = RecordPeer(
                _transport.PeerLookup.DeviceIdFor(endpointId),
                info.GetEndpointInfo(),
                info.EndpointName);

            _transport.OnDeviceFound(device);
        });

    internal void OnEndpointLost(string endpointId)
        => Callback(nameof(OnEndpointLost), endpointId, ()
            => _transport.OnDeviceLost(_transport.PeerLookup.DeviceIdFor(endpointId)));

    internal void OnPayloadReceived(string endpointId, Payload payload)
        => Callback(nameof(OnPayloadReceived), endpointId, () =>
        {
            var deviceId = _transport.PeerLookup.DeviceIdFor(endpointId);

            NearbyLog.PayloadReceived(_logger, deviceId, payload.Id, payload.PayloadType);

            if (payload.PayloadType == Payload.Type.Stream)
            {
                HandleInboundStream(deviceId, payload);
                return;
            }

            _incomingPayloads.TryAdd(payload.Id, (deviceId, payload));
        });

    internal void OnPayloadTransferUpdate(string endpointId, PayloadTransferUpdate update)
        => Callback(nameof(OnPayloadTransferUpdate), endpointId, () =>
        {
            var deviceId = _transport.PeerLookup.DeviceIdFor(endpointId);

            NearbyLog.PayloadTransferUpdate(
                _logger,
                deviceId,
                update.PayloadId,
                update.TransferStatus,
                update.TotalBytes,
                update.BytesTransferred);

            if (_outgoingTransfers.TryGetValue(update.PayloadId, out var outgoingTransfer))
            {
                var status = ToNearbyTransferStatus(update.TransferStatus);
                outgoingTransfer.OnUpdate(new NearbyTransferProgress(
                    payloadId: update.PayloadId,
                    bytesTransferred: update.BytesTransferred,
                    totalBytes: update.TotalBytes,
                    status));

                return;
            }

            if (update.TransferStatus == PayloadTransferUpdate.Status.InProgress
                && _incomingPayloads.TryGetValue(update.PayloadId, out var inboundEntry))
            {
                _transport.Connections.ReportInboundProgress(inboundEntry.DeviceId, new NearbyTransferProgress(
                    payloadId: update.PayloadId,
                    bytesTransferred: update.BytesTransferred,
                    totalBytes: update.TotalBytes,
                    NearbyTransferStatus.InProgress));
            }

            if (update.TransferStatus == PayloadTransferUpdate.Status.Success)
            {
                var payloadId = update.PayloadId;
                _ = _transport.Connections.WorkQueue.Enqueue(deviceId, () => OnIncomingPayloadSuccess(deviceId, payloadId));
            }
            else if (update.TransferStatus is PayloadTransferUpdate.Status.Failure or PayloadTransferUpdate.Status.Canceled
                && _incomingPayloads.TryRemove(update.PayloadId, out var deadEntry))
            {
                if (update.TransferStatus == PayloadTransferUpdate.Status.Canceled)
                {
                    NearbyLog.InboundPayloadCanceled(_logger, deviceId, update.PayloadId);
                }
                else
                {
                    NearbyLog.InboundPayloadFailed(_logger, deviceId, update.PayloadId, "the platform reported a failed transfer");
                }

                DisposeIncomingPayload(deadEntry.Payload);
                _transport.Connections.ReportInboundProgress(deadEntry.DeviceId, new NearbyTransferProgress(
                    payloadId: update.PayloadId,
                    bytesTransferred: update.BytesTransferred,
                    totalBytes: update.TotalBytes,
                    ToNearbyTransferStatus(update.TransferStatus)));
            }
        });

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership passes to the connection's payload channel, which disposes undelivered " +
            "payloads when it closes.")]
    async Task OnIncomingPayloadSuccess(string deviceId, long payloadId)
    {
        if (!_incomingPayloads.TryRemove(payloadId, out var entry))
        {
            return;
        }

        var copyToken = _transport.Connections.InboundWorkToken(deviceId);

        if (entry.Payload.PayloadType != Payload.Type.File
            && entry.Payload.AsBytes() is { } maybeControl
            && ControlMessage.HasSignature(maybeControl))
        {
            if (ControlMessage.TryDecode(maybeControl, out var controlType))
            {
                HandleControlFrame(deviceId, controlType, maybeControl);
            }
            else
            {
                NearbyLog.ControlFrameUnreadable(_logger, deviceId, maybeControl.Length);
            }

            DisposeIncomingPayload(entry.Payload);
            return;
        }

        NearbyPayload? nearbyPayload;

        if (entry.Payload.PayloadType == Payload.Type.File)
        {
            // The copy logs every outcome it can distinguish — no URI, no stream, canceled by
            // teardown, failed — so a null here needs no second diagnosis from this side.
            nearbyPayload = await CopyFilePayloadAsync(deviceId, entry.Payload, InboundStaging.Root, copyToken)
                .ConfigureAwait(false);
        }
        else if (entry.Payload.AsBytes() is { } bytes)
        {
            nearbyPayload = new NearbyBytesPayload(bytes);
        }
        else
        {
            nearbyPayload = null;
            NearbyLog.InboundPayloadFailed(_logger, deviceId, payloadId, "it carries neither bytes nor a file");
        }

        if (nearbyPayload is not null)
        {
            _transport.Connections.WritePayload(deviceId, nearbyPayload);
        }

        DisposeIncomingPayload(entry.Payload);

        _transport.Connections.CompleteInboundTransfer(
            deviceId,
            payloadId,
            nearbyPayload is null
                ? NearbyTransferStatus.Failed
                : NearbyTransferStatus.Completed);
    }

    static void DisposeIncomingPayload(Payload payload)
    {
        payload.Close();
        payload.Dispose();
    }

    // Sends the name frame first, then the stream payload, on the same ordered channel. The
    // returned stream is the writable half of a managed pipe whose reader GMS consumes.
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned stream owns the pipe half; the caller disposes it.")]
    [SuppressMessage(
        "Maintainability",
        "CA1508:Avoid dead conditional code",
        Justification = "payload is still null when Payload.FromStream throws.")]
    internal async Task<Stream> OpenStreamAsync(string deviceId, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _transport.Connections.RequireConnected(deviceId, "open a stream");
        var endpointId = RequireEndpoint(deviceId, "open a stream");
        var pipe = Android.OS.ParcelFileDescriptor.CreatePipe()
            ?? throw new NearbyTransferException("Cannot open a stream: the platform could not create a pipe.");

        Payload? payload = null;
        var handedOff = false;

        try
        {
            payload = Payload.FromStream(pipe[0])
                ?? throw new NearbyTransferException("Cannot open a stream: the platform rejected the stream payload.");

            using (var frame = Payload.FromBytes(ControlMessage.EncodeStreamName(payload.Id, name))
                ?? throw new NearbyTransferException("Cannot open a stream: the platform rejected the name frame."))
            {
                await Client.SendPayloadAsync(endpointId, frame).ConfigureAwait(false);
            }

            await Client.SendPayloadAsync(endpointId, payload).ConfigureAwait(false);
            handedOff = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not NearbyException)
        {
            NearbyLog.EventWriteFailed(_logger, nameof(OpenStreamAsync), deviceId, ex);
            throw new NearbyTransferException(
                $"Failed to open a stream to device '{deviceId}'.", ex);
        }
        finally
        {
            if (!handedOff)
            {
                payload?.Dispose();
                SafeClose(pipe[0], deviceId);
                SafeClose(pipe[1], deviceId);
            }
        }

        return new NonSeekableStream(
            new Android.Runtime.OutputStreamInvoker(
                new Android.OS.ParcelFileDescriptor.AutoCloseOutputStream(pipe[1])));
    }

    // Logs rather than throws: this runs on a failure path that already has an exception to
    // report.
    void SafeClose(Android.OS.ParcelFileDescriptor? half, string deviceId)
    {
        if (half is null)
        {
            return;
        }

        try
        {
            half.Close();
            half.Dispose();
        }
        catch (Exception ex)
        {
            NearbyLog.EventWriteFailed(_logger, nameof(OpenStreamAsync), deviceId, ex);
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership passes to the connection's payload channel, which disposes undelivered " +
            "payloads when it closes.")]
    void HandleInboundStream(string deviceId, Payload payload)
    {
        var raw = payload.AsStream()?.AsInputStream();

        if (raw is null)
        {
            NearbyLog.InboundPayloadFailed(_logger, deviceId, payload.Id, "the platform gave no stream");
            return;
        }

        var stream = new NonSeekableStream(raw);
        var half = _streamPairing.OfferStream(deviceId, payload.Id, stream, out var name);

        if (half == StreamHalf.Refused)
        {
            NearbyLog.StreamHalfRefused(_logger, deviceId, payload.Id);
            stream.Dispose();
        }
        else if (name is not null)
        {
#pragma warning disable NEARBY001 // The plugin delivers its own experimental type; consumers opt in.
            _transport.Connections.WritePayload(deviceId, new NearbyStreamPayload(name, stream));
#pragma warning restore NEARBY001
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership passes to the connection's payload channel, which disposes undelivered " +
            "payloads when it closes.")]
    void HandleControlFrame(string deviceId, ControlMessageType type, byte[] frame)
    {
        switch (type)
        {
            case ControlMessageType.StreamName
                when ControlMessage.TryDecodeStreamName(frame, out var payloadId, out var name) && name is not null:
                var half = _streamPairing.OfferName(deviceId, payloadId, name, out var stream);

                if (half == StreamHalf.Refused)
                {
                    NearbyLog.StreamHalfRefused(_logger, deviceId, payloadId);
                }
                else if (stream is not null)
                {
#pragma warning disable NEARBY001 // The plugin delivers its own experimental type; consumers opt in.
                    _transport.Connections.WritePayload(deviceId, new NearbyStreamPayload(name, stream));
#pragma warning restore NEARBY001
                }

                break;

            case ControlMessageType.Disconnect:
                // See the iOS adapter's matching case: the peer closed the connection, not the
                // link, so it stays discovered and reconnectable.
                NearbyLog.PeerAnnouncedDisconnect(_logger, deviceId);
                _transport.ReleaseConnectionFromCallback(deviceId, forgetPeer: false);
                break;

            default:
                NearbyLog.ControlFrameUnknown(_logger, deviceId, type);
                break;
        }
    }

    // Logs every outcome itself, so a null return means only "nothing to deliver" and the caller
    // does not re-diagnose it.
    async Task<NearbyFilePayload?> CopyFilePayloadAsync(
        string deviceId,
        Payload payload,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        using var sourceFile = payload.AsFile();
        using var sourceUri = sourceFile?.AsUri();

        if (sourceUri is null)
        {
            NearbyLog.InboundPayloadFailed(_logger, deviceId, payload.Id, "the platform gave no file URI");
            return null;
        }

        var fileName = ResolveResourceName(sourceUri);
        var source = sourceUri.ToString() ?? string.Empty;
        string? destinationPath = null;

        try
        {
            using var inputStream = Platform.AppContext.ContentResolver?.OpenInputStream(sourceUri);

            if (inputStream is null)
            {
                NearbyLog.InboundPayloadFailed(_logger, deviceId, payload.Id, "the received file could not be opened");
                return null;
            }

            var outputStream = InboundStaging.ClaimUniquePath(destinationDirectory, fileName);
            destinationPath = outputStream.Name;

            using (outputStream)
            {
                await inputStream.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Teardown, not a failure: the connection dropped or the session was disposed mid-copy.
            // Logged at Debug rather than Error so a routine disconnect does not report a fault.
            NearbyLog.FileCopyCanceled(_logger, source, destinationPath ?? destinationDirectory);
            _transport.Staging.DeletePartialDestination(destinationPath);
            return null;
        }
        catch (Exception ex)
        {
            NearbyLog.FileCopyFailed(_logger, source, destinationPath ?? destinationDirectory, ex);
            _transport.Staging.DeletePartialDestination(destinationPath);
            return null;
        }
        finally
        {
            // Deleted on every path, cancellation included. A canceled payload is undeliverable
            // anyway — the connection is gone — so keeping the GMS original would leave a file in
            // shared storage that nothing will ever collect.
            try
            {
                Platform.AppContext.ContentResolver?.Delete(sourceUri, null, null);
            }
            catch (Exception ex)
            {
                NearbyLog.PlatformFileDeleteFailed(_logger, source, ex);
            }
        }

        return NearbyFilePayload.Staged(destinationPath);
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Java peers handed to Google Play services are owned by the Java side; disposing the " +
            "managed peer would break its callbacks.")]
    [SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "RequestConnectionAsync has no byte[] overload; the GMS task is awaited through AsAsync.")]
    public async Task InitiateConnectAsync(NearbyDevice device, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_transport.PeerLookup.TryGetEndpointId(device.Id, out var endpointId))
        {
            _transport.FaultConnectionTcs(device.Id, new NearbyConnectException(
                NearbyConnectFailure.DeviceUnknown,
                $"Cannot connect: device '{device.Id}' is not currently visible. Ensure it is actively advertising and within range."));
            return;
        }

        try
        {
            await Client
                .RequestConnection(
                    ControlMessage.EncodeConnectRequest(_transport.Options.OfferWindow, _transport.Presence),
                    endpointId,
                    new LifecycleCallback(this))
                .AsAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            try
            {
                Client.DisconnectFromEndpoint(endpointId);
            }
            catch (Exception disconnectEx)
            {
                NearbyLog.StaleConnectionClearFailed(_logger, device.Id, disconnectEx);
            }

            _transport.FaultConnectionTcs(device.Id, new NearbyConnectException(
                NearbyConnectFailure.Failed,
                $"Failed to initiate connection to device '{device.Id}'.", ex));
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Java peers handed to Google Play services are owned by the Java side; disposing the " +
            "managed peer would break its callbacks.")]
    internal Task RespondToConnectionAsync(NearbyDevice device, bool accept)
    {
        if (!_transport.PeerLookup.TryGetEndpointId(device.Id, out var endpointId))
        {
            throw new NearbyConnectException(
                NearbyConnectFailure.DeviceUnknown,
                $"Cannot respond to the connection request from device '{device.Id}': the platform no longer tracks it.");
        }

        return accept
            ? Client.AcceptConnectionAsync(endpointId, new InboundPayloadCallback(this))
            : Client.RejectConnectionAsync(endpointId);
    }

    public async Task AbandonConnectAsync(NearbyDevice device)
    {
        try
        {
            // DisconnectFromEndpoint reclaims the half-open endpoint; the peer itself stays
            // discovered and reconnectable, so its mapping stays too.
            await DisconnectEndpointAsync(device.Id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NearbyLog.AbandonConnectFailed(_logger, device.Id, ex);
        }
    }

    internal async ValueTask DisconnectEndpointAsync(string deviceId)
    {
        NearbyLog.Disconnecting(_logger, deviceId);

        if (_transport.PeerLookup.TryGetEndpointId(deviceId, out var endpointId))
        {
            Client.DisconnectFromEndpoint(endpointId);
        }

        await _transport.ReleaseConnectionAsync(deviceId).ConfigureAwait(false);
    }

    internal async Task SendBytesAsync(
        string deviceId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _transport.Connections.RequireConnected(deviceId, "send bytes");

        var endpointId = RequireEndpoint(deviceId, "send bytes");

        // GMS binds byte[], so the copy happens here rather than at the public boundary.
        using var payload = Payload.FromBytes(data.ToArray());

        try
        {
            await Client.SendPayloadAsync(endpointId, payload).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NearbyLog.SendBytesFailed(_logger, deviceId, ex);
            throw new NearbyTransferException(
                $"Failed to send bytes to device '{deviceId}'.", ex);
        }
    }

    internal async Task SendFileAsync(
        string deviceId,
        string uri,
        string? fileName,
        IProgress<NearbyTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _transport.Connections.RequireConnected(deviceId, "send file");

        var endpointId = RequireEndpoint(deviceId, "send file");

        using var androidUri = TryCreateUri(uri);

        if (androidUri is null)
        {
            NearbyLog.InvalidFileUri(_logger, uri);
            throw new NearbyTransferException("Cannot send file: the URI is not a valid or supported scheme. Use a file:// or content:// URI.");
        }

        var filePayload = BuildFilePayload(androidUri, fileName) ?? throw new NearbyTransferException("Cannot send file: failed to open the file descriptor for the given URI.");
        var transfer = new OutgoingTransfer(
            deviceId,
            filePayload.Id,
            progress,
            _transport.TransferInactivityTimeout,
            _transport.TimeProvider,
            _logger);

        _outgoingTransfers.TryAdd(filePayload.Id, transfer);

        // The send and the completion await run as one task, so a failed hand-off to GMS flows
        // through the same shared catch ladder as a failed transfer.
        async Task SendThenAwaitCompletionAsync()
        {
            await Client.SendPayloadAsync(endpointId, filePayload).ConfigureAwait(false);
            await transfer.Completion.ConfigureAwait(false);
        }

        try
        {
            await transfer.AwaitAsync(
                SendThenAwaitCompletionAsync(),
                cancelPlatformTransfer: () => _ = CancelPayloadLoggedAsync(nameof(SendFileAsync), deviceId, filePayload.Id),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _outgoingTransfers.TryRemove(filePayload.Id, out _);
            transfer.Dispose();
            filePayload.Close();
            filePayload.Dispose();
        }
    }

    internal Task CancelInboundTransferAsync(string deviceId, long payloadId)
        => _incomingPayloads.TryGetValue(payloadId, out var entry) && entry.DeviceId == deviceId
            ? CancelPayloadLoggedAsync(nameof(CancelInboundTransferAsync), deviceId, payloadId)
            : Task.CompletedTask;

    async Task CancelPayloadLoggedAsync(
        string operation,
        string deviceId,
        long payloadId)
    {
        try
        {
            await Client.CancelPayloadAsync(payloadId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NearbyLog.EventWriteFailed(_logger, operation, deviceId, ex);
        }
    }

    Payload? BuildFilePayload(AndroidUri uri, string? fileName)
    {
        try
        {
            var parcelFileDescriptor = Platform.AppContext.ContentResolver?.OpenFileDescriptor(uri, "r");
            var payload = parcelFileDescriptor is not null
                ? Payload.FromFile(parcelFileDescriptor)
                : null;
            payload?.SetFileName(string.IsNullOrWhiteSpace(fileName)
                ? ResolveResourceName(uri)
                : Path.GetFileName(fileName));
            payload?.SetSensitive(true);

            return payload;
        }
        catch (Exception ex)
        {
            NearbyLog.FilePayloadBuildFailed(_logger, ex);
        }

        return null;
    }

    public async Task<NearbyAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        // Worst-first, and the order is the contract: NearbyAvailability is ordered by severity, so
        // this reports the one thing a user should fix first rather than a set they have to rank
        // themselves. Wi-Fi is last because it does not block a start at all — Bluetooth alone
        // still carries a connection, just slowly.
        var context = Platform.AppContext;

        cancellationToken.ThrowIfCancellationRequested();

        if (GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(context) != ConnectionResult.Success)
        {
            return NearbyAvailability.PlayServicesUnavailable;
        }

        if (await new NearbyPermission().CheckStatusAsync().ConfigureAwait(false) != PermissionStatus.Granted)
        {
            return NearbyAvailability.MissingPermissions;
        }

        try
        {
            using var bluetoothManager = (Android.Bluetooth.BluetoothManager?)context.GetSystemService(Context.BluetoothService);

            if (bluetoothManager?.Adapter is { } adapter && !adapter.IsEnabled)
            {
                return NearbyAvailability.BluetoothDisabled;
            }
        }
        catch (Exception ex)
        {
            // A probe that throws is reported and skipped, not treated as a blocker: failing to
            // read the radio's state is not evidence the radio is off.
            NearbyLog.AvailabilityCheckIncomplete(_logger, nameof(NearbyAvailability.BluetoothDisabled), ex);
        }

        try
        {
            using var wifiManager = (Android.Net.Wifi.WifiManager?)context.GetSystemService(Context.WifiService);

            if (wifiManager is not null && !wifiManager.IsWifiEnabled)
            {
                return NearbyAvailability.WifiDisabled;
            }
        }
        catch (Exception ex)
        {
            NearbyLog.AvailabilityCheckIncomplete(_logger, nameof(NearbyAvailability.WifiDisabled), ex);
        }

        return NearbyAvailability.Available;
    }

    public void ReleaseConnection(string deviceId)
    {
        // Runs after ReleaseConnectionAsync drains this peer's work queue, so no copy is still
        // reading these.
        foreach (var (payloadId, entry) in _incomingPayloads)
        {
            if (entry.DeviceId == deviceId
                && _incomingPayloads.TryRemove(payloadId, out var removed))
            {
                DisposeIncomingPayload(removed.Payload);
            }
        }

        foreach (var stream in _streamPairing.Release(deviceId))
        {
            try
            {
                stream.Dispose();
            }
            catch (Exception ex)
            {
                NearbyLog.EventWriteFailed(_logger, nameof(ReleaseConnection), deviceId, ex);
            }
        }
    }

    public void SweepStaging() => _transport.Staging.Sweep(InboundStaging.Root);

    public void Dispose()
    {
        foreach (var (_, entry) in _incomingPayloads)
        {
            DisposeIncomingPayload(entry.Payload);
        }
        _incomingPayloads.Clear();

        foreach (var stream in _streamPairing.Clear())
        {
            stream.Dispose();
        }

        foreach (var (_, transfer) in _outgoingTransfers)
        {
            transfer.Dispose();
        }
        _outgoingTransfers.Clear();

        _client?.Dispose();
        _client = null;

        lock (_availabilityGate)
        {
            _availabilityWatcher?.Dispose();
            _availabilityWatcher = null;
            _availabilityChanged = null;
        }
    }

    // The adapter's boundary guard: nothing thrown inside a GMS callback may escape into the SDK's
    // thread, so every failure is logged against the peer and swallowed here.
    void Callback(string name, string endpointId, Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            NearbyLog.PlatformCallbackFailed(_logger, name, _transport.PeerLookup.DeviceIdFor(endpointId), ex);
        }
    }

    // The mapping lives here, not on NearbyOptions: which SDK constant a neutral value means is
    // this adapter's fact, and it keeps Android.Gms.Nearby.Connection.Strategy out of the folder a
    // consumer configures from.
    static Strategy ToStrategy(NearbyTopology topology) => topology switch
    {
        NearbyTopology.Star => Strategy.P2pStar,
        NearbyTopology.PointToPoint => Strategy.P2pPointToPoint,
        _ => Strategy.P2pCluster,
    };

    // See ToStrategy for why the mapping lives here.
    static int ToConnectionType(NearbyConnectionType connectionType) => connectionType switch
    {
        NearbyConnectionType.HighBandwidth => ConnectionType.Disruptive,
        NearbyConnectionType.NonDisruptive => ConnectionType.NonDisruptive,
        _ => ConnectionType.Balanced,
    };

    static NearbyTransferStatus ToNearbyTransferStatus(int androidStatus) => androidStatus switch
    {
        PayloadTransferUpdate.Status.InProgress => NearbyTransferStatus.InProgress,
        PayloadTransferUpdate.Status.Success => NearbyTransferStatus.Completed,
        PayloadTransferUpdate.Status.Failure => NearbyTransferStatus.Failed,
        PayloadTransferUpdate.Status.Canceled => NearbyTransferStatus.Canceled,
        _ => NearbyTransferStatus.InProgress
    };

    sealed class LifecycleCallback(AndroidAdapter adapter) : ConnectionLifecycleCallback
    {
        public override async void OnConnectionInitiated(string p0, ConnectionInfo p1)
        {
            try
            {
                await adapter.OnConnectionInitiatedAsync(p0, p1).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NearbyLog.PlatformCallbackFailed(
                    adapter._logger,
                    nameof(ConnectionLifecycleCallback.OnConnectionInitiated),
                    adapter._transport.PeerLookup.DeviceIdFor(p0),
                    ex);
            }
        }

        public override void OnConnectionResult(string p0, ConnectionResolution p1)
            => adapter.OnConnectionResult(p0, p1);

        public override void OnDisconnected(string p0)
            => adapter.OnDisconnected(p0);
    }

    sealed class DiscoveryCallback(AndroidAdapter adapter) : EndpointDiscoveryCallback
    {
        public override void OnEndpointFound(string p0, DiscoveredEndpointInfo p1)
            => adapter.OnEndpointFound(p0, p1);

        public override void OnEndpointLost(string p0)
            => adapter.OnEndpointLost(p0);
    }

    sealed class InboundPayloadCallback(AndroidAdapter adapter) : PayloadCallback
    {
        public override void OnPayloadReceived(string p0, Payload p1)
            => adapter.OnPayloadReceived(p0, p1);

        public override void OnPayloadTransferUpdate(string p0, PayloadTransferUpdate p1)
            => adapter.OnPayloadTransferUpdate(p0, p1);
    }
}
