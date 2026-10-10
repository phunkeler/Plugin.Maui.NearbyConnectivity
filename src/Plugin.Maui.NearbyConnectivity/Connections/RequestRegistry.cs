namespace Plugin.Maui.NearbyConnectivity;

sealed class RequestRegistry(
    TimeProvider timeProvider,
    Func<NearbyConnectionRequest, Task> onExpired)
{
    readonly ConcurrentDictionary<string, NearbyConnectionRequest> _outstanding
        = new(StringComparer.Ordinal);

    // Returns the request this one replaced. The caller settles it: the platform has already
    // swapped the offer for the same device, so the replaced request is withdrawn rather than
    // answered.
    //
    // The countdown runs for the declared window brought inside OfferWindowPolicy's trust bound, so
    // a remote initiator cannot pin a row open indefinitely by declaring an over-long or infinite
    // one.
    internal NearbyConnectionRequest? Track(NearbyConnectionRequest request)
    {
        var deviceId = request.RemoteDevice.Id;
        var timeout = OfferWindowPolicy.Clamp(request.ExpiresAt - timeProvider.GetUtcNow());

        _outstanding.TryGetValue(deviceId, out var replaced);
        replaced?.DisarmCountdown();

        _outstanding[deviceId] = request;
        _ = ExpireAfterAsync(request, timeout, request.ArmCountdown());

        return replaced;
    }

    // Exactly one of accept, reject and expiry wins this claim; the losers see false and must not
    // act on the request. Keyed on the INSTANCE, not the device id, so a claim never takes a newer
    // request that replaced this one for the same device — a stale request object, or the timer
    // armed for one, loses cleanly.
    internal bool TryClaim(NearbyConnectionRequest request)
    {
        var deviceId = request.RemoteDevice.Id;

        if (!_outstanding.TryRemove(new KeyValuePair<string, NearbyConnectionRequest>(deviceId, request)))
        {
            return false;
        }

        request.DisarmCountdown();

        return true;
    }

    internal bool Contains(string deviceId) => _outstanding.ContainsKey(deviceId);

    // Read by the convergent connect path: a device that already asked to connect is answered by
    // accepting its request rather than by racing a second offer against it.
    internal bool TryGet(string deviceId, [NotNullWhen(true)] out NearbyConnectionRequest? request)
        => _outstanding.TryGetValue(deviceId, out request);

    internal NearbyConnectionRequest[] ClaimAll()
    {
        var claimed = new List<NearbyConnectionRequest>();

        foreach (var request in _outstanding.Values)
        {
            if (TryClaim(request))
            {
                claimed.Add(request);
            }
        }

        return [.. claimed];
    }

    async Task ExpireAfterAsync(
        NearbyConnectionRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(timeout, timeProvider, cancellationToken).ConfigureAwait(false);

            if (!TryClaim(request))
            {
                return;
            }

            await onExpired(request).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Answered, or the session stopped. Whoever canceled owns the disposal.
        }
    }
}
