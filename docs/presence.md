# Show a presence before connecting

A presence is the data that other devices see before a connection exists. It is a display name and
up to 57 bytes of application-defined [`Info`](xref:Plugin.Maui.NearbyConnectivity.NearbyPresence.Info). Set the presence once at startup, or change it later
with [`SetPresence`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.SetPresence*).

The plugin broadcasts both members unencrypted to every device in range that knows the [`ServiceId`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.ServiceId).
The broadcast happens before encryption or consent applies, and any device can claim any presence.
Never put a secret, a token, or personal data in either member. Treat a peer's presence as a label
for filters and display, never as a credential. See [Security](security.md).

## Set the initial presence

```csharp
builder.AddNearbyConnectivity(o =>
{
    o.ServiceId = "cardtable";
    o.Presence = new() { Info = "table:T3"u8.ToArray() };
});
```

[`DisplayName`](xref:Plugin.Maui.NearbyConnectivity.NearbyPresence.DisplayName) defaults to `DeviceInfo.Name`, the name of this device, shortened to the limit. Set
`DisplayName` only to show a different name, such as a username or a nickname.

[`DeviceInfo.Name`](https://learn.microsoft.com/dotnet/api/microsoft.maui.devices.deviceinfo.name) does not reliably return a name that the user chose. On iOS 16 and later, it
returns a generic model string such as `iPhone`, unless the app has Apple's
`com.apple.developer.device-information.user-assigned-device-name` entitlement. Apple grants that
entitlement only on request. On Android, it returns the `device_name` setting. When that setting is
empty, it returns [`Build.Model`](https://learn.microsoft.com/dotnet/api/android.os.build.model), a string such as `SM-G991U`. When users must tell peers apart,
supply the name yourself.

## Change what peers see

`SetPresence` changes the presence that peers see. [`Presence`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.Presence) returns the presence that peers see
now, so bind to `Presence`.

A change applies the next time this device is idle. Idle means that advertising and discovery are
stopped, no connection is open, and no handshake is in progress. If you call `SetPresence` while
the device is idle, the change applies immediately and the next start announces it. If you call it
while the device is not idle, peers see the old presence until everything stops.

```csharp
nearby.SetPresence(nearby.Presence with { Info = "table:T4"u8.ToArray() });
Console.WriteLine(nearby.Presence.DisplayName);   // still the old value until then
```

To change the presence immediately, stop everything first:

```csharp
await nearby.StopAllAsync();
nearby.SetPresence(new() { DisplayName = "Alice's iPhone" });
await nearby.Advertiser.StartAsync();
```

The plugin validates both members and never shortens a value that you set. A name that is null,
empty, or longer than 63 bytes throws [`ArgumentException`](xref:System.ArgumentException), and so does an `Info` longer than 57
bytes. The exception names the member. An invalid presence therefore cannot exist, and a `with`
expression validates again. The name limit counts UTF-8 bytes, so a name with non-ASCII characters
reaches the limit at fewer characters. For both limits, see
[Options and limits](configuration.md#limits).

## Read a peer's info

`Info` is opaque to the plugin. The plugin checks its length, never interprets it, and copies it on
assignment. An array that you change afterwards therefore cannot change the advertised value. Text
takes one line in each direction:

```csharp
o.Presence = new() { Info = "table:T3"u8.ToArray() };

var text = Encoding.UTF8.GetString(device.Presence.Info.Span);
```

## Put structured data in the info

Fifty-seven bytes is a small budget, so use short property names. XML does not fit.

```csharp
public sealed record TableInfo(
    [property: JsonPropertyName("t")] string Table,
    [property: JsonPropertyName("s")] int OpenSeats);

[JsonSerializable(typeof(TableInfo))]
internal sealed partial class TableJson : JsonSerializerContext;   // trim and AOT safe

public static NearbyPresence For(string name, TableInfo info)
    => new()
    {
        DisplayName = name,
        Info = JsonSerializer.SerializeToUtf8Bytes(info, TableJson.Default.TableInfo),
    };

// Remote bytes are untrusted: never throw on them, never trust the shape.
public static TableInfo? Read(NearbyDevice device)
{
    if (device.Presence.Info.IsEmpty)
    {
        return null;
    }

    try
    {
        return JsonSerializer.Deserialize(device.Presence.Info.Span, TableJson.Default.TableInfo)
            is { Table.Length: > 0 and <= 8, OpenSeats: >= 0 and <= 8 } info
            ? info
            : null;
    }
    catch (JsonException)
    {
        return null;
    }
}
```

In your own tests, assert that the largest value fits, so the schema still fits if the limit
changes:

```csharp
Assert.True(
    JsonSerializer.SerializeToUtf8Bytes(Largest, TableJson.Default.TableInfo).Length
        <= NearbyPresence.MaxInfoBytes);
```

[`samples/NearbyChat/Services/PresenceAvatar.cs`](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/blob/main/samples/NearbyChat/Services/PresenceAvatar.cs)
puts an avatar choice in 13 bytes with this pattern. A peer whose info does not decode shows
initials.

## Next

- [Security](security.md) for why a presence is not a credential, and what to do instead.
- [Options and limits](configuration.md) for every other [`NearbyOptions`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions) member.
