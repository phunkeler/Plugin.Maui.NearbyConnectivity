namespace Plugin.Maui.NearbyConnectivity;

public sealed partial class NearbyPermission
{
    /// <summary>
    /// Reports <see cref="PermissionStatus.Granted"/>. iOS has no runtime permission for
    /// MultipeerConnectivity.
    /// </summary>
    /// <returns>A task whose result is always <see cref="PermissionStatus.Granted"/>.</returns>
    /// <remarks>
    /// Local-network access is gated by <c>NSLocalNetworkUsageDescription</c> and
    /// <c>NSBonjourServices</c> in the application's <c>Info.plist</c>, build-time configuration
    /// with no runtime prompt to request. A missing usage description, and a
    /// <c>NSBonjourServices</c> that names the configured service identifier under no transport,
    /// are reported through
    /// <see cref="INearbyConnectivity.CheckAvailabilityAsync(CancellationToken)"/> as
    /// <see cref="NearbyAvailability.InvalidConfiguration"/>, not here.
    /// </remarks>
    public override Task<PermissionStatus> CheckStatusAsync()
        => Task.FromResult(PermissionStatus.Granted);

    /// <inheritdoc cref="CheckStatusAsync"/>
    public override Task<PermissionStatus> RequestAsync()
        => Task.FromResult(PermissionStatus.Granted);
}
