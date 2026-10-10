namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Why an activity stopped.
/// </summary>
/// <remarks>
/// Carried by <see cref="NearbyActivityStop.Reason"/>.
/// </remarks>
public enum NearbyActivityStopReason
{
    /// <summary>
    /// This application stopped it, through <see cref="INearbyActivity.StopAsync"/> or
    /// <see cref="INearbyConnectivity.StopAllAsync"/>.
    /// </summary>
    Requested,

    /// <summary>
    /// The platform ended it: the radio was turned off, a permission was revoked, or the platform
    /// returned an error.
    /// </summary>
    /// <remarks>
    /// <see cref="NearbyActivityStop.Error"/> carries the fault. Call
    /// <see cref="INearbyConnectivity.CheckAvailabilityAsync(CancellationToken)"/> to find what now
    /// blocks a restart.
    /// </remarks>
    Faulted,

    /// <summary>
    /// The application was backgrounded and the platform tore the session down.
    /// </summary>
    /// <remarks>
    /// iOS only, for the reason described on <see cref="NearbyEndReason.AppSuspended"/>.
    /// </remarks>
    Suspended,
}
