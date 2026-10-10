# Get started

In this tutorial, we build an app for two phones. Each phone shows the other phone in a list. A tap
on a row sends a line of text to the other screen. The tutorial takes about ten minutes plus build
time.

The finished app is [`samples/Minimal`](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/tree/main/samples/Minimal).
Every snippet on this page comes from that app.

You need a .NET MAUI app and two physical devices, both Android or both iPhone. An Android phone
and an iPhone never discover each other, because the two platforms use unrelated native transports.
See [why the platforms do not interoperate](platform-behavior.md#why-the-platforms-do-not-interoperate).

Your app's `SupportedOSPlatformVersion` must be at least Android 24 and iOS 26.0. To start from
an empty app, run `dotnet new maui -n Minimal`.

## 1. Install the package

```bash
dotnet add package Plugin.Maui.NearbyConnectivity
```

## 2. Register the plugin

In `MauiProgram.cs`, register the plugin with a [`ServiceId`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.ServiceId). Both devices must use the same string,
because only devices with the same `ServiceId` find each other.

[!code-csharp[](../samples/Minimal/MauiProgram.cs#register)]

Build and run the app. You should see the app start as before. The plugin starts nothing yet.

## 3. Add the platform entries

On Android, the manifest needs no changes, because the package declares every permission that
Nearby Connections needs. If your app does not derive the user's physical location from Bluetooth
or Wi-Fi scan results, add this block:

[!code-xml[](../samples/Minimal/Platforms/Android/AndroidManifest.xml?range=7-12)]

On iOS, add two Bonjour entries and a usage string to `Platforms/iOS/Info.plist`:

[!code-xml[](../samples/Minimal/Platforms/iOS/Info.plist?range=23-29)]

For every entry and its purpose, see [Set up Android and iOS](platform-setup.md).

## 4. Request permission and start

Request [`NearbyPermission`](xref:Plugin.Maui.NearbyConnectivity.NearbyPermission) before you start. The plugin never shows the permission prompt itself.
On Android, the start methods throw [`NearbyException`](xref:Plugin.Maui.NearbyConnectivity.NearbyException) when a permission is missing.

Start both advertising and discovery. Advertising makes this device visible. Discovery finds the
other devices. The start methods throw `NearbyException` when the device cannot start, for example
when Bluetooth is off. Catch it: an unhandled exception in an `async void` method ends the app.

[!code-csharp[](../samples/Minimal/MainPage.cs#start)]

> [!IMPORTANT]
> [`SetConsent(NearbyConsentPolicy.AcceptAll)`](xref:Plugin.Maui.NearbyConnectivity.INearbyAdvertiser.SetConsent*) accepts every request from any device that knows the
> `ServiceId`. This policy keeps the tutorial short. In a shipping app, leave the policy at [`Hold`](xref:Plugin.Maui.NearbyConnectivity.NearbyConsentPolicy.Hold)
> and let a person decide. See [Answer an inbound request](connect.md#answer-an-inbound-request).

You should see the permission prompt on the first run.

## 5. Send a message

Connect to the tapped device. Then send the text as UTF-8 bytes. [`ConnectAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.ConnectAsync*) returns the
existing connection when the device is already connected, so a second tap is not an error.

[!code-csharp[](../samples/Minimal/MainPage.cs#send)]

The device list is a [`NearbyDeviceCollection`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceCollection). It handles the plugin's [`DeviceChanged`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.DeviceChanged) event and
raises [`INotifyCollectionChanged`](xref:System.Collections.Specialized.INotifyCollectionChanged), so a bound [`CollectionView`](https://learn.microsoft.com/dotnet/api/microsoft.maui.controls.collectionview) updates without more code.

## 6. Receive messages

Read every inbound payload in one loop. [`PayloadsAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.PayloadsAsync*) merges the payloads from every connection,
so you do not track the connection that each payload came from.

[!code-csharp[](../samples/Minimal/MainPage.cs#receive)]

Payloads arrive on a thread-pool thread, never on the UI thread. Dispatch to the UI thread before
you touch a control. You own every payload, so dispose each one when you are done with it.

## 7. Stop when the page disappears

Stop the plugin in [`OnDisappearing`](https://learn.microsoft.com/dotnet/api/microsoft.maui.controls.page.ondisappearing):

[!code-csharp[](../samples/Minimal/MainPage.cs#stop)]

[`StopAllAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.StopAllAsync*) stops advertising, stops discovery, and disconnects every peer. The plugin stays
usable, so you can start again later.

## 8. Run it on two devices

Deploy the app to both phones. Start the app on each phone.

```bash
dotnet build -t:Run -f net10.0-android
```

Within a few seconds, you should see these results:

1. Each phone lists the other phone by name.
2. A tap on a row on one phone connects the two phones.
3. The message appears on the other phone, prefixed with the sender's name.

If the list stays empty, see [Troubleshooting](troubleshooting.md). The two most common causes are
a `ServiceId` that differs between the two apps and a missing Android runtime permission.

## Next

- [Accept and make connections](connect.md) to prompt a person instead of accepting everything.
- [Send and receive payloads](payloads.md) for files and streams.
- [Bind to a MAUI page](bind-ui.md) for the bindable types that this tutorial used.
