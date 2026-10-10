---
_disableToc: true
---

# Plugin.Maui.NearbyConnectivity

<p class="lead">Plugin.Maui.NearbyConnectivity provides the ability to discover, connect to, and exchange data with nearby devices that run your .NET MAUI application, without a server or an internet connection.</p>

<p>
  <a class="btn btn-primary me-2 mb-2" href="getting-started.md"><i class="bi bi-play-fill" aria-hidden="true"></i> Get started</a>
  <a class="btn btn-outline-secondary me-2 mb-2" href="api/index.md"><i class="bi bi-braces" aria-hidden="true"></i> API reference</a>
  <a class="btn btn-outline-secondary me-2 mb-2" href="https://www.nuget.org/packages/Plugin.Maui.NearbyConnectivity"><i class="bi bi-box-seam" aria-hidden="true"></i> NuGet</a>
  <a class="btn btn-outline-secondary mb-2" href="https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity"><i class="bi bi-github" aria-hidden="true"></i> GitHub</a>
</p>

## Supported platforms

| Target | Minimum | Support |
| --- | --- | --- |
| `net10.0-android` | API 24 | Requires Google Play services. Built on [Nearby Connections](https://developers.google.com/nearby/connections/overview). |
| `net10.0-ios` | iOS 26.0 | Built on [Multipeer Connectivity](https://developer.apple.com/documentation/multipeerconnectivity). |
| `net10.0` | None | Reference target for shared code and unit tests. [`CheckAvailabilityAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.CheckAvailabilityAsync*) reports [`NearbyAvailability.Unsupported`](xref:Plugin.Maui.NearbyConnectivity.NearbyAvailability.Unsupported), and every start method throws. |

> [!IMPORTANT]
> A device connects only to devices on the same platform. Android connects to Android, and iOS
> connects to iOS. On iOS, the plugin stops the session when the app moves to the background. On
> Android, a connection ends when the process ends, so it survives the background only if your app
> runs a foreground service. See
> [why the platforms do not interoperate](platform-behavior.md#why-the-platforms-do-not-interoperate)
> and [backgrounding](platform-behavior.md#connections-do-not-survive-backgrounding).

## Start here

<div class="row row-cols-1 row-cols-md-2 g-3 mb-4">
  <div class="col">
    <div class="card h-100">
      <div class="card-body">
        <p class="card-title h5"><i class="bi bi-phone me-1" aria-hidden="true"></i> Connect two phones, step by step</p>
        <ul class="list-unstyled mb-0">
          <li><a href="getting-started.md">Get started</a></li>
        </ul>
      </div>
    </div>
  </div>
  <div class="col">
    <div class="card h-100">
      <div class="card-body">
        <p class="card-title h5"><i class="bi bi-list-check me-1" aria-hidden="true"></i> Do one specific thing</p>
        <ul class="list-unstyled mb-0">
          <li><a href="connect.md">Accept and make connections</a></li>
          <li><a href="payloads.md">Send and receive payloads</a></li>
          <li><a href="presence.md">Show a presence before connecting</a></li>
          <li><a href="bind-ui.md">Bind to a MAUI page</a></li>
          <li><a href="test-your-app.md">Test code that uses the plugin</a></li>
        </ul>
      </div>
    </div>
  </div>
  <div class="col">
    <div class="card h-100">
      <div class="card-body">
        <p class="card-title h5"><i class="bi bi-wrench me-1" aria-hidden="true"></i> Fix something that is not working</p>
        <ul class="list-unstyled mb-0">
          <li><a href="troubleshooting.md">Troubleshooting</a></li>
          <li><a href="logging.md">Turn on logging</a></li>
        </ul>
      </div>
    </div>
  </div>
  <div class="col">
    <div class="card h-100">
      <div class="card-body">
        <p class="card-title h5"><i class="bi bi-book me-1" aria-hidden="true"></i> Look up a type or a limit</p>
        <ul class="list-unstyled mb-0">
          <li><a href="api/index.md">API reference</a></li>
          <li><a href="configuration.md">Options and limits</a></li>
        </ul>
      </div>
    </div>
  </div>
  <div class="col">
    <div class="card h-100">
      <div class="card-body">
        <p class="card-title h5"><i class="bi bi-sign-stop me-1" aria-hidden="true"></i> Know what the plugin cannot do</p>
        <ul class="list-unstyled mb-0">
          <li><a href="platform-behavior.md">Platform behavior</a></li>
          <li><a href="security.md">Security</a></li>
        </ul>
      </div>
    </div>
  </div>
  <div class="col">
    <div class="card h-100">
      <div class="card-body">
        <p class="card-title h5"><i class="bi bi-code-slash me-1" aria-hidden="true"></i> Read a complete app</p>
        <ul class="list-unstyled mb-0">
          <li><a href="#samples">Samples</a></li>
        </ul>
      </div>
    </div>
  </div>
</div>

## Install

```bash
dotnet add package Plugin.Maui.NearbyConnectivity
```

Android needs no manifest changes. iOS needs two `Info.plist` keys. See
[Set up Android and iOS](platform-setup.md).

Then follow [Get started](getting-started.md), which takes about ten minutes.

## Samples

The repository has two sample apps.

- [`samples/Minimal`](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/tree/main/samples/Minimal)
  is the app that the [Get started](getting-started.md) tutorial builds. It is MAUI in code only,
  in four files plus the platform files.
- [`samples/NearbyChat`](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/tree/main/samples/NearbyChat)
  is the larger app. The how-to pages link to the file that does each task:

  | Task | Page |
  | --- | --- |
  | Answer a request before it expires | [Handle a request that expires](connect.md#handle-a-request-that-expires) |
  | Block sends until a person compares the code | [Compare the code](security.md#compare-the-code) |
  | Put an avatar in the presence | [Put structured data in the info](presence.md#put-structured-data-in-the-info) |
  | Send a file, and handle each failure | [Send a file](payloads.md#send-a-file) |
  | Receive in one loop for the whole app | [Receive data](payloads.md#receive-data) |
  | Bind connected devices, and rows with their own state | [Bind to a MAUI page](bind-ui.md) |
  | Stay connected in the background on Android | [Keep a connection open in the background](platform-setup.md#keep-a-connection-open-in-the-background) |
