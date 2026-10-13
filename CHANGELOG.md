# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Every user-facing pull
request adds a line under **Unreleased**; the release PR moves that section under the new version.

## [Unreleased]

### Added

- A demo video of the `NearbyChat` sample on three phones, on the docs home page and linked from the README.

## [1.0.0-preview.1] - 2026-10-10

First public preview, and the first release under this name. It replaces
`Plugin.Maui.NearbyConnections`, whose 0.x previews are deprecated and receive no fixes.

Android (API 24+, Google Play services) and iOS (26+) devices running your .NET MAUI app find,
connect to, and exchange data with each other, on the same platform only. A `net10.0` reference
target reports `NearbyAvailability.Unsupported` and every start throws, so shared code and unit
tests compile.

### Added

#### Registration and availability

- `builder.AddNearbyConnectivity(options => ...)` on `MauiAppBuilder` registers
  `INearbyConnectivity` and the validated `NearbyOptions` as singletons; the `IServiceCollection`
  overload serves a host without a builder, such as a unit test. `NearbyConnectivity.Default`
  reaches the session without dependency injection. Logging is optional: a host that never called `AddLogging()` gets
  `NullLogger`.
- `NearbyOptions`, validated at registration: `ServiceId` (required), `Presence`, `OfferWindow`,
  `DiscoveryRefreshInterval`, `FileTransferInactivityTimeout`, and `Android` (`Topology`,
  `ConnectionType`, `UseLowPower`). The `configure` delegate is required.
- `NearbyPermission` for `Permissions.RequestAsync`, covering the Android runtime permission set.
  The package declares the manifest permissions at assembly level. iOS has no runtime permission and
  reports `Granted`.
- `CheckAvailabilityAsync`, `Availability`, and the `AvailabilityChanged` event, reporting a
  `NearbyAvailability`: a radio off, missing permissions, Play services missing, and on iOS an
  `Info.plist` without `NSLocalNetworkUsageDescription` or without an `NSBonjourServices` entry for
  the `ServiceId` (`InvalidConfiguration`). Android observes radio state rather than polling it,
  and re-reads availability when the app returns to the foreground, so a permission granted in
  Settings takes effect on return.

#### Events

- `INearbyConnectivity.DeviceChanged`, `INearbyConnectivity.AvailabilityChanged`, and
  `INearbyActivity.Changed` on the advertiser and the discoverer. Every event is raised after its
  property holds the new value, one handler at a time, in one order across the session. A handler
  that throws is logged and the others still run. Subscribe first, then read the property.

#### Presence

- `NearbyPresence`, a display name of up to 63 UTF-8 bytes and up to 57 bytes of opaque `Info`, on
  `NearbyOptions.Presence`, `INearbyConnectivity.Presence`, and `NearbyDevice.Presence`. It rides in
  the advertisement and in every connection request on both platforms, so a list can be filtered and
  consent decided before a connection exists. `SetPresence` changes it at the next idle moment. Both
  members are public and unauthenticated: never put a secret or personal data in either.
  `DisplayName` defaults to the device's own name, cut to the cap. A remote name is cut to the same
  cap on both platforms and may be empty.

#### Advertising and discovery

- `INearbyConnectivity.Advertiser` (`INearbyAdvertiser`) and `INearbyConnectivity.Discoverer`
  (`INearbyDiscoverer`), each an `INearbyActivity` with `IsActive`, `StartAsync`, `StopAsync`,
  `LastStop`, and `Changed`. `LastStop` is a `NearbyActivityStop` holding a
  `NearbyActivityStopReason` (`Requested`, `Faulted`, `Suspended`) and the error, if any. The two
  activities are independent. The discoverer adds `RefreshAsync`; `StopAllAsync` on the root stops
  both and closes every connection. Cancelling a start stops the platform on the way out. The stops
  take no token: stop first, then cancel the tokens driving your own loops. Stopping the advertiser
  also cancels an automatic accept still in its handshake. `StopAllAsync` also cancels an outbound
  connect still in its handshake, and `ConnectAsync` then throws `NearbyConnectException` with
  `Failed`. Stopping the discoverer leaves an outbound connect running.

#### Devices

- `Devices` and the `DeviceChanged` event. `NearbyDevice` carries `Id` (minted locally, one session),
  `Presence`, `Status` (`Visible`, `RequestReceived`, `Connecting`, `Connected`), `Request`,
  `Connection`, and `ConnectExpiresAt`. Its constructor is public so a test can build one; a device
  built that way belongs to no session. Each `NearbyDeviceChange` carries an action (`Added`,
  `Updated`, `Removed`) and, on removal, a `NearbyEndReason`. A held request moves its device to
  `RequestReceived`, and an open connection moves it to `Connected`; both raise `DeviceChanged`. A decline, timeout, or disconnect returns a device to `Visible`; only a dropped
  connection or a real loss from discovery forgets it.

#### Connections

- Outbound `NearbyDevice.ConnectAsync` and `DisconnectAsync`. Inbound `NearbyConnectionRequest` on
  `NearbyDevice.Request`, with `AcceptAsync`, `DeclineAsync`, `ExpiresAt`, `ExpiresIn`, `Expired`, and
  `IsPending`. Race a prompt against `Expired` with `Task.WhenAny`. A request that arrives after
  advertising stopped is declined promptly. A request from a device that is already connected is
  declined and never reaches the app. When a connection to a device with a held request opens
  another way, such as both people tapping connect, `Expired` completes.
- `Advertiser.SetConsent(policy, decide)` with `NearbyConsentPolicy` `Hold`, `AcceptAll`, or
  `RejectAll`, and an optional `decide` callback per device that returns accept, decline, or `null`
  to fall through to the policy. `decide` takes a `CancellationToken` and may prompt a person: the
  plugin declines the request when the offer expires or advertising stops, cancels the token, and
  ignores a later answer. An automatically accepted handshake is bounded at 30 seconds; a person gets
  the initiator's full offer window.
- `Connections`, and `NearbyConnection` with `RemoteDevice`, `IsOpen`,
  `CloseAsync`, and `Closed`, a task that completes with the `NearbyEndReason`. `CloseAsync` and
  `DisconnectAsync` take no token, like the stops. `StopAllAsync` and
  disposal close connections concurrently.
- `NearbyConnection.AuthenticationCode`: six digits derived from the platform's own handshake and
  identical on both ends, so two people can rule out a machine relaying between them by reading the
  code to each other. Encryption alone proves nobody can read the traffic, not who is on the other
  end. The comparison has to happen over a channel the relay does not control, nothing enforces it,
  and the derivation may change in any release. On iOS each side also commits to a random nonce
  before either reveals one, so a relay cannot search for certificates that make its two codes agree,
  and every connection gets its own code, including a reconnect over a link that is still up. After
  three failed exchanges in which it revealed its nonce, an iOS device refuses new connections until
  advertising and discovery have stopped and no connection is open. When
  the handshake produces nothing to derive a code from, the plugin logs a warning and the code is
  empty.

#### Payloads

- `NearbyBytesPayload`, `NearbyFilePayload` (with `MoveTo`), and `NearbyStreamPayload`. Sends:
  `SendAsync(bytes)`, which completes when the platform accepts the bytes and is capped at
  `NearbyConnection.MaxBytesPayloadLength`; `SendFileAsync(FileResult)` and `SendFileAsync(path)`
  with `IProgress<NearbyTransferProgress>`, which complete when the transfer finishes; and
  `OpenStreamAsync(name)`. `NearbyTransferProgress.TotalBytes` is `null` when the size is unknown.
  `SendFileAsync(FileResult)` sends the result's `FileName`. `SendAsync` throws `ArgumentException`
  for bytes that start with the 4-byte signature of the plugin's control frames; a received frame
  with that signature that this version cannot read is dropped and logged.
- Streams are experimental: `OpenStreamAsync` and `NearbyStreamPayload` report build error
  `NEARBY001` until you add it to `NoWarn`, because they are not yet tested between two devices.
  Their behavior and API can change in any release.
- Every `NearbyPayload` is `IDisposable`: a stream payload closes its stream, a received file payload
  deletes its staged file unless `MoveTo` moved it, and a byte payload holds nothing. Undelivered
  payloads are disposed when their connection closes. Received files that were never moved or
  disposed are deleted when the app next creates the session.
- Receiving: `INearbyConnectivity.PayloadsAsync` merges every connection's payloads into
  `NearbyInboundPayload` (`RemoteDevice`, `Payload`). Each payload is delivered once, to one reader. The inbound buffer is unbounded: an app
  that stops reading owns the memory. A warning is logged once at 64 unread payloads.
- `NearbyConnection.InboundTransfersAsync` reports every inbound transfer's progress and ends it
  with a terminal `Completed`, `Failed`, or `Canceled` snapshot.
  `NearbyConnection.CancelInboundTransferAsync(payloadId)` cancels one inbound file and keeps the
  connection open; the sender's `SendFileAsync` then throws `NearbyTransferCanceledException`.

#### Binding to a MAUI page

- `NearbyDeviceCollection`, an `INotifyCollectionChanged` view of `Devices` with an optional
  filter, and `NearbySessionState`, an `INotifyPropertyChanged` view of availability, advertising,
  discovery, and `IsReady`. `AdvertisingStop` and `DiscoveryStop` are the session's last
  `NearbyActivityStop` for that activity, and clear when it starts again. Both types take an
  optional `IDispatcher` and raise their notifications on it. Constructed on that thread, they hold
  the session's current values when the constructor returns. If the collection's filter throws, it
  keeps its last devices and reports the failure through `IsFaulted` and `Exception` rather than
  throwing.

#### Errors

- `NearbyException`; `NearbyConnectException` with `NearbyConnectFailure` (`TimedOut`, `Failed`,
  `DeviceUnknown`), the only type a failed connect or accept throws; `NearbyTransferException`,
  `NearbyTransferTimeoutException`, and `NearbyTransferCanceledException` for a transfer the other
  device or the platform cancelled. Enum values are not part of the contract before 1.0: branch on
  the names.

#### Logging

- Structured `ILogger` output under the `Plugin.Maui.NearbyConnectivity` category with stable event
  ids, grouped by area in blocks of 100 from 1000 to 1799. A failure the plugin also throws to your
  code logs at `Debug`, because the exception is the report. Display names, payload bytes, and
  authentication codes are never logged.

#### Packaging, samples, and documentation

- Trimming and Native AOT compatible; `samples/NearbyChat` runs with the library trimmed. Source
  Link, deterministic builds, and a symbol package.
- Depends on `Microsoft.Maui.Core` 10.0.110 and `Microsoft.Extensions.*.Abstractions` 10.0.12, the
  latest stable releases.
- `samples/Minimal`, the app the getting-started tutorial is built from, and `samples/NearbyChat`, a
  chat that walks presence, discovery, consent, the authentication-code comparison, every send, and
  cancelling an inbound file.
- A documentation site at <https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/> covering
  getting started, connecting, presence, payloads, binding, configuration, platform behavior and
  setup, security, logging, testing your app, and troubleshooting.

[Unreleased]: https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/compare/v1.0.0-preview.1...HEAD
[1.0.0-preview.1]: https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/releases/tag/v1.0.0-preview.1
