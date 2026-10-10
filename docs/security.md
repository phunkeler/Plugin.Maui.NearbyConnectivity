# Security

A proximity network is an untrusted network. Any device in radio range that knows your [`ServiceId`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.ServiceId)
can discover this app and request a connection. Read this page before you release your app.

## What the plugin gives you

- **The link is encrypted.** Both platforms always encrypt every connection, and nothing in the API
  can reduce that encryption.
- **Inbound file names are safe to write.** The plugin removes every directory component from a
  file name, so a name such as `../../databases/app.db` cannot leave the staging directory.
- **Device ids are random.** [`NearbyDevice.Id`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.Id) is 16 hex characters from a cryptographically secure
  source. It is never a platform identifier.
- **Every connection has an authentication code.** Two people compare six digits out loud. See
  [Compare the code](#compare-the-code).

## What the plugin does not give you

**Neither platform authenticates the peer.** Encryption proves that nobody else can read the
traffic. It does not prove who is on the other end.

- **A presence is public and unverified.** The peer chooses [`NearbyDevice.Presence`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.Presence), and the peer's
  device broadcasts it unencrypted before consent applies. Never put a secret, a token, or personal
  data in your own presence. Never use a peer's presence as an identity or for an authorization
  decision.
- **A display name is untrusted text.** [`DisplayName`](xref:Plugin.Maui.NearbyConnectivity.NearbyPresence.DisplayName) arrives as the peer sent it. It may contain
  bidirectional control characters, and it may be empty. Show it in a single-line control.
- **A device id lasts one session.** `NearbyDevice.Id` changes between sessions, and it is not a
  credential. Do not persist it.
- **`AcceptAll` accepts every device.** [`NearbyConsentPolicy.AcceptAll`](xref:Plugin.Maui.NearbyConnectivity.NearbyConsentPolicy.AcceptAll) accepts every request from
  any device that knows the `ServiceId`. Use it only in a closed environment, such as a kiosk or a
  demo. In a shipping app, leave the policy at [`Hold`](xref:Plugin.Maui.NearbyConnectivity.NearbyConsentPolicy.Hold).
- **A `decide` callback is not an authorization check.** It sees only the device id and the
  presence. To act on who connects, accept the request first, and then authenticate over the
  connection with a token, a pairing code, or a signature.

## Compare the code

[`NearbyConnection.AuthenticationCode`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.AuthenticationCode) is six digits, derived from the handshake and identical on
both ends. Show the code to the person at each device, and have the two people read it to each
other.

```csharp
var connection = await device.ConnectAsync(cancellationToken);

// "Does your screen say 482107?"
codeLabel.Text = connection.AuthenticationCode;
```

A machine in radio range of both people can hold one encrypted connection to each of them and relay
between the two. That relay reads everything, but it cannot make the two codes agree. Matching
codes mean that the two devices completed one handshake with each other. Different codes mean that
something sits between the devices.

- **Compare the code out loud.** A code that you send over the connection proves nothing, because a
  relay reads it and repeats it.
- **Close the connection on a mismatch.** An empty code is also a mismatch, and a relay can cause
  one on purpose. Check for six digits before you ask anyone to compare.
- **Block your own sends until a person confirms.** The plugin does not enforce the comparison.
- **Let a person accept each request.** A relay has a one-in-a-million chance on each attempt, and
  it can try again. With automatic accept, nobody sees the attempts. On iOS, the plugin refuses new
  connections after three failed attempts, until advertising and discovery stop and no connection
  is open.
- **Do not persist a code or compare it with a stored value.** The derivation can change in any
  release.

```csharp
if (connection.AuthenticationCode.Length != 6)
{
    await connection.CloseAsync();
}
```

[`samples/NearbyChat/Services/ConnectionConfirmations.cs`](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/blob/main/samples/NearbyChat/Services/ConnectionConfirmations.cs)
blocks sends until a person confirms the code. It forgets the confirmation when the connection
closes, so a reconnect asks again.

The comparison ties the connection to a person in the room, not to an account. On iOS, the code
also depends on an undocumented behavior of Multipeer Connectivity. The plugin assumes that the
certificate reported for a peer is the certificate that the peer proved it holds.

## Prove membership before connecting

To make casual forgery of a presence harder, put a keyed tag in [`Info`](xref:Plugin.Maui.NearbyConnectivity.NearbyPresence.Info) and check the tag in
`decide`. Compute the tag over the display name and the remaining bytes of `Info`, so that a tag
is valid for one presence only. The tag uses part of the 57-byte limit. For example, an
HMAC-SHA256 tag truncated to 16 bytes leaves 41 bytes for your data.

Do not put the key in the binary.

**This defeats casual spoofing, not replay.** A broadcast contains no challenge, so any device that
observes a valid presence can broadcast it again. To prove identity, wait until the connection is
open. Then use a nonce with an HMAC or a signature. The plugin signs nothing itself.

## Treat every payload as hostile input

Payload bytes and file contents come from an unauthenticated sender. The plugin delivers them
unchanged and does not inspect them.

- Check the size of the bytes and validate them before you parse them. Apply the same rule to
  `Presence.Info`.
- Do not trust an inbound file's name or extension to describe its contents.
- Inbound files have no size limit. Watch [`TotalBytes`](xref:Plugin.Maui.NearbyConnectivity.NearbyTransferProgress.TotalBytes) in [`InboundTransfersAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.InboundTransfersAsync*). If a transfer is
  larger than your app expects, call [`CancelInboundTransferAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.CancelInboundTransferAsync*) with its [`PayloadId`](xref:Plugin.Maui.NearbyConnectivity.NearbyTransferProgress.PayloadId). The
  connection stays open. See [Send and receive payloads](payloads.md#send-a-file).
- Unread payloads stay in memory with no limit. Read [`PayloadsAsync(ct)`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.PayloadsAsync*) for the life of the app,
  or close the connection.
- File names that the sender supplied appear in the plugin's logs at [`Debug`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.loglevel#microsoft-extensions-logging-loglevel-debug), and at [`Error`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.loglevel#microsoft-extensions-logging-loglevel-error) when
  a transfer fails. See [Turn on logging](logging.md).
- [`SendAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.SendAsync*) throws `ArgumentException` for a byte payload that the receiving plugin would read
  as one of its own control frames. Such a payload starts with the bytes `43 4E 4D 50`. If your
  app forwards bytes from another source, do not let that source choose the first bytes of a
  payload.

## Next

- [Show a presence before connecting](presence.md) for what a presence can carry.
- [Accept and make connections](connect.md) to decide which devices connect.
