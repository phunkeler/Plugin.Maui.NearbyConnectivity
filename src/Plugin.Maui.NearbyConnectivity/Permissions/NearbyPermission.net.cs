namespace Plugin.Maui.NearbyConnectivity;

public sealed partial class NearbyPermission
{
    /// <summary>
    /// Reports <see cref="PermissionStatus.Granted"/> on targets with no nearby connectivity
    /// support, so shared code can call it unconditionally.
    /// </summary>
    /// <returns>A task whose result is always <see cref="PermissionStatus.Granted"/>.</returns>
    /// <remarks>
    /// A permission is not what is missing on an unsupported target. The platform is. That is
    /// reported by <see cref="INearbyConnectivity.CheckAvailabilityAsync(CancellationToken)"/> as
    /// <see cref="NearbyAvailability.Unsupported"/>, which is where a caller should branch.
    /// </remarks>
    public override Task<PermissionStatus> CheckStatusAsync()
        => Task.FromResult(PermissionStatus.Granted);

    /// <inheritdoc cref="CheckStatusAsync"/>
    public override Task<PermissionStatus> RequestAsync()
        => Task.FromResult(PermissionStatus.Granted);
}
