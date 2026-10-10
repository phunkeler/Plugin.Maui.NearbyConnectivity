namespace Plugin.Maui.NearbyConnectivity;

// What the transport asks a platform SDK to perform. Inbound SDK callbacks do not come back
// through this interface: adapters call the transport's internal On*/Write* methods directly, and
// the device tests drive that surface. Operations on an established link live on the
// IPlatformConnection the adapter produces.
interface IPlatformAdapter : IDisposable
{
    // The Start* pair throws the typed start failure; the Stop* pair never throws.
    Task StartAdvertisingAsync(CancellationToken cancellationToken);

    void StopAdvertising();

    Task StartDiscoveryAsync(CancellationToken cancellationToken);

    void StopDiscovering();

    // Completes when the request is sent — the terminal callback, not this task, resolves the
    // handshake ledger.
    Task InitiateConnectAsync(NearbyDevice device, CancellationToken cancellationToken);

    // Never throws; failures log.
    Task AbandonConnectAsync(NearbyDevice device);

    // Never prompts, never mutates.
    Task<NearbyAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);

    // Only Android has such a signal: the user toggling Bluetooth or Wi-Fi arrives as a system
    // broadcast. Platforms with nothing to report leave this null. The event carries no value on
    // purpose — a refresh re-runs the whole worst-first ladder rather than patching one input.
    event Action? AvailabilityChanged;

    // Only iOS raises this: MultipeerConnectivity has no background mode, so a backgrounded app's
    // session dies within seconds whether or not the library notices.
    event Action? Suspended;

    // Called at construction, before anything can be staged, and again at disposal.
    void SweepStaging();

    // Called after the transport drained the work that read this connection's handles. Draining
    // before releasing is what keeps the release safe.
    void ReleaseConnection(string deviceId);

    // Releases whatever the platform holds for the span between the first start and the moment
    // nothing is running, connected or being negotiated. Never throws.
    //
    // The transport decides when: it owns the run count and both tables the decision reads, and
    // calls this from the one chokepoint every ending routes through. An adapter must not test
    // idleness itself — a platform computing it from its own SDK fields cannot see a handshake
    // dropped without a terminal callback, and the iOS session then outlives its span.
    //
    // Only iOS holds anything for that span: the shared MCSession and the local MCPeerID.
    void ReleaseIdleState();
}
