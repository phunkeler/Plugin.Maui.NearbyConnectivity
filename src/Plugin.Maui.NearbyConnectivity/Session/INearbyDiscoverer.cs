namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Discovery: looks for nearby advertising devices and reports them in
/// <see cref="INearbyConnectivity.Devices"/>.
/// </summary>
/// <seealso cref="INearbyConnectivity.Discoverer"/>
public interface INearbyDiscoverer : INearbyActivity
{
    /// <summary>
    /// Re-sweeps discovery now, without waiting for the next periodic refresh.
    /// </summary>
    /// <param name="cancellationToken">A token to abandon the refresh.</param>
    /// <returns>A task that completes once the sweep has restarted discovery.</returns>
    /// <remarks>
    /// The pull-to-refresh operation. No-op when not discovering. Devices no longer in range leave
    /// <see cref="INearbyConnectivity.Devices"/> once the sweep completes.
    /// </remarks>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
