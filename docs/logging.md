# Turn on logging

The plugin logs through [`Microsoft.Extensions.Logging`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging), to the providers that your app configured.
The plugin installs no provider of its own and sends nothing off the device.

At the default levels, the plugin logs nothing for a session without problems. Routine events, such
as discovery, connections, and payloads, are [`Debug`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.loglevel#microsoft-extensions-logging-loglevel-debug) and [`Trace`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.loglevel#microsoft-extensions-logging-loglevel-trace). The framework's default minimum
level is [`Information`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.loglevel#microsoft-extensions-logging-loglevel-information), which removes them. By default you see [`Warning`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.loglevel#microsoft-extensions-logging-loglevel-warning), [`Error`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.loglevel#microsoft-extensions-logging-loglevel-error), and one
`Information` message, the iOS background teardown.

To troubleshoot, lower the minimum level for the plugin:

```csharp
builder.Logging.AddFilter("Plugin.Maui.NearbyConnectivity", LogLevel.Debug);
```

Use `Debug` when devices do not appear or connections do not open. Use `Trace` for transfer
problems. `Trace` adds one entry for each payload.

File names that the sender supplied appear in messages at `Debug`, and in file paths at `Error`.
The peer chose those names, and they may contain personal data. The plugin never logs device
names, presence info, or authentication codes.

## Levels

Every message follows one rule for its level, so a level filter has the same effect across the
plugin.

| Level | What it logs |
| --- | --- |
| `Trace` | One entry per payload or transfer update, and on iOS per control frame and per remote peer tracked |
| `Debug` | One entry per device event: found, lost, request received, connection result, disconnected, file received. Also a failure that the plugin throws to your code, because the exception is the report |
| `Information` | A state change that your app cannot observe another way. Currently only the iOS background teardown |
| `Warning` | A problem occurred, and the plugin recovered from it or avoided it |
| `Error` | A failure that no caller can observe: a background activity stopped, an event was lost, a received payload was not delivered, or state may be stale |

## Event ids

Every message has a fixed [`EventId`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.eventid). Filter on the id, not the message text. Ids are stable from
1.0.0-preview.1. A message keeps its id, and the plugin does not reuse a retired id.

| Range | Area |
| --- | --- |
| 1000–1099 | Session lifecycle: start, stop, teardown, and the iOS background teardown |
| 1100–1199 | Devices: found, lost, discovery refresh, and iOS peer tracking |
| 1200–1299 | Requests and connections: requests, consent, handshakes, disconnects |
| 1300–1399 | Authentication codes |
| 1400–1499 | Payloads and file transfers |
| 1500–1599 | Availability and configuration, such as `Info.plist` checks |
| 1600–1699 | App events and the bindable types |
| 1700–1799 | Internal faults and control frames |

When both platforms log the same condition, the two messages share one id. For example, a
connection that opens without an authentication code is 1300 on Android and iOS.

## Next

- [Troubleshooting](troubleshooting.md) for what the log lines usually mean.
