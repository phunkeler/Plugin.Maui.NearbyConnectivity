namespace Plugin.Maui.NearbyConnectivity;

// The net10.0 adapter: every start throws, so a session on an unsupported platform fails at the
// operation rather than pretending. Stop, release and dispose stay no-ops — they run on shared
// cleanup paths where a PlatformNotSupportedException would swallow the caller's original failure.
// Off-device unit tests use the scripted adapter in the test suite; this is the shipping stub.
sealed class NetThrowingAdapter : IPlatformAdapter
{
    const string PlatformNotSupportedMessage = "Nearby connectivity is supported on Android and iOS only. The current platform is not supported.";

    public Task StartAdvertisingAsync(CancellationToken cancellationToken)
        => throw new PlatformNotSupportedException(PlatformNotSupportedMessage);

    public void StopAdvertising()
    {
    }

    public Task StartDiscoveryAsync(CancellationToken cancellationToken)
        => throw new PlatformNotSupportedException(PlatformNotSupportedMessage);

    public void StopDiscovering()
    {
    }

    public Task InitiateConnectAsync(NearbyDevice device, CancellationToken cancellationToken)
        => throw new PlatformNotSupportedException(PlatformNotSupportedMessage);

    public Task AbandonConnectAsync(NearbyDevice device) => Task.CompletedTask;

    // Reports rather than throws: the whole point of a preflight check is to answer "can I
    // start?" without the caller having to catch anything.
    public Task<NearbyAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(NearbyAvailability.Unsupported);
    }

    // Neither signal exists on a platform that cannot start: nothing observes radios, and nothing
    // is suspended because nothing runs.
    public event Action? AvailabilityChanged
    {
        add { }
        remove { }
    }

    public event Action? Suspended
    {
        add { }
        remove { }
    }

    // No payload ever arrives here, so nothing is staged and there is nothing to sweep.
    public void SweepStaging()
    {
    }

    public void ReleaseConnection(string deviceId)
    {
    }

    public void ReleaseIdleState()
    {
        // Nothing ever starts here, so nothing is held for a span.
    }

    public void Dispose()
    {
    }
}
