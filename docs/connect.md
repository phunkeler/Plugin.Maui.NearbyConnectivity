# Accept and make connections

A connection opens in one of two directions. Either a peer sends you a request and you answer it,
or you call [`ConnectAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.ConnectAsync*) on a peer. A peer is a nearby device that runs your app.

## Answer an inbound request

Give the advertiser a `decide` callback. The plugin calls the callback one time for each request,
on a separate task, and applies the value that the callback returns:

- `true` accepts the request.
- `false` declines the request.
- `null` holds the request for a person to answer later.

```csharp
nearby.Advertiser.SetConsent(
    NearbyConsentPolicy.Hold,
    decide: async (device, cancellationToken) =>
    {
        // decide runs on a thread-pool thread, so dispatch to the UI thread first.
        var prompt = _dispatcher.DispatchAsync(() => page.DisplayAlertAsync(
            "Connect?", device.Presence.DisplayName, "Accept", "Decline"));

        return await prompt.WaitAsync(cancellationToken);
    });

await nearby.Advertiser.StartAsync(cancellationToken);
```

When the deadline of the request passes or advertising stops, the plugin cancels the token and
declines the request. MAUI alerts accept no token, so the example waits on the token with
[`WaitAsync`](https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.waitasync). If the callback throws, the plugin logs the exception and holds the request.

A held request stays on its device. The device reports `Status == RequestReceived`, and
[`device.Request`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.Request) returns the request. A row can therefore call [`AcceptAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectionRequest.AcceptAsync*) or [`DeclineAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectionRequest.DeclineAsync*) from
its own buttons.

The callback can also answer without a prompt, for example from [`device.Presence.Info`](xref:Plugin.Maui.NearbyConnectivity.NearbyPresence.Info). Do not
treat that answer as authorization. [`device.Id`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.Id) lasts one session, and the peer chooses its own
presence, so neither is a credential. See
[What the plugin does not give you](security.md#what-the-plugin-does-not-give-you) and
[Prove membership before connecting](security.md#prove-membership-before-connecting).

Each [`SetConsent`](xref:Plugin.Maui.NearbyConnectivity.INearbyAdvertiser.SetConsent*) call replaces both the policy and the `decide` callback. For the three policies,
see [Consent policies](configuration.md#consent-policies).

## Handle a request that expires

Every request has one deadline, which comes from the [`OfferWindow`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.OfferWindow) that the requesting device
declared. If nobody answers a held request before the deadline, the plugin declines the request and
the device returns to [`Visible`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceStatus.Visible).

[`request.Expired`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectionRequest.Expired) completes at the deadline. To prompt for a held request, race the prompt against
`request.Expired`:

```csharp
var prompt = page.DisplayAlertAsync(
    "Connect?", request.RemoteDevice.Presence.DisplayName, "Accept", "Decline");

if (await Task.WhenAny(prompt, request.Expired) != prompt)
{
    // The request expired while the prompt was open. The plugin already declined it.
    return;
}

await (await prompt ? request.AcceptAsync(ct) : request.DeclineAsync(ct));
```

An answer can still arrive a moment after the deadline. In that case, `AcceptAsync` throws
[`NearbyConnectException`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectException) with [`TimedOut`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectFailure.TimedOut), so catch that exception. For a countdown, read
[`request.ExpiresIn`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectionRequest.ExpiresIn) from your own UI timer.
[`samples/NearbyChat/ViewModels/AdvertisedDeviceViewModel.cs`](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/blob/main/samples/NearbyChat/ViewModels/AdvertisedDeviceViewModel.cs)
answers held requests from a row that counts down to [`request.ExpiresAt`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectionRequest.ExpiresAt).

## Connect to a device

Discovered devices appear in [`nearby.Devices`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.Devices) with `Status == Visible`:

```csharp
NearbyConnection connection = await device.ConnectAsync(cancellationToken);
```

You do not have to check [`Status`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.Status) first, and a second call is not an error:

- If the device is already connected, `ConnectAsync` returns the existing connection.
- If a handshake is already in progress, `ConnectAsync` joins it.
- If the device sent you a request, `ConnectAsync` accepts that request.

A row that was bound earlier still works, because `ConnectAsync` finds the device in the session at
the moment you call it. To connect to an id that you stored, find the device in `nearby.Devices`
first.

## Confirm the connection

The transport encrypts an open connection. The connection proves only that a device is on the other
end, not which person. [`connection.AuthenticationCode`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.AuthenticationCode) is six digits, identical on both ends. Show
the code on both screens, and have the two people read it to each other. Treat an empty code as a
mismatch.

The plugin does not enforce the comparison. If your app needs confirmation, block your own sends
until a person confirms the code. See [Compare the code](security.md#compare-the-code).

## Handle a connect that fails

If the handshake produces no connection, `ConnectAsync` throws `NearbyConnectException` and the
device returns to `Visible`. One exception type covers every connect failure, so branch on
[`Reason`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectException.Reason):

```csharp
try
{
    await device.ConnectAsync(cancellationToken);
}
catch (NearbyConnectException ex) when (ex.Reason is NearbyConnectFailure.TimedOut)
{
    // Nobody answered within OfferWindow, or the device moved out of range. Retrying is reasonable.
}
catch (NearbyConnectException ex) when (ex.Reason is NearbyConnectFailure.Failed)
{
    // The peer declined, or the handshake failed. The two are not distinguishable.
}
catch (NearbyConnectException ex) when (ex.Reason is NearbyConnectFailure.DeviceUnknown)
{
    // The device left Devices before the handshake began.
}
```

[`NearbyConnectFailure`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectFailure) has no `Declined` value. iOS does not tell the requesting device that a peer
declined, so the plugin reports [`Failed`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectFailure.Failed) on both platforms. If you cancel your own token,
`ConnectAsync` throws [`OperationCanceledException`](xref:System.OperationCanceledException) instead.

## Know when a connection closes

When a connection closes, its [`Closed`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.Closed) task completes with the reason. The task never faults and is
never canceled:

```csharp
NearbyEndReason reason = await connection.Closed;
```

For the four values, see [Connection end reasons](configuration.md#connection-end-reasons).

Every change to a device's `Status` also raises [`nearby.DeviceChanged`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.DeviceChanged). The plugin raises all
events from one loop, in order, and never on the UI thread. A slow handler delays every handler
after it, so keep handlers short and dispatch to the UI thread. To avoid the handler, bind to a
[`NearbyDeviceCollection`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceCollection). See [Bind to a MAUI page](bind-ui.md).

## Stop advertising or discovering

Advertising and discovery are independent, and a stop of one leaves the other active:

```csharp
await nearby.Advertiser.StopAsync();   // Declines pending requests.
await nearby.Discoverer.StopAsync();   // Removes devices that were only Visible from Devices.
```

Neither stop closes an open connection. To also disconnect every peer, call [`StopAllAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.StopAllAsync*).

[`IsActive`](xref:Plugin.Maui.NearbyConnectivity.INearbyActivity.IsActive) and the [`Changed`](xref:Plugin.Maui.NearbyConnectivity.INearbyActivity.Changed) event of each activity tell you whether it is active.
[`LastStop.Reason`](xref:Plugin.Maui.NearbyConnectivity.NearbyActivityStop.Reason) tells you how the activity last stopped: [`Requested`](xref:Plugin.Maui.NearbyConnectivity.NearbyActivityStopReason.Requested), [`Faulted`](xref:Plugin.Maui.NearbyConnectivity.NearbyActivityStopReason.Faulted), or [`Suspended`](xref:Plugin.Maui.NearbyConnectivity.NearbyActivityStopReason.Suspended).
`Faulted` is the only reason that a user needs to see. To find what prevents a restart, call
[`CheckAvailabilityAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.CheckAvailabilityAsync*).

## Next

- [Send and receive payloads](payloads.md) after a connection opens.
- [Troubleshooting](troubleshooting.md) if a connect never completes.
