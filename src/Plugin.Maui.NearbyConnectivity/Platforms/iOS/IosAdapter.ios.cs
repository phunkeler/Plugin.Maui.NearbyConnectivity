using Security;

namespace Plugin.Maui.NearbyConnectivity;

[SuppressMessage(
    "Usage",
    "CA2213:Disposable fields should be disposed",
    Justification = "The NearbyTransport in _transport owns this adapter, not the reverse.")]
sealed class IosAdapter(NearbyTransport transport) : IPlatformAdapter
{
    const double ProgressLogStep = 0.5;
    const string InfoKey = "info";
    const char KeySeparator = '\u001F';

    // A relay that learns this side's nonce and then aborts gets another one-in-a-million try, and
    // under automatic acceptance nobody sees it retry. Spent exchanges are counted per idle span, not
    // per device: a relay can return under a fresh MCPeerID, which is a fresh device id.
    const int MaxSpentCodeExchanges = 3;

    // How long a start waits for MPC's failure delegate before it is treated as a success.
    static readonly TimeSpan s_startFailureGraceWindow = TimeSpan.FromMilliseconds(250);

    readonly NearbyTransport _transport = transport;
    readonly ILogger _logger = transport.Logger;
    readonly ConcurrentDictionary<string, (IDisposable Observer, NSProgress Progress)> _progressObservers = [];
    readonly ConcurrentDictionary<string, long> _inboundTransferIds = [];
    readonly Lock _sessionLock = new();
    readonly Lock _suspendedGate = new();

    // The local certificate's fingerprint dies with the MCSession that minted it; a peer's lives as
    // long as that peer's MPC link, because a reconnect over a live link runs no new certificate
    // exchange. The code exchange lives for one handshake: a reconnect runs a fresh one, which is
    // what gives every connection its own code.
    readonly ConcurrentDictionary<string, byte[]> _remoteFingerprints = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, (Task Handshake, AuthenticationCodeExchange Exchange)> _codeExchanges = new(StringComparer.Ordinal);
    byte[]? _localFingerprint;

    MCNearbyServiceAdvertiser? _mcAdvertiser;
    MCNearbyServiceBrowser? _mcBrowser;
    MCSession? _session;
    MCPeerID? _localPeerId;
    int _spentCodeExchanges;
    long _nextPayloadId;

    AppLifecycleObserver? _lifecycleObserver;
    Action? _suspended;

    public event Action? AvailabilityChanged
    {
        add { }
        remove { }
    }

    public event Action? Suspended
    {
        add
        {
            lock (_suspendedGate)
            {
                _suspended += value;
                _lifecycleObserver ??= new AppLifecycleObserver(() => _suspended?.Invoke(), _logger);
            }
        }
        remove
        {
            lock (_suspendedGate)
            {
                _suspended -= value;
            }
        }
    }

    static ReadOnlyMemory<byte> DecodeInfo(NSDictionary? info)
    {
        if (info?[InfoKey]?.ToString() is not { Length: > 0 } encoded)
        {
            return default;
        }

        var decoded = new byte[NearbyPresence.MaxInfoBytes];

        return Convert.TryFromBase64String(encoded, decoded, out var written)
            ? decoded.AsMemory(0, written)
            : default;
    }

    internal MCPeerID GetLocalPeerId()
    {
        lock (_sessionLock)
        {
            if (_localPeerId is null)
            {
                NearbyLog.LocalPeerCreated(_logger);
            }

            return _localPeerId ??= new MCPeerID(_transport.Presence.DisplayName);
        }
    }

    public async Task StartAdvertisingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var myPeerId = GetLocalPeerId();
        var presenceInfo = _transport.Presence.Info;

        using var discoveryInfo = presenceInfo.IsEmpty
            ? null
            : NSDictionary.FromObjectAndKey(
                (NSString)Convert.ToBase64String(presenceInfo.Span),
                (NSString)InfoKey);

        _mcAdvertiser = new MCNearbyServiceAdvertiser(
            myPeerID: myPeerId,
            info: discoveryInfo,
            serviceType: _transport.Options.ServiceId)
        {
            Delegate = new AdvertiserDelegate(this)
        };

        _mcAdvertiser.StartAdvertisingPeer();

        await AwaitStartFailureGraceWindowAsync(_transport.AdvertiseChannelCompletion, cancellationToken)
            .ConfigureAwait(false);
    }

    // Rethrows a fault arriving within the grace window. A fault arriving after it — including the
    // TimeoutException this method swallows — is left on the channel for the pump to observe as a
    // logged, post-start failure.
    async Task AwaitStartFailureGraceWindowAsync(Task channelCompletion, CancellationToken cancellationToken)
    {
        try
        {
            await channelCompletion.WaitAsync(s_startFailureGraceWindow, _transport.TimeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Started successfully as far as this window can tell; a later fault takes the logged path.
        }
    }

    public void StopAdvertising()
    {
        _mcAdvertiser?.StopAdvertisingPeer();
        _mcAdvertiser?.Dispose();
        _mcAdvertiser = null;
    }

    internal void DidNotStartAdvertisingPeer(MCNearbyServiceAdvertiser advertiser, NSError error)
    {
        var exception = new NearbyException($"Failed to start advertising. {error.LocalizedDescription}", new NSErrorException(error));

        NearbyLog.AdvertisingStartFailed(_logger, exception);

        if (!_transport.TryFaultAdvertiseChannel(exception))
        {
            NearbyLog.AdvertisingStartFaultDropped(_logger, exception);
        }
    }

    internal void DidNotStartBrowsingForPeers(MCNearbyServiceBrowser browser, NSError error)
    {
        var exception = new NearbyException($"Failed to start discovery. {error.LocalizedDescription}", new NSErrorException(error));

        NearbyLog.DiscoveryStartFailed(_logger, exception);

        if (!_transport.TryFaultDiscoverChannel(exception))
        {
            NearbyLog.DiscoveryStartFaultDropped(_logger, exception);
        }
    }

    internal void DidReceiveInvitationFromPeer(
        MCNearbyServiceAdvertiser advertiser,
        MCPeerID peerID,
        NSData? context,
        MCNearbyServiceAdvertiserInvitationHandler invitationHandler)
        => Callback(
            nameof(DidReceiveInvitationFromPeer),
            peerID,
            () => OfferConnection(peerID, context?.ToArray(), invitationHandler.Invoke));

    void OfferConnection(MCPeerID peerID, byte[]? frame, Action<bool, MCSession?> respond)
    {
        var framed = ControlMessage.TryDecodeConnectRequest(
            frame ?? [], out var declaredWindow, out var name, out var info);

        var device = framed
            ? _transport.PeerLookup.Record(_transport.PeerLookup.DeviceIdFor(peerID), name, info)
            : _transport.PeerLookup.Record(peerID);

        var id = device.Id;

        if (!framed)
        {
            declaredWindow = OfferWindowPolicy.s_default;
            NearbyLog.RequestFrameMissing(_logger, id);
        }

        NearbyLog.RequestReceived(_logger, id);

        var pending = respond;

        void Respond(bool accept, MCSession? session)
            => Interlocked.Exchange(ref pending, null)?.Invoke(accept, session);

        var request = _transport.CreateInboundRequest(
            device,
            declaredWindow,
            sendAccept: async () => Respond(true, await EnsureSessionAsync().ConfigureAwait(false)),
            sendReject: () =>
            {
                Respond(false, null);

                return Task.CompletedTask;
            },
            onAcceptFailed: () =>
            {
                Respond(false, null);
            });

        _transport.WriteConnectionRequest(request);
    }

    public async Task StartDiscoveryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var myPeerId = GetLocalPeerId();

        _mcBrowser = new MCNearbyServiceBrowser(
            myPeerID: myPeerId,
            serviceType: _transport.Options.ServiceId)
        {
            Delegate = new BrowserDelegate(this)
        };

        _mcBrowser.StartBrowsingForPeers();

        await AwaitStartFailureGraceWindowAsync(_transport.DiscoverChannelCompletion, cancellationToken)
            .ConfigureAwait(false);
    }

    public void StopDiscovering()
    {
        _mcBrowser?.StopBrowsingForPeers();
        _mcBrowser?.Dispose();
        _mcBrowser = null;
    }

    internal void FoundPeer(MCNearbyServiceBrowser browser, MCPeerID peerID, NSDictionary? info)
        => Callback(nameof(FoundPeer), peerID, () =>
        {
            var device = _transport.PeerLookup.Record(peerID, DecodeInfo(info));
            _transport.OnDeviceFound(device);
        });

    internal void LostPeer(MCNearbyServiceBrowser browser, MCPeerID peerID)
        => Callback(nameof(LostPeer), peerID, ()
            => _transport.OnDeviceLost(_transport.PeerLookup.DeviceIdFor(peerID)));

    public async Task InitiateConnectAsync(NearbyDevice device, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_transport.PeerLookup.TryGetPeerId(device.Id, out var peerID))
        {
            NearbyLog.PeerNotFound(_logger, device.Id);
            _transport.FaultConnectionTcs(device.Id, new NearbyConnectException(
                NearbyConnectFailure.DeviceUnknown,
                $"Cannot connect: device '{device.Id}' is not currently visible. Ensure it is actively advertising and within range."));
            return;
        }

        if (_mcBrowser is not { } browser)
        {
            _transport.FaultConnectionTcs(device.Id, new NearbyConnectException(
                NearbyConnectFailure.DeviceUnknown,
                $"Cannot connect: device '{device.Id}' is not currently visible. Ensure it is actively advertising and within range."));
            return;
        }

        var session = await EnsureSessionAsync().ConfigureAwait(false);
        var frame = ControlMessage.EncodeConnectRequest(_transport.Options.OfferWindow, _transport.Presence);

        if (Array.Exists(session.ConnectedPeers, peerID.Equals))
        {
            NearbyLog.ConnectRequestOverLiveLink(_logger, device.Id);

            if (!TrySendControl(peerID, frame))
            {
                _transport.FaultConnectionTcs(device.Id, new NearbyConnectException(
                    NearbyConnectFailure.Failed,
                    $"Failed to initiate connection to device '{device.Id}': the existing link refused the request."));
            }

            return;
        }

        using var context = NSData.FromArray(frame);

        try
        {
            browser.InvitePeer(peerID, session, context, ToInvitationTimeout(_transport.Options.OfferWindow));
        }
        catch (Exception ex)
        {
            _transport.FaultConnectionTcs(device.Id, new NearbyConnectException(
                NearbyConnectFailure.Failed,
                $"Failed to initiate connection to device '{device.Id}'.", ex));
        }
    }

    static double ToInvitationTimeout(TimeSpan connectTimeout)
        => OfferWindowPolicy.Clamp(connectTimeout).TotalSeconds;

    public Task AbandonConnectAsync(NearbyDevice device) => Task.CompletedTask;

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "An out NSError is a borrowed autoreleased object; its managed wrapper holds no " +
            "resource that needs early release.")]
    internal Task SendBytesAsync(
        string deviceId,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (session, peerID) = RequireLiveSession(deviceId, "send bytes");

        // NSData.FromArray binds byte[], so the copy happens here rather than at the public boundary.
        using var nsData = NSData.FromArray(bytes.ToArray());
        session.SendData(nsData, [peerID], MCSessionSendDataMode.Reliable, out var error);

        if (error is not null)
        {
            var nsErrorException = new NSErrorException(error);

            NearbyLog.SendBytesFailed(_logger, deviceId, nsErrorException);
            throw new NearbyTransferException($"Failed to send bytes to device '{deviceId}': {error.LocalizedDescription}", nsErrorException);
        }

        return Task.CompletedTask;
    }

    [SuppressMessage(
        "Reliability",
        "CA2025:Do not pass 'IDisposable' instances into unawaited tasks",
        Justification = "MultipeerConnectivity retains the URL and the session for the transfer; disposing " +
            "the managed wrappers releases only this side's reference.")]
    internal async Task SendFileAsync(
        string deviceId,
        string uri,
        string? fileName,
        IProgress<NearbyTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (session, peerID) = RequireLiveSession(deviceId, "send file");

        using var nsUrl = NSUrl.FromFilename(uri);
        var payloadId = Interlocked.Increment(ref _nextPayloadId);
        using var transfer = new OutgoingTransfer(
            deviceId,
            payloadId,
            progress,
            _transport.TransferInactivityTimeout,
            _transport.TimeProvider,
            _logger);
        var resourceName = string.IsNullOrWhiteSpace(fileName)
            ? nsUrl.LastPathComponent ?? Path.GetFileName(uri)
            : Path.GetFileName(fileName);
        var sendTask = session.SendResourceAsync(nsUrl, resourceName, peerID, out var nsProgress);

        var observer = nsProgress is null
            ? null
            : ObserveProgress(nsProgress, deviceId, "Outbound", payloadId, transfer.OnUpdate, nameof(SendFileAsync));

        try
        {
            await transfer.AwaitAsync(
                CanceledByPeerOrPlatform(sendTask, transfer, cancellationToken),
                cancelPlatformTransfer: () => nsProgress?.Cancel(),
                cancellationToken).ConfigureAwait(false);

            transfer.Complete(NearbyTransferStatus.Completed);
        }
        finally
        {
            observer?.Dispose();
            nsProgress?.Dispose();
        }
    }

    // MPC reports a cancel by either end as a failed send. Reported as Canceled, so the sender sees
    // NearbyTransferCanceledException as on Android. A cancel this side requested, by the caller's
    // token or the inactivity timeout, keeps the outcome AwaitAsync gives it.
    static async Task CanceledByPeerOrPlatform(
        Task sendTask,
        OutgoingTransfer transfer,
        CancellationToken cancellationToken)
    {
        try
        {
            await sendTask.ConfigureAwait(false);
        }
        catch (NSErrorException ex) when (IsCancel(ex.Error)
            && !cancellationToken.IsCancellationRequested
            && !transfer.InactivityToken.IsCancellationRequested)
        {
            transfer.Complete(NearbyTransferStatus.Canceled);
            throw new OperationCanceledException(ex.Message, ex);
        }
    }

    static bool IsCancel(NSError error)
        => error.Domain == MCError.Cancelled.GetDomain()
            && error.Code == (nint)(long)MCError.Cancelled;

    public Task<NearbyAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var failures = new List<string>();
        ServiceIdRules.Validate(_transport.Options.ServiceId, suggestion: null, failures);

        if (failures.Count > 0)
        {
            NearbyLog.ServiceIdInvalid(_logger, _transport.Options.ServiceId, string.Join(" ", failures));
            return Task.FromResult(NearbyAvailability.InvalidConfiguration);
        }

        var warnings = new List<string>();
        InfoPlistRules.Validate(_transport.Options.ServiceId, failures, warnings);

        if (warnings.Count > 0)
        {
            NearbyLog.InfoPlistSuspect(_logger, string.Join(" ", warnings));
        }

        if (failures.Count > 0)
        {
            NearbyLog.InfoPlistIncomplete(_logger, string.Join(" ", failures));
            return Task.FromResult(NearbyAvailability.InvalidConfiguration);
        }

        return Task.FromResult(NearbyAvailability.Available);
    }

    public void SweepStaging() => _transport.Staging.Sweep(InboundStaging.Root);

    public void Dispose()
    {
        lock (_suspendedGate)
        {
            _lifecycleObserver?.Dispose();
            _lifecycleObserver = null;
            _suspended = null;
        }

        StopAdvertising();
        StopDiscovering();

        foreach (var (_, entry) in _progressObservers)
        {
            entry.Observer.Dispose();
        }
        _progressObservers.Clear();
        _inboundTransferIds.Clear();
        _transport.PeerLookup.Clear();

        // Unconditional here: disposal ends the span whatever the transport thinks is live.
        ReleaseIdleState();
    }

    internal void OnPeerStateChanged(MCPeerID peerID, MCSessionState state)
        => Callback(nameof(OnPeerStateChanged), peerID, () =>
        {
            var id = _transport.PeerLookup.DeviceIdFor(peerID);

            NearbyLog.PeerStateChanged(_logger, id, state);

            switch (state)
            {
                case MCSessionState.Connected:
                    StartCodeExchange(peerID, id);
                    break;

                case MCSessionState.NotConnected:
                    // NotConnected is MPC's terminal state for both a dropped connection and a
                    // handshake that never completed, and only the first means the peer is gone.
                    // A declined or failed invitation leaves the browsed MCPeerID valid — no
                    // LostPeer follows, and the same handle takes the next invite — so forgetting
                    // it would strand a Visible row that can never connect again.
                    _remoteFingerprints.TryRemove(id, out _);
                    ForgetCodeExchange(id);
                    _transport.ReleaseConnectionFromCallback(id, forgetPeer: _transport.Connections.IsConnected(id));
                    _transport.FaultConnectionTcs(id, new NearbyConnectException(
                        NearbyConnectFailure.Failed,
                        $"Connection to device '{id}' failed: session state changed to NotConnected before the connection was established."));

                    break;

                case MCSessionState.Connecting:
                    break;
            }
        });

    // MPC hands over the peer's certificate chain between Connecting and Connected, so the
    // fingerprint is in place before the code exchange that reads it can start.
    internal void OnCertificateReceived(SecCertificate[]? chain, MCPeerID peerID)
        => Callback(nameof(OnCertificateReceived), peerID, () =>
        {
            var id = _transport.PeerLookup.DeviceIdFor(peerID);

            // One certificate per link. A new link always follows NotConnected, which clears the
            // entry; a different certificate on a live link voids the binding, so neither one is
            // trusted and the connection reports no code.
            if (chain is { Length: > 0 })
            {
                var fingerprint = EphemeralIdentity.Fingerprint(chain[0]);
                var stored = _remoteFingerprints.AddOrUpdate(
                    id,
                    fingerprint,
                    (_, existing) => existing.AsSpan().SequenceEqual(fingerprint) ? existing : []);

                if (stored.Length == 0)
                {
                    NearbyLog.CertificateChanged(_logger, id);
                }
            }
        });

    // The exchange for the handshake now in flight, or null when none is, so a stray or late frame
    // cannot complete a handshake nobody registered. Keyed to the handshake's task: an exchange left
    // behind by an earlier handshake on the same live link is replaced, never reused. The binding
    // is read here, once, before the exchange exists to send a commitment.
    AuthenticationCodeExchange? CodeExchangeFor(string deviceId)
    {
        if (!_transport.Handshakes.TryJoin(deviceId, out var handshake))
        {
            return null;
        }

        if (Volatile.Read(ref _spentCodeExchanges) >= MaxSpentCodeExchanges)
        {
            throw new NearbyConnectException(
                NearbyConnectFailure.Failed,
                "The authentication code exchange was refused: too many exchanges failed after this device revealed its nonce. Stop advertising and discovery and close every connection, then start again.");
        }

        return _codeExchanges.AddOrUpdate(
            deviceId,
            static (_, arg) => (arg.Handshake, new AuthenticationCodeExchange(arg.Binding)),
            static (_, current, arg) => current.Handshake == arg.Handshake
                ? current
                : (arg.Handshake, new AuthenticationCodeExchange(arg.Binding)),
            (Handshake: (Task)handshake, Binding: LinkBinding(deviceId))).Exchange;
    }

    byte[]? LinkBinding(string deviceId)
        => _localFingerprint is { } local
            && _remoteFingerprints.TryGetValue(deviceId, out var remote)
            && remote.Length > 0
                ? AuthenticationCodeRules.Unordered(local, remote)
                : null;

    void StartCodeExchange(MCPeerID peerID, string deviceId)
    {
        try
        {
            CodeExchangeFor(deviceId)?.Commit(next => TrySendControl(peerID, next));
        }
        catch (Exception ex)
        {
            FailCodeExchange(deviceId, ex);
        }
    }

    void FailCodeExchange(string deviceId, Exception ex)
    {
        ForgetCodeExchange(deviceId);
        NearbyLog.CodeExchangeFailed(_logger, deviceId, ex);
        _transport.FaultConnectionTcs(deviceId, ex);
    }

    void FailPendingCodeExchange(string deviceId)
    {
        if (_codeExchanges.ContainsKey(deviceId))
        {
            FailCodeExchange(deviceId, new NearbyConnectException(
                NearbyConnectFailure.Failed,
                $"The authentication code exchange with device '{deviceId}' failed: the device sent a control frame this version cannot read."));
        }
    }

    // An exchange that ends without completing, after this side revealed, handed its nonce to
    // whoever is on the other end: that is one spent attempt.
    void ForgetCodeExchange(string deviceId)
    {
        if (_codeExchanges.TryRemove(deviceId, out var entry)
            && entry.Exchange.Revealed
            && Interlocked.Increment(ref _spentCodeExchanges) == MaxSpentCodeExchanges)
        {
            NearbyLog.CodeExchangesExhausted(_logger, MaxSpentCodeExchanges);
        }
    }

    // Every iOS connection surfaces here, on the peer's valid reveal, and nowhere else.
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The transport's connection table owns the IosConnection and disposes it on release.")]
    void OnCodeFrame(MCPeerID peerID, string deviceId, ControlMessageType type, byte[] frame)
    {
        try
        {
            var exchange = CodeExchangeFor(deviceId)
                ?? throw new NearbyConnectException(
                    NearbyConnectFailure.Failed,
                    $"The authentication code exchange failed: no connection to device '{deviceId}' is being established.");

            if (type == ControlMessageType.CodeCommit)
            {
                exchange.AnswerCommit(ControlMessage.Body(frame), next => TrySendControl(peerID, next));
                return;
            }

            var code = exchange.Open(ControlMessage.Body(frame));
            _codeExchanges.TryRemove(deviceId, out _);

            if (code is null)
            {
                NearbyLog.AuthenticationCodeMissing(_logger, deviceId);
            }

            _transport.CompleteHandshake(
                _transport.PeerLookup.Record(peerID),
                new IosConnection(this, deviceId),
                code);
        }
        catch (Exception ex)
        {
            FailCodeExchange(deviceId, ex);
        }
    }

    internal void OnDataReceived(NSData data, MCPeerID peerID)
        => Callback(nameof(OnDataReceived), peerID, () =>
        {
            var id = _transport.PeerLookup.DeviceIdFor(peerID);

            NearbyLog.DataReceived(_logger, id, (long)data.Length);

            var bytes = data.ToArray();

            if (ControlMessage.TryDecode(bytes, out var controlType))
            {
                NearbyLog.ControlFrameReceived(_logger, id, controlType);
                HandleControlMessage(peerID, id, controlType, bytes);
                return;
            }

            if (ControlMessage.HasSignature(bytes))
            {
                NearbyLog.ControlFrameUnreadable(_logger, id, bytes.Length);
                FailPendingCodeExchange(id);
                return;
            }

            var payload = new NearbyBytesPayload(bytes);
            _transport.Connections.WritePayload(id, payload);
        });

    void HandleControlMessage(MCPeerID peerID, string deviceId, ControlMessageType type, byte[] frame)
    {
        switch (type)
        {
            case ControlMessageType.Disconnect:
                NearbyLog.PeerAnnouncedDisconnect(_logger, deviceId);
                _transport.ReleaseConnectionFromCallback(deviceId, forgetPeer: false);
                break;

            case ControlMessageType.ConnectRequest:
                OfferConnection(peerID, frame, respond: (accept, _) =>
                {
                    if (!accept)
                    {
                        TrySendControl(peerID, ControlMessage.Encode(ControlMessageType.ConnectRejected));
                        return;
                    }

                    if (!TrySendControl(peerID, ControlMessage.Encode(ControlMessageType.ConnectAccepted)))
                    {
                        throw new NearbyConnectException(
                            NearbyConnectFailure.Failed,
                            $"Connection to device '{deviceId}' failed: the accept could not be sent over the existing link.");
                    }

                    StartCodeExchange(peerID, deviceId);
                });
                break;

            case ControlMessageType.ConnectAccepted:
                StartCodeExchange(peerID, deviceId);
                break;

            case ControlMessageType.CodeCommit:
            case ControlMessageType.CodeReveal:
                OnCodeFrame(peerID, deviceId, type, frame);
                break;

            case ControlMessageType.ConnectRejected:
                _transport.FaultConnectionTcs(deviceId, new NearbyConnectException(
                    NearbyConnectFailure.Failed,
                    $"Connection to device '{deviceId}' failed: the request was declined."));
                break;

            default:
                NearbyLog.ControlFrameUnknown(_logger, deviceId, type);
                break;
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "An out NSError is a borrowed autoreleased object; its managed wrapper holds no " +
            "resource that needs early release.")]
    bool TrySendControl(MCPeerID peerID, byte[] frame)
    {
        MCSession? session;

        lock (_sessionLock)
        {
            session = _session;
        }

        if (session is null)
        {
            return false;
        }

        using var data = NSData.FromArray(frame);
        session.SendData(data, [peerID], MCSessionSendDataMode.Reliable, out var error);

        return error is null;
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "An out NSError is a borrowed autoreleased object; its managed wrapper holds no " +
            "resource that needs early release.")]
    internal Task<Stream> OpenStreamAsync(string deviceId, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (session, peerID) = RequireLiveSession(deviceId, "open a stream");

        var stream = session.StartStream(name, peerID, out var error);

        if (stream is null || error is not null)
        {
            Exception cause = error is null
                ? new InvalidOperationException("StartStream returned no stream.")
                : new NSErrorException(error);
            NearbyLog.EventWriteFailed(_logger, nameof(OpenStreamAsync), deviceId, cause);

            throw new NearbyTransferException(
                $"Failed to open a stream to device '{deviceId}'.", cause);
        }

        return Task.FromResult<Stream>(new NsOutputStreamAdapter(stream));
    }

    // The one MCSession this adapter uses, shared across every peer, created on first need. Both
    // the initiating and the accepting path need it and either may run first, so the construction
    // is shared: a divergence in delegate wiring between two build sites would make the session's
    // configuration depend on which side happened to connect first.
    //
    // The identity is minted off the caller's thread: an RSA-2048 keygen and a certificate signature
    // on the first connect or accept of a span would otherwise run on whatever thread called
    // ConnectAsync or AcceptAsync, which in a page is the UI thread. Two first callers may both
    // mint; the loser's identity is discarded.
    [SuppressMessage(
        "Maintainability",
        "CA1508:Avoid dead conditional code",
        Justification = "Another caller can create the session while this one awaits the identity.")]
    async Task<MCSession> EnsureSessionAsync()
    {
        lock (_sessionLock)
        {
            if (_session is not null)
            {
                return _session;
            }
        }

        var identity = await Task.Run(EphemeralIdentity.Create).ConfigureAwait(false);

        lock (_sessionLock)
        {
            if (_session is not null)
            {
                identity.Dispose();
                return _session;
            }

            _localFingerprint = EphemeralIdentity.Fingerprint(identity.Certificate);

            return _session = new MCSession(
                GetLocalPeerId(),
                identity,
                MCEncryptionPreference.Required)
            {
                Delegate = new SessionDelegate(this)
            };
        }
    }

    // The connection check comes FIRST and is the transport's, not this file's: _session is one
    // handle shared by every peer, so it stays non-null while any other peer is connected and
    // cannot answer "is THIS device still connected".
    (MCSession Session, MCPeerID PeerID) RequireLiveSession(string deviceId, string operation)
    {
        _transport.Connections.RequireConnected(deviceId, operation);

        MCSession? session;

        lock (_sessionLock)
        {
            session = _session;
        }

        if (session is null || !_transport.PeerLookup.TryGetPeerId(deviceId, out var peerID))
        {
            throw new NearbyException(
                $"Cannot {operation}: no active connection for device '{deviceId}'.");
        }

        return (session, peerID);
    }

    internal void OnStreamReceived(string streamName, MCPeerID fromPeer, NSInputStream stream)
        => Callback(nameof(OnStreamReceived), fromPeer, () =>
        {
            var id = _transport.PeerLookup.DeviceIdFor(fromPeer);
            var name = NearbyText.Truncate(streamName, ControlMessage.MaxStreamNameBytes);

#pragma warning disable NEARBY001 // The plugin delivers its own experimental type; consumers opt in.
            _transport.Connections.WritePayload(
                id, new NearbyStreamPayload(name, new NsInputStreamAdapter(stream)));
#pragma warning restore NEARBY001
        });

    // The local-disconnect path for one peer. Sends the in-band disconnect frame because MPC has
    // no per-peer disconnect.
    internal async ValueTask DisconnectPeerAsync(string deviceId)
    {
        if (_transport.PeerLookup.TryGetPeerId(deviceId, out var peer))
        {
            TrySendControl(peer, ControlMessage.Encode(ControlMessageType.Disconnect));
        }

        // A local close leaves the device discovered and reconnectable, so the peer entry stays.
        await _transport.ReleaseConnectionAsync(deviceId, forgetPeer: false).ConfigureAwait(false);
    }

    // Retires the shared session and the local peer. The transport decides when — it owns the run
    // count and both tables — so this only releases what it is told to.
    //
    // The peer, the session, the browser and the advertiser share ONE lifetime. MPC will not carry
    // a fresh MCSession on a peer a disposed one was built on — the browser logs "Failed to send
    // dictionary" and reports NotConnected at once — and a browser rebuilt on a new peer is a new
    // browse, which hands every remote peer back as a new instance with a new device id, so
    // consumers see every row removed and re-added. Disposing the session while either is running
    // breaks the next invite one way or the other; the session outlives a failed handshake and is
    // reused for the next.
    //
    // Retiring the session while a browser or advertiser still runs breaks the next invite: the
    // request reaches the advertiser and expires, while the retry on this side is rejected before
    // it leaves the device.
    public void ReleaseIdleState()
    {
        MCSession? sessionToDispose;
        MCPeerID? localPeerToDispose;

        lock (_sessionLock)
        {
            sessionToDispose = _session;
            localPeerToDispose = _localPeerId;
            _session = null;
            _localPeerId = null;

            // The identity dies with the session. Leaving the fingerprint behind would derive the
            // next session's codes from a retired certificate, and the two phones would disagree.
            _localFingerprint = null;
            _remoteFingerprints.Clear();
            _codeExchanges.Clear();
            _spentCodeExchanges = 0;
        }

        if (sessionToDispose is not null)
        {
            NearbyLog.PlatformSessionReleased(_logger);
            sessionToDispose.Disconnect();
            sessionToDispose.Dispose();
        }

        localPeerToDispose?.Dispose();
    }

    internal void OnResourceStarted(
        string resourceName,
        MCPeerID fromPeer,
        NSProgress progress)
    {
        try
        {
            var id = _transport.PeerLookup.DeviceIdFor(fromPeer);
            NearbyLog.FileReceiveStarted(_logger, id, resourceName);

            if (!_transport.Connections.IsConnected(id) && !_transport.Handshakes.TryJoin(id, out _))
            {
                NearbyLog.FileRefused(_logger, id);
                progress.Cancel();
                return;
            }

            var payloadId = Interlocked.Increment(ref _nextPayloadId);
            var observerKey = ObserverKey(id, payloadId);
            var observer = ObserveProgress(
                progress,
                id,
                "Inbound",
                payloadId,
                update => _transport.Connections.ReportInboundProgress(id, update),
                nameof(OnResourceStarted));

            _progressObservers[observerKey] = (observer, progress);
            _inboundTransferIds[InboundKey(id, resourceName)] = payloadId;
        }
        catch (Exception ex)
        {
            NearbyLog.PlatformCallbackFailed(_logger, nameof(OnResourceStarted), _transport.PeerLookup.DeviceIdFor(fromPeer), ex);
        }
    }

    static string ObserverKey(string deviceId, long payloadId)
        => $"{deviceId}{KeySeparator}{payloadId}";

    static string InboundKey(string deviceId, string resourceName)
        => $"{deviceId}{KeySeparator}{resourceName}";

    internal void CancelInboundTransfer(string deviceId, long payloadId)
    {
        if (_progressObservers.TryGetValue(ObserverKey(deviceId, payloadId), out var entry))
        {
            entry.Progress.Cancel();
        }
    }

    public void ReleaseConnection(string deviceId)
        => RemoveProgressObserversFor(deviceId);

    void RemoveProgressObserversFor(string deviceId)
    {
        var prefix = $"{deviceId}{KeySeparator}";

        foreach (var (key, _) in _progressObservers)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (_progressObservers.TryRemove(key, out var stale))
            {
                stale.Observer.Dispose();
            }
        }

        foreach (var (key, _) in _inboundTransferIds)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _inboundTransferIds.TryRemove(key, out _);
            }
        }
    }

    internal void OnResourceFinished(
        string resourceName,
        MCPeerID fromPeer,
        NSUrl? localUrl,
        NSError? error)
        => Callback(nameof(OnResourceFinished), fromPeer, () =>
        {
            var id = _transport.PeerLookup.DeviceIdFor(fromPeer);
            NearbyLog.FileReceiveFinished(_logger, id, resourceName, localUrl?.Path, error?.LocalizedDescription);

            // The finish callback carries only the resource name, so it needs this to find
            // the transfer's minted id. Last writer wins: with two live sends of one name the
            // second finish claims whichever entry remains, and the other observer is swept
            // by ReleaseConnection -- so both are still disposed exactly once.
            long? transferId = _inboundTransferIds.TryRemove(InboundKey(id, resourceName), out var payloadId) ? payloadId : null;

            var canceledHere = false;

            if (transferId is not null && _progressObservers.TryRemove(ObserverKey(id, payloadId), out var entry))
            {
                entry.Observer.Dispose();
                canceledHere = entry.Progress.Cancelled;
            }

            void Finish(NearbyTransferStatus status)
            {
                if (transferId is { } known)
                {
                    _transport.Connections.CompleteInboundTransfer(id, known, status);
                }
            }

            if (error is not null && !_transport.Connections.IsConnected(id))
            {
                return;
            }

            if (error is not null && canceledHere)
            {
                Finish(NearbyTransferStatus.Canceled);
                return;
            }

            if (error is not null)
            {
                NearbyLog.FileReceiveFailed(_logger, id, resourceName, "the platform reported an error", new NSErrorException(error));
                Finish(NearbyTransferStatus.Failed);
                return;
            }

            if (localUrl?.Path is not string sourcePath)
            {
                NearbyLog.FileReceiveFailed(_logger, id, resourceName, "the platform gave no file path", null);
                Finish(NearbyTransferStatus.Failed);
                return;
            }

            string destinationPath;

            // Deliberately synchronous. MultipeerConnectivity deletes its temp file once this
            // delegate returns, so the file must be consumed before then. A same-volume move is an
            // O(1) rename, which is why this costs nothing on the delegate's serial queue — and
            // that queue is also what keeps per-peer payload order. Making this async would break
            // both guarantees at once: re-introduce staging first.
            try
            {
                // The claim is held as a zero-byte file across the move: releasing it before the
                // move would reopen the race it exists to close, so overwrite it in place.
                using (var claim = InboundStaging.ClaimUniquePath(InboundStaging.Root, resourceName))
                {
                    destinationPath = claim.Name;
                }

                File.Move(sourcePath, destinationPath, overwrite: true);
            }
            catch (Exception ex)
            {
                NearbyLog.FileCopyFailed(_logger, sourcePath, InboundStaging.Root, ex);
                Finish(NearbyTransferStatus.Failed);
                return;
            }

            var payload = NearbyFilePayload.Staged(destinationPath);
            _transport.Connections.WritePayload(id, payload);

            // After the payload is routed, so a consumer that clears its progress indicator on
            // Completed never does so before the payload it announces exists.
            Finish(NearbyTransferStatus.Completed);
        });

    void Callback(string name, MCPeerID peerID, Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            NearbyLog.PlatformCallbackFailed(
                _logger,
                name,
                _transport.PeerLookup.DeviceIdFor(peerID),
                ex);
        }
    }

    // Every update reaches the consumer; only the log is thinned. MPC ticks fractionCompleted about
    // once per kilobyte, so an unthinned Trace line buries every other message in this category
    // while a file moves.
    IDisposable ObserveProgress(
        NSProgress progress,
        string deviceId,
        string direction,
        long payloadId,
        Action<NearbyTransferProgress> report,
        string callbackName)
    {
        var lastLoggedFraction = -1.0;

        return progress.AddObserver(
            "fractionCompleted",
            NSKeyValueObservingOptions.New,
            _ =>
            {
                try
                {
                    var total = progress.TotalUnitCount;
                    var fraction = progress.FractionCompleted;
                    var transferred = (long)(fraction * total);

                    if (ShouldLogProgress(fraction, ref lastLoggedFraction))
                    {
                        NearbyLog.FileTransferProgress(_logger, deviceId, direction, payloadId, total, transferred);
                    }

                    report(new NearbyTransferProgress(
                        payloadId,
                        bytesTransferred: transferred,
                        totalBytes: total,
                        NearbyTransferStatus.InProgress));
                }
                catch (Exception ex)
                {
                    NearbyLog.PlatformCallbackFailed(_logger, callbackName, deviceId, ex);
                }
            });
    }

    static bool ShouldLogProgress(double fraction, ref double lastLogged)
    {
        // Tolerance, not tidiness: an advance of exactly one step is not representable in binary
        // (0.20 + 0.5 - 0.20 == 0.49999999999999994), so a bare `< ProgressLogStep` drops the
        // update the caller just earned.
        if (fraction < 1.0 && fraction - lastLogged < ProgressLogStep - 1e-9)
        {
            return false;
        }

        lastLogged = fraction;
        return true;
    }

    sealed class AdvertiserDelegate(IosAdapter adapter) : NSObject, IMCNearbyServiceAdvertiserDelegate
    {
#pragma warning disable S1144, S1172
        public void DidNotStartAdvertisingPeer(MCNearbyServiceAdvertiser advertiser, NSError error)
            => adapter.DidNotStartAdvertisingPeer(advertiser, error);

        public void DidReceiveInvitationFromPeer(
            MCNearbyServiceAdvertiser advertiser,
            MCPeerID peerID,
            NSData? context,
            MCNearbyServiceAdvertiserInvitationHandler invitationHandler)
            => adapter.DidReceiveInvitationFromPeer(advertiser, peerID, context, invitationHandler);
#pragma warning restore S1144, S1172
    }

    sealed class BrowserDelegate(IosAdapter adapter) : NSObject, IMCNearbyServiceBrowserDelegate
    {
#pragma warning disable S1144, S1172
        public void FoundPeer(MCNearbyServiceBrowser browser, MCPeerID peerID, NSDictionary? info)
            => adapter.FoundPeer(browser, peerID, info);

        public void LostPeer(MCNearbyServiceBrowser browser, MCPeerID peerID)
            => adapter.LostPeer(browser, peerID);

        public void DidNotStartBrowsingForPeers(MCNearbyServiceBrowser browser, NSError error)
            => adapter.DidNotStartBrowsingForPeers(browser, error);
#pragma warning restore S1144, S1172
    }

    // Subclasses MCSessionDelegate rather than implementing IMCSessionDelegate on an NSObject:
    // DidReceiveCertificate declared on the interface implementation is never invoked, and the
    // binding types it as returning bool where the framework's selector returns void. Return true
    // AND call the handler — the handler is what MPC acts on.
    //
    // Always accepts: a self-signed ephemeral certificate carries no identity to validate against,
    // so rejecting could only ever mean rejecting everyone. It is the code the two people compare
    // that rules out a relay, not this callback.
    sealed class SessionDelegate(IosAdapter adapter) : MCSessionDelegate
    {
#pragma warning disable S1144, S1172
        public override bool DidReceiveCertificate(
            MCSession session,
            SecCertificate[]? certificate,
            MCPeerID peerID,
            Action<bool> certificateHandler)
        {
            adapter.OnCertificateReceived(certificate, peerID);
            certificateHandler(true);

            return true;
        }

        public override void DidChangeState(MCSession session, MCPeerID peerID, MCSessionState state)
            => adapter.OnPeerStateChanged(peerID, state);

        public override void DidReceiveData(MCSession session, NSData data, MCPeerID peerID)
            => adapter.OnDataReceived(data, peerID);

        public override void DidReceiveStream(MCSession session, NSInputStream stream, string streamName, MCPeerID peerID)
            => adapter.OnStreamReceived(streamName, peerID, stream);

        public override void DidStartReceivingResource(MCSession session, string resourceName, MCPeerID fromPeer, NSProgress progress)
            => adapter.OnResourceStarted(resourceName, fromPeer, progress);

        public override void DidFinishReceivingResource(MCSession session, string resourceName, MCPeerID fromPeer, NSUrl? localUrl, NSError? error)
            => adapter.OnResourceFinished(resourceName, fromPeer, localUrl, error);
#pragma warning restore S1144, S1172
    }
}
