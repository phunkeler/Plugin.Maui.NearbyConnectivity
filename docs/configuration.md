# Options and limits

The plugin reads all [`NearbyOptions`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions) values once at startup. Set them in the configure delegate of
[`AddNearbyConnectivity(...)`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectivityAppBuilderExtensions.AddNearbyConnectivity*), shown in [Register the plugin](getting-started.md#2-register-the-plugin).
A change after startup has no effect.

The presence is the one value that can change later, through
[`SetPresence`](presence.md#change-what-peers-see). The `Presence` option is its initial value.

[`NearbyOptions.Android`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.Android) exists on every target, so shared code compiles without `#if`. On other
platforms it has no effect.

## Options

| Member | Platform | Default | Description |
| --- | --- | --- | --- |
| [`ServiceId`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.ServiceId) | Both | None. **Required.** | The identifier that both devices must share. Only devices with the same `ServiceId` find each other. 1 to 15 characters: ASCII lowercase letters, digits, and hyphens. At least one letter. No hyphen at the start, at the end, or next to another hyphen. On iOS, it is also the service type that the system advertises. See [Set up Android and iOS](platform-setup.md#ios). Startup validation throws if the value is not set or is invalid. |
| [`Presence`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.Presence) | Both | `DeviceInfo.Name`, empty info | The data that other devices see before a connection exists. It has a [`DisplayName`](xref:Plugin.Maui.NearbyConnectivity.NearbyPresence.DisplayName) of at most 63 UTF-8 bytes and at most 57 bytes of opaque [`Info`](xref:Plugin.Maui.NearbyConnectivity.NearbyPresence.Info). The option sets the initial value only. See [Show a presence before connecting](presence.md). **Public and unverified.** The plugin broadcasts it unencrypted, and any device can claim any presence. |
| [`OfferWindow`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.OfferWindow) | Both | 2 minutes | The time that a connect or an inbound request waits for an answer, on both ends. At most 30 minutes. Startup validation rejects zero, negative values, and `Timeout.InfiniteTimeSpan`. The value travels with the request, so the peer's prompt, through [`NearbyConnectionRequest.ExpiresAt`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectionRequest.ExpiresAt), and an accepted handshake expire at the same moment. When the time elapses, [`ConnectAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.ConnectAsync*) and [`AcceptAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectionRequest.AcceptAsync*) throw [`NearbyConnectException`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectException) with [`TimedOut`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectFailure.TimedOut). A request that a consent policy answers has a separate limit. See [Consent policies](#consent-policies). |
| [`DiscoveryRefreshInterval`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.DiscoveryRefreshInterval) | Both | `null`, never restarts | The interval at which discovery restarts to find which devices are still in range. The plugin removes devices that the new pass does not report again. It never removes a device that is connected or in a handshake. With the default, discovery never restarts on its own, and [`Discoverer.RefreshAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyDiscoverer.RefreshAsync*) restarts it on request. |
| [`FileTransferInactivityTimeout`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.FileTransferInactivityTimeout) | Both | 10 seconds | Outbound only. If a file send makes no progress within this time, the plugin abandons the send with [`NearbyTransferTimeoutException`](xref:Plugin.Maui.NearbyConnectivity.NearbyTransferTimeoutException). `null` disables the check, as it does for `DiscoveryRefreshInterval`. An inbound transfer that stops has no time limit: watch [`InboundTransfersAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.InboundTransfersAsync*) and call [`CancelInboundTransferAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.CancelInboundTransferAsync*). |
| [`Android.Topology`](xref:Plugin.Maui.NearbyConnectivity.NearbyAndroidOptions.Topology) | Android | [`NearbyTopology.Cluster`](xref:Plugin.Maui.NearbyConnectivity.NearbyTopology.Cluster) | How devices can connect. `Cluster` is many-to-many. [`Star`](xref:Plugin.Maui.NearbyConnectivity.NearbyTopology.Star) is one-to-many. [`PointToPoint`](xref:Plugin.Maui.NearbyConnectivity.NearbyTopology.PointToPoint) is one-to-one, with the highest bandwidth. The advertising device and the discovering device must use the same value, or they never find each other. |
| [`Android.ConnectionType`](xref:Plugin.Maui.NearbyConnectivity.NearbyAndroidOptions.ConnectionType) | Android | [`NearbyConnectionType.Balanced`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectionType.Balanced) | How hard the radio works after a connection opens: `Balanced`, [`HighBandwidth`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectionType.HighBandwidth), or [`NonDisruptive`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectionType.NonDisruptive). |
| [`Android.UseLowPower`](xref:Plugin.Maui.NearbyConnectivity.NearbyAndroidOptions.UseLowPower) | Android | `false` | When `true`, the plugin uses only low-power mediums such as BLE. The result is less battery use, less range, and less throughput. |

Inbound consent is not an option, because it can change at run time. [`Advertiser.SetConsent`](xref:Plugin.Maui.NearbyConnectivity.INearbyAdvertiser.SetConsent*) sets
it. See [Answer an inbound request](connect.md#answer-an-inbound-request).

## Limits

This table lists every fixed number that the plugin enforces. The plugin validates each one and
never truncates a value silently.

| What | Limit | Where it applies |
| --- | --- | --- |
| `ServiceId` length | 1 to 15 characters | Startup validation throws outside this range |
| `NearbyPresence.DisplayName` | 63 UTF-8 bytes, [`NearbyPresence.MaxDisplayNameBytes`](xref:Plugin.Maui.NearbyConnectivity.NearbyPresence.MaxDisplayNameBytes) | `ArgumentException` that names the member |
| `NearbyPresence.Info` | 57 bytes, [`NearbyPresence.MaxInfoBytes`](xref:Plugin.Maui.NearbyConnectivity.NearbyPresence.MaxInfoBytes) | `ArgumentException` that names the member |
| A bytes payload | 32 KiB, [`NearbyConnection.MaxBytesPayloadLength`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.MaxBytesPayloadLength) | [`SendAsync(ReadOnlyMemory<byte>)`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.SendAsync*) throws `ArgumentOutOfRangeException`. A file or a stream carries more |
| A stream name | 1024 UTF-8 bytes | [`OpenStreamAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.OpenStreamAsync*) throws |
| `OfferWindow` | 30 minutes | Startup validation rejects a longer value, zero, negative values, and [`Timeout.InfiniteTimeSpan`](xref:System.Threading.Timeout.InfiniteTimeSpan). The plugin clamps a value that a peer declared to the same limit on arrival. |
| Unread payloads on one connection | None. At 64, the plugin logs a warning | The plugin drops nothing and pauses nothing. See [the buffer has no bound](payloads.md#receive-data) |

The 57-byte `Info` limit comes from the plugin's connect request frame. It is the space that remains
after the frame header, the offer window, and a 63-byte name of maximum length. Both limits come
from the frame, not from one platform, so they apply on both platforms.

An inbound file has no size limit. See
[Treat every payload as hostile input](security.md#treat-every-payload-as-hostile-input).

## Consent policies

A consent policy controls how the plugin answers inbound connection requests.
`Advertiser.SetConsent` is the only way to set it, and each call replaces both the policy and the
`decide` callback. To answer requests yourself, see
[Answer an inbound request](connect.md#answer-an-inbound-request).

| Policy | What it does |
| --- | --- |
| [`Hold`](xref:Plugin.Maui.NearbyConnectivity.NearbyConsentPolicy.Hold), the default | Moves the device to [`RequestReceived`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceStatus.RequestReceived) with the request on [`device.Request`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.Request), and waits |
| [`AcceptAll`](xref:Plugin.Maui.NearbyConnectivity.NearbyConsentPolicy.AcceptAll) | Accepts every request for you. The device moves directly to [`Connected`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceStatus.Connected) and never reports `RequestReceived`, so a UI that waits only for requests shows nothing |
| [`RejectAll`](xref:Plugin.Maui.NearbyConnectivity.NearbyConsentPolicy.RejectAll) | Declines every request for you |

A request that the policy answers for you does not use the offer window. It has 30 seconds to
complete its handshake. The offer window covers delivery, the decision of a person, and the
handshake, and a policy that answers removes the decision. When the 30 seconds elapse, the plugin
abandons the handshake, the device returns to [`Visible`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceStatus.Visible), and the plugin logs a warning. A request
that a person answers through `AcceptAsync` still has the full window that the requesting device
declared.

## Connection end reasons

[`NearbyConnection.Closed`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.Closed) completes with one of these four values. [`NearbyEndReason`](xref:Plugin.Maui.NearbyConnectivity.NearbyEndReason) has ten
values. The other six describe a handshake or a change to [`Devices`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.Devices), and appear in
[`NearbyDeviceChange.Reason`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceChange.Reason) instead.

| Reason | What happened |
| --- | --- |
| [`ClosedLocally`](xref:Plugin.Maui.NearbyConnectivity.NearbyEndReason.ClosedLocally) | This side called [`CloseAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.CloseAsync*) or [`DisconnectAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.DisconnectAsync*) |
| [`PeerDisconnected`](xref:Plugin.Maui.NearbyConnectivity.NearbyEndReason.PeerDisconnected) | The peer closed the connection, or the link was lost. Neither platform distinguishes the two |
| [`SessionStopped`](xref:Plugin.Maui.NearbyConnectivity.NearbyEndReason.SessionStopped) | [`StopAllAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.StopAllAsync*) stopped the session |
| [`AppSuspended`](xref:Plugin.Maui.NearbyConnectivity.NearbyEndReason.AppSuspended) | The plugin stopped the session because the iOS app moved to the background |

## Next

- [Show a presence before connecting](presence.md) to change the presence at run time.
- [Set up Android and iOS](platform-setup.md) for the manifest and `Info.plist` entries.
