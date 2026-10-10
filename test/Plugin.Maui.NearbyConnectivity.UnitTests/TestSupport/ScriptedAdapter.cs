namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// The scripted <see cref="IPlatformAdapter"/>: the fourth implementation of the platform seam,
/// and the one that makes the transport's own logic — the channel swap, the handshake ledger, the
/// release order — testable on <c>net10.0</c>, where the shipping adapter throws.
/// </summary>
/// <remarks>
/// Every operation defaults to the shipping <c>net10.0</c> behavior (starts throw, availability
/// reports unsupported), so a test that scripts nothing observes the stub. A test scripts an
/// operation by assigning its delegate.
/// </remarks>
sealed class ScriptedAdapter : IPlatformAdapter
{
    const string NotSupported = "Not scripted: the default mirrors the net10.0 throwing adapter.";

    /// <summary>What <see cref="StartAdvertisingAsync"/> runs. Defaults to a throw.</summary>
    public Func<CancellationToken, Task> OnStartAdvertising { get; set; }
        = static _ => throw new PlatformNotSupportedException(NotSupported);

    /// <summary>What <see cref="StartDiscoveryAsync"/> runs. Defaults to a throw.</summary>
    public Func<CancellationToken, Task> OnStartDiscovery { get; set; }
        = static _ => throw new PlatformNotSupportedException(NotSupported);

    /// <summary>What <see cref="InitiateConnectAsync"/> runs. Defaults to a throw.</summary>
    public Func<NearbyDevice, CancellationToken, Task> OnInitiateConnect { get; set; }
        = static (_, _) => throw new PlatformNotSupportedException(NotSupported);

    /// <summary>Gets the device ids whose platform bookkeeping was released, in order.</summary>
    public List<string> Released { get; } = [];

    /// <summary>Gets how many times advertising was stopped.</summary>
    /// <remarks>
    /// Counted because a cancelled start must still reach the stop: the platform can already be
    /// advertising when the token fires, and nothing else off-device can observe that it was told.
    /// </remarks>
    public int StopAdvertisingCount { get; private set; }

    /// <summary>Gets how many times discovery was stopped. See <see cref="StopAdvertisingCount"/>.</summary>
    public int StopDiscoveringCount { get; private set; }

    /// <inheritdoc/>
    public event Action? AvailabilityChanged;

    /// <inheritdoc/>
    public event Action? Suspended;

    /// <summary>Raises <see cref="AvailabilityChanged"/>, so a test can drive the transport's relay.</summary>
    public void EmitAvailabilityChanged() => AvailabilityChanged?.Invoke();

    /// <summary>Raises <see cref="Suspended"/>, so a test can drive the transport's relay.</summary>
    public void EmitSuspended() => Suspended?.Invoke();

    public Task StartAdvertisingAsync(CancellationToken cancellationToken)
        => OnStartAdvertising(cancellationToken);

    public void StopAdvertising() => StopAdvertisingCount++;

    public Task StartDiscoveryAsync(CancellationToken cancellationToken)
        => OnStartDiscovery(cancellationToken);

    public void StopDiscovering() => StopDiscoveringCount++;

    public Task InitiateConnectAsync(NearbyDevice device, CancellationToken cancellationToken)
        => OnInitiateConnect(device, cancellationToken);

    public Task AbandonConnectAsync(NearbyDevice device) => Task.CompletedTask;

    public Task<NearbyAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(NearbyAvailability.Unsupported);
    }

    /// <summary>Gets how many times the transport asked for the staging root to be swept.</summary>
    public int SweepStagingCount { get; private set; }

    public void SweepStaging() => SweepStagingCount++;

    public void ReleaseConnection(string deviceId) => Released.Add(deviceId);

    /// <summary>Gets how many times the transport reported the idle span had ended.</summary>
    public int ReleaseIdleStateCount { get; private set; }

    public void ReleaseIdleState() => ReleaseIdleStateCount++;

    public void Dispose()
    {
    }
}
