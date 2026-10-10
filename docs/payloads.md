# Send and receive payloads

You send through a [`NearbyConnection`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection). [`request.AcceptAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectionRequest.AcceptAsync*), [`ConnectAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.ConnectAsync*), and
[`device.Connection`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.Connection) each return the same instance. Read the connection from the device each time,
and do not cache it. `device.Connection` returns `null` after the connection closes.

```csharp
if (device.Connection is { } connection)
{
    await connection.SendAsync(data, cancellationToken);
}
```

## What a completed send means

The three send methods complete at different points, and none of them tells you that the peer's
app received data.

| Call | Completes when |
| --- | --- |
| [`SendAsync(bytes)`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.SendAsync*) | The platform accepts the bytes for sending |
| [`SendFileAsync(file)`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.SendFileAsync*), `SendFileAsync(path)` | The transfer finishes |
| [`OpenStreamAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.OpenStreamAsync*) | The stream opens. Your code controls everything after that |

**A completed `SendAsync(bytes)` is not a delivery receipt.** Neither platform reports that the
peer's app read a payload, and the peer may have moved out of range a moment later. If your app
must know that the peer stored the work, put an identifier on the payload. Then have the peer's app
send the identifier back after it saves the work.

## Send bytes

```csharp
byte[] data = Encoding.UTF8.GetBytes("Hello!");
await connection.SendAsync(data, cancellationToken);
```

The limit is [`NearbyConnection.MaxBytesPayloadLength`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.MaxBytesPayloadLength), which is 32 KiB on both platforms. A larger
payload throws [`ArgumentOutOfRangeException`](xref:System.ArgumentOutOfRangeException). Send larger data as a file or a stream.

## Send a file

```csharp
// A picker result: pass it directly.
await connection.SendFileAsync(fileResult, cancellationToken: cancellationToken);

// A path that you already have, or a content:// URI on Android.
var path = Path.Combine(FileSystem.CacheDirectory, "file.bin");
await connection.SendFileAsync(path, cancellationToken: cancellationToken);
```

When a picker result has no file path, the plugin copies the file to a temporary directory, sends
the copy, and then deletes it. The device needs free space for that copy.

To show progress, pass an [`IProgress<NearbyTransferProgress>`](xref:System.IProgress%601). [`TotalBytes`](xref:Plugin.Maui.NearbyConnectivity.NearbyTransferProgress.TotalBytes) and [`Fraction`](xref:Plugin.Maui.NearbyConnectivity.NearbyTransferProgress.Fraction) are
`null` when the total size is not known in advance. For inbound progress, read
[`connection.InboundTransfersAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection.InboundTransfersAsync*).

To refuse an inbound file, cancel it with the [`PayloadId`](xref:Plugin.Maui.NearbyConnectivity.NearbyTransferProgress.PayloadId) from its progress. The connection stays
open. The last progress for the transfer has the status [`Canceled`](xref:Plugin.Maui.NearbyConnectivity.NearbyTransferStatus.Canceled), or [`Completed`](xref:Plugin.Maui.NearbyConnectivity.NearbyTransferStatus.Completed) if the transfer
finished first. `SendFileAsync` on the sender then throws [`NearbyTransferCanceledException`](xref:Plugin.Maui.NearbyConnectivity.NearbyTransferCanceledException).

```csharp
await foreach (var progress in connection.InboundTransfersAsync(cancellationToken))
{
    if (progress.TotalBytes > MaxFileBytes)
    {
        await connection.CancelInboundTransferAsync(progress.PayloadId);
    }
}
```

A file send can fail in these ways. Catch the exceptions in this order, because each type derives
from a type lower in the table.

| Exception | Cause |
| --- | --- |
| [`OperationCanceledException`](xref:System.OperationCanceledException) | Your `cancellationToken` was canceled. |
| [`NearbyTransferTimeoutException`](xref:Plugin.Maui.NearbyConnectivity.NearbyTransferTimeoutException) | No progress within [`NearbyOptions.FileTransferInactivityTimeout`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.FileTransferInactivityTimeout). |
| `NearbyTransferCanceledException` | The other device or the platform canceled the transfer. |
| [`NearbyTransferException`](xref:Plugin.Maui.NearbyConnectivity.NearbyTransferException) | The file is not readable, or the platform returned an error. |
| [`NearbyException`](xref:Plugin.Maui.NearbyConnectivity.NearbyException) | The connection closed before the call. |

[`samples/NearbyChat/ViewModels/ChatViewModel.cs`](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/blob/main/samples/NearbyChat/ViewModels/ChatViewModel.cs)
sends files with progress, and shows a retry message for each exception.

## Open a stream

> [!WARNING]
> Streams are experimental. Nobody has tested them between two devices yet, and their behavior and
> API can change in any release. Code that uses `OpenStreamAsync` or [`NearbyStreamPayload`](xref:Plugin.Maui.NearbyConnectivity.NearbyStreamPayload) gets
> build error `NEARBY001`. To use streams, add the ID to `NoWarn` in your project file:
>
> ```xml
> <PropertyGroup>
>   <NoWarn>$(NoWarn);NEARBY001</NoWarn>
> </PropertyGroup>
> ```
>
> An app that does not use streams can still receive one from a peer. It arrives as a
> [`NearbyPayload`](xref:Plugin.Maui.NearbyConnectivity.NearbyPayload). Dispose it.

Use a stream when the data has no known length. Also use one when the peer must start to read
before you finish the write.

```csharp
await using var stream = await connection.OpenStreamAsync("vitals", cancellationToken);
await stream.WriteAsync(chunk, cancellationToken);
```

The peer receives one `NearbyStreamPayload` with the same name. When you dispose your end, the
peer's read ends.

## Receive data

Read [`nearby.PayloadsAsync(ct)`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.PayloadsAsync*) in one loop for the whole app. Each item has the payload and the
[`RemoteDevice`](xref:Plugin.Maui.NearbyConnectivity.NearbyInboundPayload.RemoteDevice) that sent it. The loop awaits your work in the loop body before it takes the next
payload from the same connection.

```csharp
await foreach (var (sender, payload) in nearby.PayloadsAsync(cancellationToken))
{
    switch (payload)
    {
        case NearbyBytesPayload bytes:
            string message = Encoding.UTF8.GetString(bytes.Data.Span);
            Console.WriteLine($"From {sender.Presence.DisplayName}: {message}");
            break;

        case NearbyFilePayload filePayload:
            // MoveTo moves the staged file to the path that you give it. No copy stays in staging.
            var kept = filePayload.MoveTo(
                Path.Combine(FileSystem.AppDataDirectory, filePayload.File.FileName));
            await GenerateThumbnailAsync(kept.FullPath);
            break;

        case NearbyStreamPayload stream:
            // Experimental: needs NEARBY001 in NoWarn. See "Open a stream".
            // The stream ends when the peer closes its end.
            using (stream)
            {
                await ConsumeAsync(stream.Name, stream.Stream);
            }
            break;
    }
}
```

**Use one loop.** `PayloadsAsync` gives each payload to one enumeration, so two loops divide the
payloads between them.
[`samples/NearbyChat/Services/NearbyIngestionService.cs`](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/blob/main/samples/NearbyChat/Services/NearbyIngestionService.cs)
runs one loop for the whole app, and sends each payload to the pages as a message. It also reads
`InboundTransfersAsync` for each connection.

**The buffer has no bound.** Payloads wait in memory until your code reads them, and the plugin
does not slow the sender. Read `PayloadsAsync` for the life of the app, or close the connection. At
64 unread payloads on one connection, the plugin logs one warning.

**You own every payload.** Dispose each payload after you read it. A disposed stream payload closes
its stream, and a disposed file payload deletes its staged file.

**Move a file to keep it.** The plugin stages a received file in app-private storage, which the
operating system may purge. [`MoveTo`](xref:Plugin.Maui.NearbyConnectivity.NearbyFilePayload.MoveTo*) moves the file and keeps the payload usable. If you neither
move the file nor dispose the payload, the plugin deletes the file when the session is disposed, or
the next time your app creates the session.

**Treat the content as untrusted.** [`FileName`](xref:Microsoft.Maui.Storage.FileBase.FileName) and [`ContentType`](https://learn.microsoft.com/dotnet/api/microsoft.maui.storage.filebase.contenttype) come from the sender, and an
inbound file has no size limit. If a staged file already has the same name, the plugin adds a
suffix such as `(1)`, so `FileName` can differ from the name that the sender used. See
[Treat every payload as hostile input](security.md#treat-every-payload-as-hostile-input).

## Next

- [Limits](configuration.md#limits) for every size limit.
- [Troubleshooting](troubleshooting.md) if messages go missing.
