# Platform behavior

## Why the platforms do not interoperate

**An Android device and an iPhone that run your app never discover each other.** The plugin gives
you one API over two unrelated native transports. It does not give you one network across both.

| | Android | iOS |
| --- | --- | --- |
| Transport | Nearby Connections | Multipeer Connectivity |
| Delivered by | Google Play services | The operating system |
| Wire format | Google's, closed | Apple's, closed |

Neither vendor publishes a specification that the other could implement, and the same [`ServiceId`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.ServiceId)
on both sides does not help. If your users are on both platforms and must reach each other, do not
adopt this plugin.

## Android requires Google Play services

Nearby Connections is part of Google Play services, not of the Android platform. A device without
Google Play services cannot advertise or discover. Such devices include AOSP builds and some rugged
industrial handhelds.

On such a device, [`CheckAvailabilityAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.CheckAvailabilityAsync*) reports [`NearbyAvailability.PlayServicesUnavailable`](xref:Plugin.Maui.NearbyConnectivity.NearbyAvailability.PlayServicesUnavailable) and
the start methods throw [`NearbyException`](xref:Plugin.Maui.NearbyConnectivity.NearbyException). The plugin has no fallback transport. Check availability
before you show the feature:

```csharp
if (await nearby.CheckAvailabilityAsync(ct) is NearbyAvailability.PlayServicesUnavailable)
{
    // This device cannot do nearby connectivity at all. Hide the feature.
}
```

## Connections do not survive backgrounding

**iOS.** Multipeer Connectivity has no background mode, and iOS suspends an app within seconds
after it moves to the background. The plugin stops the session on [`DidEnterBackground`](https://learn.microsoft.com/dotnet/api/uikit.uiapplication.didenterbackgroundnotification), the same
way that [`StopAllAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.StopAllAsync*) does, but with a different reason:

- The [`Closed`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.Closed) task of every open connection completes with [`NearbyEndReason.AppSuspended`](xref:Plugin.Maui.NearbyConnectivity.NearbyEndReason.AppSuspended).
- Every device leaves [`Devices`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.Devices), and [`DeviceChanged`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.DeviceChanged) reports each one as [`Removed`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceChangeAction.Removed).
- Advertising and discovery stop, and [`LastStop.Reason`](xref:Plugin.Maui.NearbyConnectivity.NearbyActivityStop.Reason) is [`Suspended`](xref:Plugin.Maui.NearbyConnectivity.NearbyActivityStopReason.Suspended).

The plugin restarts nothing when the app returns to the foreground. Your app decides whether to
restart.

**Android.** The connection ends when the process ends, and Doze separately suspends networking. A
connection survives the background only with a foreground service. The plugin leaves that work to
your app. See
[Keep a connection open in the background](platform-setup.md#keep-a-connection-open-in-the-background).
The plugin also does not stop advertising or discovery when the app moves to the background. If your app continues in the background, make sure that the user knows. Google's
[Nearby guidelines](https://developers.google.com/nearby/developer-guidelines) require this.

**There is no auto-reconnect.** Neither platform has a reconnect API. To recover, advertise again
and connect again.

## App versions during a rollout

Devices with different versions of your app can connect. The plugin sends a small control frame
with the offer window and the presence. A device that cannot read that frame ignores it and does
not deliver it to your app. It then uses the default [`OfferWindow`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.OfferWindow) and receives no [`Info`](xref:Plugin.Maui.NearbyConnectivity.NearbyPresence.Info). Discovery, connections,
and payloads still work. On iOS, a frame that the device cannot read during the authentication
code exchange fails that connection attempt.

## Which radio carries the data

Neither platform lets you choose the radio. On Android, Nearby Connections selects from Bluetooth
Classic, BLE, and Wi-Fi, based on [`NearbyOptions.Android.Topology`](xref:Plugin.Maui.NearbyConnectivity.NearbyAndroidOptions.Topology). On iOS, Multipeer Connectivity
uses Wi-Fi only and always behaves as a mesh. For that reason, the plugin reports
[`NearbyAvailability.BluetoothDisabled`](xref:Plugin.Maui.NearbyConnectivity.NearbyAvailability.BluetoothDisabled) and [`WifiDisabled`](xref:Plugin.Maui.NearbyConnectivity.NearbyAvailability.WifiDisabled) on Android only.

## Next

- [Security](security.md) for what an encrypted link does and does not prove.
- [Set up Android and iOS](platform-setup.md) for the manifest and `Info.plist` entries that each
  platform needs.
