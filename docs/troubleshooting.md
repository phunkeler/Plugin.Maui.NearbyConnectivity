# Troubleshooting

Each entry gives a symptom that you can see, its cause, and the fix. If no entry matches,
[turn on logging](logging.md) first. The plugin logs every platform callback that it receives.

## Devices never appear

Discovery starts and reports no error, but the list stays empty. Check these causes in order.

**The two devices use different `ServiceId` values.** Only devices with the same [`ServiceId`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.ServiceId) find
each other. Check that both apps register the same string.

**The two devices are on different platforms.** An Android device and an iPhone never discover each
other. See [why the platforms do not interoperate](platform-behavior.md#why-the-platforms-do-not-interoperate).

**On Android, the runtime permissions are missing.** A request for the wrong set fails without an
error. Discovery starts and finds nothing. Request [`NearbyPermission`](xref:Plugin.Maui.NearbyConnectivity.NearbyPermission), which contains the correct
set for the running API level. See
[Android runtime permissions](platform-setup.md#android-runtime-permissions).

**On Android, the `Topology` values differ.** [`NearbyOptions.Android.Topology`](xref:Plugin.Maui.NearbyConnectivity.NearbyAndroidOptions.Topology) must be the same on
the advertising device and the discovering device. If the values differ, the devices never find
each other.

**On Android, Google Play services is missing.** [`CheckAvailabilityAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.CheckAvailabilityAsync*) reports
[`PlayServicesUnavailable`](xref:Plugin.Maui.NearbyConnectivity.NearbyAvailability.PlayServicesUnavailable) and the start methods throw. The plugin has no fallback transport. See
[Android requires Google Play services](platform-behavior.md#android-requires-google-play-services).

**On iOS, `NSBonjourServices` is missing or wrong.** `Info.plist` must contain both
`_yourserviceid._tcp` and `_yourserviceid._udp`, with the same `ServiceId` that you registered.
`CheckAvailabilityAsync` reports [`InvalidConfiguration`](xref:Plugin.Maui.NearbyConnectivity.NearbyAvailability.InvalidConfiguration) when neither is declared. See
[iOS setup](platform-setup.md#ios).

**On iOS, the person denied Local Network access.** Discovery starts and finds nothing, and no
API reports the denial. `NearbyPermission` still reports [`Granted`](xref:Microsoft.Maui.ApplicationModel.PermissionStatus.Granted). Tell the person to turn on the
app under Settings › Privacy & Security › Local Network.

## The device list renders once and never updates

You bound a [`CollectionView`](https://learn.microsoft.com/dotnet/api/microsoft.maui.controls.collectionview) to [`nearby.Devices`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.Devices). `nearby.Devices` is a read-only snapshot list and
raises no change notification. Bind a [`NearbyDeviceCollection`](bind-ui.md#bind-a-device-list)
instead.

## The status says "Ready" but nothing is happening

You bound [`Availability`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.Availability) alone. A platform fault that stops advertising or discovery reaches only
the activity's [`LastStop`](xref:Plugin.Maui.NearbyConnectivity.INearbyActivity.LastStop), so availability still reports no problem. Bind
[`NearbySessionState`](bind-ui.md#bind-a-status-line), which merges both.

## A request expired while the prompt was open

MAUI prompts accept no [`CancellationToken`](xref:System.Threading.CancellationToken), so a slow decision lasts longer than the offer window.
Race the prompt against [`request.Expired`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectionRequest.Expired). See
[handle a request that expires](connect.md#handle-a-request-that-expires).

## Messages go missing

Two loops read the same payloads. [`nearby.PayloadsAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.PayloadsAsync*) gives each payload to one enumeration, so
two enumerations divide the payloads between them. Read in one loop, and distribute the payloads
from that loop.

## The send completed but nothing arrived

When [`SendAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.SendAsync*) returns, the platform accepted the bytes for delivery. It does not mean that the
peer's app processed them. If your app must know that the peer's app stored the work, have the peer
send an acknowledgement payload. See [Send and receive payloads](payloads.md).

## Everything stops when the app moves to the background

This is expected on both platforms. iOS suspends an app within seconds after it moves to the
background, and Multipeer Connectivity has no background mode. The plugin therefore stops the session. On
Android, the connection ends when the process ends. The plugin does not reconnect automatically.
See
[connections do not survive backgrounding](platform-behavior.md#connections-do-not-survive-backgrounding).

## `ConnectAsync` threw `Failed` when the peer declined

[`Failed`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectFailure.Failed) covers a decline, a dropped radio, and an out-of-range device. [`NearbyConnectFailure`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectFailure) has
no `Declined` value, because iOS does not tell the requesting device that a peer declined.

## `SendAsync` threw `ArgumentOutOfRangeException`

The payload is larger than 32 KiB. Send a file or open a stream instead. See
[Limits](configuration.md#limits).

## The app throws at startup

`ServiceId` is not set or is invalid. It must be 1 to 15 characters: ASCII lowercase letters,
digits, and hyphens. It must contain at least one letter, and no hyphen at the start, at the end,
or next to another hyphen. `ServiceId` has no default.

## `AuthenticationCode` is empty

The log shows event 1300. On Android, the handshake gave no authentication token. On iOS, the
certificate exchange gave no peer certificate, or a second certificate arrived on the same link
(event 1303). Treat an empty code as a mismatch. See [Security](security.md).

## Next

- [Turn on logging](logging.md) to see what the platform reported.
- [Platform behavior](platform-behavior.md) for what each platform does on its own.
