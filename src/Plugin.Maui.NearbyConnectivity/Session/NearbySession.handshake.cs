namespace Plugin.Maui.NearbyConnectivity;

sealed partial class NearbySession
{
    static NearbyEndReason ReasonFor(Exception exception) => exception switch
    {
        OperationCanceledException => NearbyEndReason.Canceled,
        NearbyConnectException { Reason: NearbyConnectFailure.TimedOut } => NearbyEndReason.TimedOut,
        _ => NearbyEndReason.Failed,
    };

    void EndHandshake(NearbyDevice device, NearbyEndReason reason)
    {
        NearbyLog.HandshakeEnded(_logger, device.Id, reason);
        ResetToVisible(device, reason);
    }

    void Transition(
        NearbyDevice device,
        NearbyDeviceStatus status,
        NearbyEndReason? reason = null)
    {
        _deviceRegistry.Update(
            device.Id,
            current => current.Status == status ? current : current.With(status),
            reason);
    }

    void OnDeviceEvent(NearbyDeviceEvent deviceEvent)
    {
        var device = deviceEvent.Device;

        if (deviceEvent.Found)
        {
            _deviceRegistry.AddIfAbsent(device);
            return;
        }

        if (_nearbyTransport.TryGetConnection(device.Id, out _)
            || _requests.Contains(device.Id))
        {
            return;
        }

        if (_deviceRegistry.TryGet(device.Id, out var known)
            && known.Status is NearbyDeviceStatus.Visible)
        {
            _deviceRegistry.Remove(device.Id, NearbyEndReason.LostFromDiscovery);
        }
    }

    // Never throws — a failing predicate falls through to holding the request, which is the
    // outcome that loses nothing.
    async Task ResolveConsentAsync(NearbyConnectionRequest request, CancellationToken advertising)
    {
        var device = request.RemoteDevice;

        if (_nearbyTransport.TryGetConnection(device.Id, out _))
        {
            NearbyLog.RequestFromConnectedDevice(_logger, device.Id);
            await RejectUnderConsentAsync(request, advertising).ConfigureAwait(false);
            return;
        }

        var consent = _consent;
        bool? decision = null;

        if (consent.Decide is { } decide)
        {
            using var expiry = new CancellationTokenSource(
                OfferWindowPolicy.Clamp(request.ExpiresAt - _timeProvider.GetUtcNow()),
                _timeProvider);
            using var deciding = CancellationTokenSource.CreateLinkedTokenSource(advertising, expiry.Token);

            try
            {
                decision = await decide(device, deciding.Token).AsTask().WaitAsync(deciding.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (deciding.IsCancellationRequested)
            {
                NearbyLog.ConsentDecisionAbandoned(_logger, device.Id);
                await RejectUnderConsentAsync(request, CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                NearbyLog.ConsentPredicateFailed(_logger, device.Id, ex);
            }
        }

        decision ??= consent.Policy switch
        {
            NearbyConsentPolicy.AcceptAll => true,
            NearbyConsentPolicy.RejectAll => false,
            _ => null,
        };

        if (decision is true)
        {
            await AutoAcceptAsync(request, advertising).ConfigureAwait(false);
            return;
        }

        if (decision is false)
        {
            await RejectUnderConsentAsync(request, advertising).ConfigureAwait(false);
            return;
        }

        HoldRequest(request);
    }

    Task RejectUnderConsentAsync(NearbyConnectionRequest request, CancellationToken cancellationToken)
        => request.TryRejectAsync(
            ex => NearbyLog.ConsentRejectFailed(_logger, request.RemoteDevice.Id, ex),
            cancellationToken);

    void HoldRequest(NearbyConnectionRequest request)
    {
        var device = request.RemoteDevice;
        var replaced = _requests.Track(request);
        _deviceRegistry.AddIfAbsent(device);

        if (replaced is null)
        {
            Transition(device, NearbyDeviceStatus.RequestReceived);
        }
        else
        {
            replaced.MarkExpired();
            NearbyLog.RequestReplaced(_logger, device.Id);
            _deviceRegistry.Update(device.Id, current => current.With(NearbyDeviceStatus.RequestReceived));
        }
    }

    Task DeclineOutstandingRequestsAsync()
        => Task.WhenAll(_requests.ClaimAll().Select(async request =>
        {
            var device = request.RemoteDevice;

            request.MarkExpired();
            await request.TryRejectAsync(ex => NearbyLog.StopRejectFailed(_logger, device.Id, ex), CancellationToken.None).ConfigureAwait(false);

            ResetToVisible(device, NearbyEndReason.Declined);
        }));

    async Task RunRequestExpiryAsync(NearbyConnectionRequest request)
    {
        var device = request.RemoteDevice;

        try
        {
            request.MarkExpired();
            NearbyLog.HandshakeEnded(_logger, device.Id, NearbyEndReason.RequestExpired);
            NearbyLog.RequestExpired(_logger, device.Id);

            await request.TryRejectAsync(
                ex => NearbyLog.RequestExpiryRejectFailed(_logger, device.Id, ex),
                CancellationToken.None).ConfigureAwait(false);

            ResetToVisible(device, NearbyEndReason.RequestExpired);
        }
        catch (Exception ex)
        {
            NearbyLog.RequestExpiryFailed(_logger, device.Id, ex);
        }
    }

    async Task AutoAcceptAsync(NearbyConnectionRequest request, CancellationToken advertising)
    {
        var device = request.RemoteDevice;

        _deviceRegistry.AddIfAbsent(device);
        Transition(device, NearbyDeviceStatus.Connecting);

        var remainingWindow = OfferWindowPolicy.Clamp(request.ExpiresAt - _timeProvider.GetUtcNow());
        var window = remainingWindow < OfferWindowPolicy.s_autoAcceptHandshake
            ? remainingWindow
            : OfferWindowPolicy.s_autoAcceptHandshake;

        using var windowCts = new CancellationTokenSource(window, _timeProvider);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(advertising, windowCts.Token);

        try
        {
            var connection = await request.AcceptCore(linkedCts.Token).ConfigureAwait(false);
            OnConnected(device, connection);
        }
        catch (OperationCanceledException) when (windowCts.IsCancellationRequested && !advertising.IsCancellationRequested)
        {
            NearbyLog.AutoAcceptTimedOut(_logger, device.Id, window.TotalSeconds);
            EndHandshake(device, NearbyEndReason.TimedOut);
        }
        catch (Exception ex)
        {
            NearbyLog.AutoAcceptFailed(_logger, device.Id, ex);
            EndHandshake(device, ReasonFor(ex));
        }
    }

    void OnConnected(NearbyDevice device, NearbyConnection connection)
    {
        if (_requests.TryGet(device.Id, out var held)
            && _requests.TryClaim(held))
        {
            held.MarkExpired();
        }

        _deviceRegistry.AddIfAbsent(device);
        Transition(device, NearbyDeviceStatus.Connected);
        _tasks.Add(WatchDisconnectAsync(device, connection));
        _connectionDeliveries.Publish(connection);
    }

    async Task WatchDisconnectAsync(
        NearbyDevice device,
        NearbyConnection connection)
    {
        try
        {
            var reason = await connection.Closed.WaitAsync(StopToken).ConfigureAwait(false);

            if (_nearbyTransport.TryGetConnection(device.Id, out var current)
                && !ReferenceEquals(current, connection))
            {
                return;
            }

            ResetToVisible(device, reason);
        }
        catch (OperationCanceledException) when (StopToken.IsCancellationRequested)
        {
            // The session stopped first. It clears the registry itself, so there is no transition
            // left for this watcher to record.
        }
        catch (Exception ex)
        {
            NearbyLog.DisconnectWatchFailed(_logger, device.Id, ex);
        }
    }

    void ResetToVisible(NearbyDevice device, NearbyEndReason? reason = null)
    {
        if (!_discoverer.IsActive)
        {
            _deviceRegistry.Remove(device.Id, reason);
            return;
        }

        Transition(device, NearbyDeviceStatus.Visible, reason);
    }

    CancellationToken StopToken => Volatile.Read(ref _stopCts).Token;

    void EnsureStopTokenArmed()
    {
        var observed = Volatile.Read(ref _stopCts);

        if (!observed.IsCancellationRequested)
        {
            return;
        }

        var fresh = new CancellationTokenSource();

        if (!ReferenceEquals(Interlocked.CompareExchange(ref _stopCts, fresh, observed), observed))
        {
            fresh.Dispose();
        }
    }

    async Task<bool> RefreshDiscoveryOnceAsync(CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_discoverer.IsActive)
            {
                return false;
            }

            _deviceRegistry.BeginGeneration(id => _nearbyTransport.TryGetConnection(id, out _));
            await _discoverer.StopPumpAsync(clearFlag: false).ConfigureAwait(false);
            await _discoverer.StartConfirmedAsync(CancellationToken.None).ConfigureAwait(false);

            return true;
        }
        finally
        {
            _stateGate.Release();
        }
    }
}
