# Set up Android and iOS

## What each platform needs

| Step | Android | iOS |
| --- | --- | --- |
| Set the same [`ServiceId`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.ServiceId) on both devices | Yes | Yes |
| Merge the plugin's permissions | Automatic | Not applicable |
| Add `neverForLocation` to two scan permissions | Recommended | Not applicable |
| Request [`NearbyPermission`](xref:Plugin.Maui.NearbyConnectivity.NearbyPermission) at run time | Required | Reports `Granted` at once |
| Add two `NSBonjourServices` entries | Not applicable | Required |
| Add `NSLocalNetworkUsageDescription` | Not applicable | Required |
| Run a foreground service to stay connected in the background | Optional | Not possible |

## Android

**The manifest needs no changes.** The package declares every permission that Nearby Connections
needs. The build merges those permissions into your app's manifest.

**Recommended.** If your app does not derive the user's physical location from Bluetooth or Wi-Fi
scan results, add this block to `Platforms/Android/AndroidManifest.xml`. The package cannot declare
the `neverForLocation` flag itself. Without the flag, Android assumes that these two permissions
imply location access.

```xml
<uses-permission
  android:name="android.permission.BLUETOOTH_SCAN"
  android:usesPermissionFlags="neverForLocation" />
<uses-permission
  android:name="android.permission.NEARBY_WIFI_DEVICES"
  android:usesPermissionFlags="neverForLocation" />
```

### Permissions the package declares

| Permission | Notes |
| --- | --- |
| `INTERNET`, `ACCESS_NETWORK_STATE` | Install-time |
| `ACCESS_WIFI_STATE`, `CHANGE_WIFI_STATE` | Install-time |
| `BLUETOOTH`, `BLUETOOTH_ADMIN` | Capped at `maxSdkVersion="30"` |
| `ACCESS_COARSE_LOCATION`, `ACCESS_FINE_LOCATION` | Capped at `maxSdkVersion="32"` |
| `BLUETOOTH_ADVERTISE`, `BLUETOOTH_CONNECT`, `BLUETOOTH_SCAN` | Runtime, API 31+ |
| `NEARBY_WIFI_DEVICES` | Runtime, API 33+ |

The package declares **no** media permissions. Your app is responsible for the permission to read a
file that it sends. The package also declares no `uses-feature` entry, so it never reduces the set
of devices that can install your app.

To override a permission, declare it again in your own `AndroidManifest.xml`. Your declaration
replaces the package's declaration. Two limits apply:

- **Repeat `maxSdkVersion` in your declaration.** A `BLUETOOTH` declaration without the cap applies
  to every API level, not only up to `maxSdkVersion="30"`.
- **`tools:node="remove"` does not remove these permissions.** The build copies the directive into
  the final manifest unchanged, and the app still requests the permission.

### Android runtime permissions

The manifest entries are not sufficient. Several of them are dangerous permissions, which Android
grants only after an explicit runtime request. The set depends on the API level:

- Below API 31, the location permissions.
- From API 31, the granular `BLUETOOTH_*` permissions.
- From API 33, `NEARBY_WIFI_DEVICES`.

A request for the wrong set fails without an error. Discovery starts and finds nothing.

`NearbyPermission` is a standard MAUI permission that contains the correct set. Request it before
you start:

```csharp
var status = await Permissions.RequestAsync<NearbyPermission>();

if (status is not PermissionStatus.Granted)
{
    // Tell the user why, or send them to system settings.
    return;
}
```

The plugin never shows the permission prompt itself, because the prompt is a UI decision. When a
permission is missing, [`Advertiser.StartAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyActivity.StartAsync*) and `Discoverer.StartAsync` throw [`NearbyException`](xref:Plugin.Maui.NearbyConnectivity.NearbyException).

On iOS, `NearbyPermission` reports [`Granted`](xref:Microsoft.Maui.ApplicationModel.PermissionStatus.Granted) immediately, because iOS has no runtime permission to
request. The first time the app uses the local network, iOS shows the local-network prompt with the
text from `NSLocalNetworkUsageDescription`. If the person denies access, discovery finds nothing
and no API reports the denial. Access can be turned on again under Settings › Privacy & Security ›
Local Network.

### Keep a connection open in the background

Android can stop the process of a background app, and the connection ends with the process. To
keep a connection open, run a foreground service of type `connectedDevice` while a device is
connected. The plugin does not start one, because the ongoing notification is a UI decision.

1. Add the permissions to `Platforms/Android/AndroidManifest.xml`:

   ```xml
   <uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
   <uses-permission android:name="android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE" />
   <uses-permission android:name="android.permission.POST_NOTIFICATIONS" />
   ```

2. Declare the service with `[Service(ForegroundServiceType = ForegroundService.TypeConnectedDevice)]`.
   In [`OnStartCommand`](https://learn.microsoft.com/dotnet/api/android.app.service.onstartcommand), call [`StartForeground`](https://learn.microsoft.com/dotnet/api/android.app.service.startforeground) with the same type.
3. Start the service when the first device connects. Stop it when the last device disconnects. A
   [`NearbyDeviceCollection`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceCollection) with a filter on [`NearbyDeviceStatus.Connected`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceStatus.Connected) gives the count.

- **Start the service while the app is in the foreground.** From API 31, Android throws
  [`ForegroundServiceStartNotAllowedException`](https://learn.microsoft.com/dotnet/api/android.app.foregroundservicestartnotallowedexception) when a background app starts a foreground service.
  Catch it. The connection is not affected.
- From API 33, request [`Permissions.PostNotifications`](https://learn.microsoft.com/dotnet/api/microsoft.maui.applicationmodel.permissions.postnotifications). Without it, the service runs, but the
  notification shade does not show it.
- From API 34, a `connectedDevice` service also needs a granted permission such as
  `BLUETOOTH_CONNECT`. `NearbyPermission` includes it.

[`samples/NearbyChat/Platforms/Android/ConnectionKeepAlive.cs`](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/blob/main/samples/NearbyChat/Platforms/Android/ConnectionKeepAlive.cs)
is a full example. iOS has no equivalent. See
[Connections do not survive backgrounding](platform-behavior.md#connections-do-not-survive-backgrounding).

## iOS

Add these entries to `Info.plist`:

```xml
<key>NSBonjourServices</key>
<array>
  <string>_yourserviceid._tcp</string>
  <string>_yourserviceid._udp</string>
</array>
<key>NSLocalNetworkUsageDescription</key>
<string>Used to discover and connect to nearby devices.</string>
```

**Declare both `_tcp` and `_udp` unless you have a reason not to.** Apple does not document which
of the two Multipeer Connectivity needs, and developers report both results. Some apps work with
`_tcp` alone. In other apps, discovery finds nothing until the app also declares `_udp`. Two
entries cost nothing, so they are the safe default.

When neither entry is declared for your `ServiceId`, [`CheckAvailabilityAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.CheckAvailabilityAsync*) reports
[`NearbyAvailability.InvalidConfiguration`](xref:Plugin.Maui.NearbyConnectivity.NearbyAvailability.InvalidConfiguration). When only one entry is declared, the plugin logs a
warning and still starts, because that configuration may be correct.

`NearbyOptions.ServiceId` is a **separate, shorter value** than the `NSBonjourServices` entries. On
iOS, the plugin passes it unchanged as the `serviceType` for [`MCNearbyServiceAdvertiser`](https://learn.microsoft.com/dotnet/api/multipeerconnectivity.mcnearbyserviceadvertiser) and
[`MCNearbyServiceBrowser`](https://learn.microsoft.com/dotnet/api/multipeerconnectivity.mcnearbyservicebrowser). Apple requires a `serviceType` to be a bare string of 1 to 15 characters,
such as `"nearbychat"`, not the `_name._tcp` Bonjour form. `ServiceId` has no default. If you do
not set it, startup validation throws.

## Next

- [Get started](getting-started.md) to build an app on this setup.
- [Troubleshooting](troubleshooting.md) if devices never appear.
