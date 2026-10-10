namespace Plugin.Maui.NearbyConnectivity;

// The handshakes in flight: one entry per device being connected, from the moment a connect or an
// accept registers it until the platform's terminal callback resolves or faults it.
//
// One fact, one owner: "device X has a handshake running, under this deadline, attributed to this
// token". Everything that reads or writes that fact is here, so the convergent-connect rule — a
// second connect joins the handshake already running rather than starting a competing one — is
// decided in one place rather than wherever a caller happens to touch the table.
//
// Every member is safe to call from any thread: the platform resolves handshakes from SDK callback
// threads while the session registers them from its own.
//
// isDisposed is tested after a registration wins, so a handshake registered during teardown is
// canceled rather than left waiting forever. onRemoved runs after a Drop, so the owner can re-test
// whether its idle span has ended — a dropped handshake produces no platform callback, so the
// removal itself is the only signal that nothing is left in flight. TryClaim and ClaimAll do not
// report: they hand the entry to a caller that settles it, and that caller reports once it has.
sealed class HandshakeLedger(Func<bool> isDisposed, Action? onRemoved = null)
{
    // AttributedTo is the token a cancellation is attributed to — the owner's, not a joiner's. Not
    // a token this ledger observes: it is recorded so a cancellation names the operation that
    // owned it. Deadline is null for a registration made before the window is known.
    internal sealed record Pending(
        TaskCompletionSource<NearbyConnection> Tcs,
        DateTimeOffset? Deadline,
        CancellationToken AttributedTo);

    readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    internal bool IsEmpty => _pending.IsEmpty;

    internal TaskCompletionSource<NearbyConnection> Register(
        string deviceId,
        out bool joined,
        DateTimeOffset? deadline,
        CancellationToken attributedTo)
    {
        var mine = new Pending(
            new TaskCompletionSource<NearbyConnection>(TaskCreationOptions.RunContinuationsAsynchronously),
            deadline,
            attributedTo);

        var entry = _pending.GetOrAdd(deviceId, mine);
        var tcs = entry.Tcs;
        joined = !ReferenceEquals(entry, mine);

        if (!joined && isDisposed() && _pending.TryRemove(deviceId, out _))
        {
            tcs.TrySetCanceled(attributedTo);
        }

        return tcs;
    }

    internal bool TryJoin(string deviceId, [NotNullWhen(true)] out Task<NearbyConnection>? handshake)
    {
        if (_pending.TryGetValue(deviceId, out var entry))
        {
            handshake = entry.Tcs.Task;
            return true;
        }

        handshake = null;
        return false;
    }

    // Read live off the same table TryJoin resolves, so the value is the deadline the handshake is
    // actually running under rather than a copy that could outlive it.
    internal bool TryGetDeadline(string deviceId, [NotNullWhen(true)] out DateTimeOffset? deadline)
    {
        if (_pending.TryGetValue(deviceId, out var entry) && entry.Deadline is { } pending)
        {
            deadline = pending;
            return true;
        }

        deadline = null;
        return false;
    }

    // Drops a registered handshake without resolving it, for a reject that answers the offer
    // through the platform instead, or for a deadline that has passed.
    internal void Drop(string deviceId)
    {
        if (_pending.TryRemove(deviceId, out _))
        {
            onRemoved?.Invoke();
        }
    }

    // Deliberately does not report to onRemoved: a claim hands the handshake to a caller that has
    // not finished with it, and that caller reports once it has settled the entry. The success path
    // adds the connection to the table before claiming, so the span never reads as empty between
    // the two.
    internal bool TryClaim(string deviceId, [NotNullWhen(true)] out Pending? entry)
        => _pending.TryRemove(deviceId, out entry);

    // For teardown. The caller settles what it is handed.
    internal Pending[] ClaimAll()
    {
        var claimed = new List<Pending>();

        foreach (var deviceId in _pending.Keys)
        {
            if (_pending.TryRemove(deviceId, out var entry))
            {
                claimed.Add(entry);
            }
        }

        return [.. claimed];
    }
}
