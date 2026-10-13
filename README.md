# Plugin.Maui.NearbyConnectivity

[![NuGet Version](https://img.shields.io/nuget/v/Plugin.Maui.NearbyConnectivity)](https://www.nuget.org/packages/Plugin.Maui.NearbyConnectivity) [![NuGet Downloads](https://img.shields.io/nuget/dt/Plugin.Maui.NearbyConnectivity)](https://www.nuget.org/packages/Plugin.Maui.NearbyConnectivity) [![CI](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/actions/workflows/ci.yml/badge.svg)](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/actions/workflows/ci.yml) [![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=phunkeler_Plugin.Maui.NearbyConnectivity&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=phunkeler_Plugin.Maui.NearbyConnectivity) [![Coverage](https://sonarcloud.io/api/project_badges/measure?project=phunkeler_Plugin.Maui.NearbyConnectivity&metric=coverage)](https://sonarcloud.io/summary/new_code?id=phunkeler_Plugin.Maui.NearbyConnectivity) [![License](https://img.shields.io/github/license/phunkeler/Plugin.Maui.NearbyConnectivity)](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/blob/main/LICENSE)

Plugin.Maui.NearbyConnectivity provides the ability to discover, connect to, and exchange data with nearby devices running your .NET MAUI app, without a server or an internet connection.

## Features

- **Discovery.** Advertise to nearby devices, and discover the devices that advertise the same `ServiceId`. Read them from `Devices`, or handle `DeviceChanged`.
- **[Connection consent](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/connect.html).** Answer each inbound connection request with `AcceptAsync` or `DeclineAsync`, or call `SetConsent` to set a policy that answers for you. An unanswered request times out.
- **[Bytes, files, and streams](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/payloads.html).** Send byte payloads up to 32 KiB, files with progress reporting, and streams (experimental, opt in with `NEARBY001`). Receive payloads from every connection in one `await foreach` loop. Received files are staged in the app's cache directory until you move or dispose them.
- **[Presence](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/presence.html).** Advertise a display name of up to 63 UTF-8 bytes and up to 57 bytes of app-defined data. Both are broadcast unencrypted to nearby devices before a connection exists.
- **[Encrypted connections](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/security.html).** Every connection is encrypted on both platforms. Each one exposes a six-digit `AuthenticationCode` that users compare out of band to detect a man-in-the-middle.
- **[Bindable types](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/bind-ui.html).** `NearbyDeviceCollection` implements `INotifyCollectionChanged`, and `NearbySessionState` implements `INotifyPropertyChanged`. Both raise their events on the UI thread, so XAML binds to them directly.
- **[Permissions and availability](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/platform-setup.html).** `NearbyPermission` works with `Permissions.RequestAsync<T>()` and covers every Android runtime permission the plugin needs. `CheckAvailabilityAsync` reports Bluetooth or Wi-Fi turned off, missing Google Play services, or a missing `Info.plist` entry.
- **Trimming and Native AOT.** The library is compatible with both.
- **[Logging](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/logging.html).** The plugin logs through `ILogger` with a stable `EventId` for each message. It registers no logging provider and sends no telemetry.

## Supported platforms

| Target | Minimum | Support |
| --- | --- | --- |
| `net10.0-android` | API 24 | Requires Google Play services. Built on [Nearby Connections](https://developers.google.com/nearby/connections/overview). |
| `net10.0-ios` | iOS 26.0 | Built on [Multipeer Connectivity](https://developer.apple.com/documentation/multipeerconnectivity). |
| `net10.0` | None | Reference target for shared code and unit tests. `CheckAvailabilityAsync` returns `NearbyAvailability.Unsupported`, and `StartAsync` throws `NearbyException`. |

> **No cross-platform connections.** Android connects only to Android, and iOS only to iOS. On iOS, the plugin stops the session when the app enters the background. On Android, a connection lives as long as the process, so it survives backgrounding only if your app runs a foreground service. See [Platform behavior](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/platform-behavior.html).

## Install

```bash
dotnet add package Plugin.Maui.NearbyConnectivity
```

Android needs no manifest changes. iOS needs two `Info.plist` keys. See [Set up Android and iOS](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/platform-setup.html).

This package replaces [`Plugin.Maui.NearbyConnections`](https://www.nuget.org/packages/Plugin.Maui.NearbyConnections). The 0.x previews under that name are deprecated and receive no fixes.

## Usage

### Register the plugin

Register the plugin in `MauiProgram.cs`. Devices discover each other only when they use the same `ServiceId`.

```csharp
using Plugin.Maui.NearbyConnectivity;

builder.AddNearbyConnectivity(options => options.ServiceId = "nearbyminimal");
```

`AddNearbyConnectivity` registers `INearbyConnectivity` as a singleton. Inject it, or use `NearbyConnectivity.Default` where constructor injection is not available. The same method exists on `IServiceCollection` for a host without a `MauiAppBuilder`, such as a unit test.

### Request permission and start

Advertising and discovery do not start automatically.

```csharp
if (await Permissions.RequestAsync<NearbyPermission>() != PermissionStatus.Granted)
{
    return;
}

nearbyConnectivity.Advertiser.SetConsent(NearbyConsentPolicy.AcceptAll);
await nearbyConnectivity.Advertiser.StartAsync();
await nearbyConnectivity.Discoverer.StartAsync();
```

`AcceptAll` accepts every inbound request without a prompt, so use it only in a demo. To prompt the user instead, see [Accept and make connections](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/connect.html).

### Send a message

Pick a `NearbyDevice` from `nearbyConnectivity.Devices`, call `ConnectAsync`, and send on the returned `NearbyConnection`.

```csharp
var connection = await device.ConnectAsync();
await connection.SendAsync("Hello"u8.ToArray());
```

### Receive messages

`PayloadsAsync` merges the inbound payloads from every connection into one `IAsyncEnumerable`. The stream is single-consumer, so enumerate it in one place.

```csharp
await foreach (var (sender, payload) in nearbyConnectivity.PayloadsAsync(cancellationToken))
{
    using (payload)
    {
        if (payload is NearbyBytesPayload bytes)
        {
            var text = Encoding.UTF8.GetString(bytes.Data.Span);
            Console.WriteLine($"{sender.Presence.DisplayName}: {text}");
        }
    }
}
```

These snippets follow [`samples/Minimal`](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/tree/main/samples/Minimal), the app from the [Get started](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/getting-started.html) tutorial.

## Demo

The `NearbyChat` sample on three Android phones: two discoverers connect to one advertiser, each phone stops advertising or discovering, each pair compares its authentication code, the advertiser sends one peer a text and a photo, the other peer sends the advertiser a video, and both peers disconnect. Click the image to watch it on the docs site.

[![Three phones running NearbyChat: connect, compare codes, send a text, a photo and a video, disconnect](https://raw.githubusercontent.com/phunkeler/Plugin.Maui.NearbyConnectivity/main/docs/public/demos/nearbychat-demo.png)](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/index.html#demo)

## Documentation

- [Get started](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/getting-started.html) builds a working two-device app in eight steps.
- How-to guides: [Accept and make connections](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/connect.html), [Send and receive payloads](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/payloads.html), [Show a presence before connecting](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/presence.html), [Bind to a MAUI page](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/bind-ui.html), [Test code that uses the plugin](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/test-your-app.html), [Troubleshooting](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/troubleshooting.html), and [Turn on logging](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/logging.html).
- [Security](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/security.html) and [Platform behavior](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/platform-behavior.html) document the plugin's limitations. Read both before you ship.
- [API reference](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/api/) and [Options and limits](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/configuration.html)
- [Samples](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/#samples): [`NearbyChat`](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/tree/main/samples/NearbyChat) shows consent prompts with a countdown, authentication code checks before the first send, presence avatars, file transfer with cancel and retry, and an Android foreground service that keeps connections open in the background.

## Feedback and contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/blob/main/CONTRIBUTING.md). Ask questions in [Discussions](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/discussions). Report bugs and request features with the issue forms.

Report vulnerabilities privately through [SECURITY.md](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/blob/main/SECURITY.md). Do not open a public issue.

Licensed under the [MIT License](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/blob/main/LICENSE).

## Acknowledgements

This plugin started from Gerald Versluis's [Plugin.Maui.Feature](https://github.com/jfversluis/Plugin.Maui.Feature) template.
