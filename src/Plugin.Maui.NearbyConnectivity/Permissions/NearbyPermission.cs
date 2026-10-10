namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// The runtime permissions nearby connectivity needs on the current platform.
/// </summary>
/// <remarks>
/// <para>
/// Use it the way every other .NET MAUI permission is used:
/// </para>
/// <example>
/// <code language="csharp">
/// var status = await Permissions.RequestAsync&lt;NearbyPermission&gt;();
///
/// if (status is not PermissionStatus.Granted)
/// {
///     // Direct the user to system settings.
///     return;
/// }
///
/// await nearby.Discoverer.StartAsync(cancellationToken);
/// </code>
/// </example>
/// <para>
/// This type resolves the right set for the running platform and OS version. On Android that set
/// differs by API level: location before 31, the granular <c>BLUETOOTH_*</c> permissions from 31,
/// and <c>NEARBY_WIFI_DEVICES</c> from 33. Requesting the wrong set fails silently, with
/// advertising and discovery starting and never seeing anything.
/// </para>
/// <para>
/// On iOS there is no runtime permission to request, so both members report
/// <see cref="PermissionStatus.Granted"/> and shared code can call them unconditionally.
/// MultipeerConnectivity is gated by the <c>NSLocalNetworkUsageDescription</c> and
/// <c>NSBonjourServices</c> keys in <c>Info.plist</c> instead. iOS shows its Local Network prompt
/// on first use. If the person denies it, discovery finds nothing, no error is reported, and this
/// type still reports <see cref="PermissionStatus.Granted"/>.
/// </para>
/// <para>
/// <see cref="INearbyActivity.StartAsync(CancellationToken)"/> reports a missing permission rather
/// than prompting. Requesting is the application's decision.
/// </para>
/// </remarks>
/// <seealso href="https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/platform-setup.html">Set up Android and iOS</seealso>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The 'Permission' suffix is the .NET MAUI convention for a Permissions.BasePlatformPermission " +
        "subclass — it is how Permissions.RequestAsync<T>() reads at the call site, and every first-party " +
        "permission type (Permissions.Bluetooth, Permissions.LocationWhenInUse) sits under that same API. " +
        "Renaming would make this type read as something other than what it is.")]
public sealed partial class NearbyPermission : Permissions.BasePlatformPermission
{
}
