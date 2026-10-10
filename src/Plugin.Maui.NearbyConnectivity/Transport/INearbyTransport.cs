namespace Plugin.Maui.NearbyConnectivity;

// The transport beneath NearbySession, so the unit suite can drive the session through a fake.
// One production implementation serves every target; the platform split is IPlatformAdapter.
//
// AdvertiseAsync and DiscoverAsync are not safe to call concurrently with themselves; start a new
// session only after the previous completes. Concurrent ConnectAsync for different devices is safe.
// DisposeAsync is safe from any thread exactly once.
//
// Platform callbacks arrive on SDK-owned threads with no guarantee which — iOS documents a private
// serial queue, Android's GMS Nearby documents nothing. Implementations record what a callback saw
// on whatever thread it arrived on and write it into a channel. That thread never reaches an
// application: NearbySession drains the channels and republishes on the thread pool, which is what
// the public contracts promise. Do not document the SDK callback thread as consumer-observable.
//
// The `started` TaskCompletionSource on the two start methods resolves once the platform start
// phase is known. Await it, not the first stream item, to learn whether the start succeeded — a
// successful start can validly yield nothing for a long time. A fault arriving after `started` has
// already completed ends the stream with that exception instead of retroactively faulting it.
interface INearbyTransport : IAsyncDisposable
{
    // Advertising begins when enumeration starts and stops when the enumerable is disposed. The
    // consumer must Accept or Decline every yielded request.
    IAsyncEnumerable<NearbyConnectionRequest> AdvertiseAsync(TaskCompletionSource started, CancellationToken cancellationToken = default);

    // Events for one device are ordered: a lost event is always preceded by its found event.
    IAsyncEnumerable<NearbyDeviceEvent> DiscoverAsync(TaskCompletionSource started, CancellationToken cancellationToken = default);

    // Cancellation removes the pending attempt but does not guarantee the remote was notified.
    Task<NearbyConnection> ConnectAsync(NearbyDevice device, CancellationToken cancellationToken = default);

    // Never prompts, never mutates.
    Task<NearbyAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    // Platform signals reach the session through this seam rather than a platform type holding the
    // session, which keeps the dependency pointing one way. Raised on whatever thread the platform
    // used, so a handler must not block: the session hands the re-check to its own task set. The
    // event carries no value — a refresh re-runs the whole ladder rather than patching one input.
    event Action? AvailabilityChanged;

    // Only iOS raises this. See AvailabilityChanged for threading.
    event Action? Suspended;

    void AttachDeviceGateway(INearbyDeviceGateway gateway);

    // The platform's connection table is the one owner of "device X has a live connection"; the
    // session queries it here rather than keeping a second table.
    bool TryGetConnection(string deviceId, [NotNullWhen(true)] out NearbyConnection? connection);

    // A copy: it does not track later opens or releases.
    NearbyConnection[] SnapshotConnections();

    // The convergent-connect rule: a second connect to a device whose handshake is already running
    // joins it rather than starting a competing one. This is what makes connecting twice safe.
    bool TryJoinHandshake(string deviceId, [NotNullWhen(true)] out Task<NearbyConnection>? handshake);

    // Read live off the handshake table so the value cannot outlive the handshake it describes.
    // Null for a joined handshake's second caller and for the acceptor side, whose deadline is
    // NearbyConnectionRequest.ExpiresAt.
    bool TryGetConnectDeadline(string deviceId, [NotNullWhen(true)] out DateTimeOffset? deadline);

    // The effective value, never one still pending.
    NearbyPresence Presence { get; }

    // Takes effect at the next idle edge.
    void SetPresence(NearbyPresence presence);
}
