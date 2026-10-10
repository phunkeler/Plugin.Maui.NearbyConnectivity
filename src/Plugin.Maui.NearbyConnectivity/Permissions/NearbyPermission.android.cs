namespace Plugin.Maui.NearbyConnectivity;

public sealed partial class NearbyPermission
{
    /// <summary>
    /// Checks whether the permissions Nearby Connections needs on this API level are granted.
    /// </summary>
    /// <returns>A task whose result is the combined permission status.</returns>
    /// <remarks>
    /// Delegates to a private <see cref="Permissions.BasePlatformPermission"/> whose
    /// <c>RequiredPermissions</c> carries the per-API-level permission set. That override is not
    /// public here, because <c>RequiredPermissions</c> exists only on Android's copy of the base
    /// type, and a member present on one target framework forces callers of shared code into
    /// <c>#if</c>.
    /// </remarks>
    public override Task<PermissionStatus> CheckStatusAsync() => new Ladder().CheckStatusAsync();

    /// <summary>
    /// Requests the permissions Nearby Connections needs on this API level.
    /// </summary>
    /// <returns>A task whose result is the combined permission status after the prompt.</returns>
    /// <inheritdoc cref="CheckStatusAsync" path="/remarks"/>
    public override Task<PermissionStatus> RequestAsync() => new Ladder().RequestAsync();

    /// <summary>
    /// The permission set Nearby Connections actually needs, by API level.
    /// </summary>
    /// <remarks>
    /// The ladder mirrors what the platform enforces, which changed twice:
    /// <list type="bullet">
    /// <item>
    /// Below API 31, BLE scanning is gated by location, and <c>Permissions.Bluetooth</c> resolves
    /// to an empty set that reports granted without prompting, so location is the only thing that
    /// actually gates discovery there.
    /// </item>
    /// <item>
    /// From API 31 the granular <c>BLUETOOTH_SCAN</c>, <c>BLUETOOTH_ADVERTISE</c> and
    /// <c>BLUETOOTH_CONNECT</c> permissions replace location.
    /// </item>
    /// <item>
    /// From API 33 <c>NEARBY_WIFI_DEVICES</c> is additionally required for the Wi-Fi transports
    /// Nearby Connections upgrades to.
    /// </item>
    /// </list>
    /// The matching <c>uses-permission</c> declarations ship with this package
    /// (<c>AndroidPermissions.android.cs</c>), so an app only has to request them.
    /// </remarks>
    sealed class Ladder : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions
        {
            get
            {
                if (!OperatingSystem.IsAndroidVersionAtLeast(31))
                {
                    return
                    [
                        (Android.Manifest.Permission.AccessFineLocation, true),
                        (Android.Manifest.Permission.AccessCoarseLocation, true),
                    ];
                }

                if (!OperatingSystem.IsAndroidVersionAtLeast(33))
                {
                    return
                    [
                        (Android.Manifest.Permission.BluetoothScan, true),
                        (Android.Manifest.Permission.BluetoothAdvertise, true),
                        (Android.Manifest.Permission.BluetoothConnect, true),
                    ];
                }

                return
                [
                    (Android.Manifest.Permission.BluetoothScan, true),
                    (Android.Manifest.Permission.BluetoothAdvertise, true),
                    (Android.Manifest.Permission.BluetoothConnect, true),
                    (Android.Manifest.Permission.NearbyWifiDevices, true),
                ];
            }
        }
    }
}
