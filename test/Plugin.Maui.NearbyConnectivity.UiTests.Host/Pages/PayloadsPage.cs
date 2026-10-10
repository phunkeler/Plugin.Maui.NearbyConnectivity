using System.Globalization;
using System.Text;

using Plugin.Maui.NearbyConnectivity.UiTests.Host.Components;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Host.Pages;

public sealed class PayloadsPage : CapabilityPage
{
    const long LargeFileBytes = 256L * 1024 * 1024;

    readonly INearbyConnectivity _nearby;
    readonly Entry _text;
    readonly StateBadge _received;
    readonly StateBadge _receivedCount;
    readonly StateBadge _maxBytes;
    readonly StateBadge _inbound;
    readonly ProgressRow _progress;
    int _count;
    (NearbyConnection Connection, long PayloadId)? _lastInbound;

    public PayloadsPage(INearbyConnectivity nearby)
        : base("payloads")
    {
        _nearby = nearby;
        Title = "Payloads";

        // A published limit the tests assert against rather than hardcoding a number of their own.
        _maxBytes = AddBadge("max-bytes");
        _maxBytes.Text = NearbyConnection.MaxBytesPayloadLength.ToString(CultureInfo.InvariantCulture);

        _received = AddBadge("last-received");
        _receivedCount = AddBadge("received-count");
        _receivedCount.Text = "0";
        _progress = new ProgressRow("payloads-progress");
        Stack.Add(_progress);
        _inbound = AddBadge("inbound-status");
        _inbound.Text = "idle";
        Stack.Add(Error);

        _text = new Entry { AutomationId = "payloads-text", Placeholder = "payload text" };
        Stack.Add(_text);

        AddButton("send-bytes", "Send bytes", SendBytesAsync);
        AddButton("send-file", "Send file", SendFileAsync);
        AddButton("send-large-file", "Send large file", SendLargeFileAsync);
        AddButton("cancel-inbound", "Cancel inbound", CancelInboundAsync);
    }

    protected override void OnPageActivated(CancellationToken cancellationToken)
    {
        // The application-wide receive loop: every connection's inbound payloads, merged. Read in
        // exactly one place — reading NearbyConnection.Inbound as well would steal from this.
        Pump(_nearby.PayloadsAsync(cancellationToken), Receive, cancellationToken);

        var watched = new HashSet<NearbyConnection>();

        void Watch(NearbyConnection connection) => Dispatcher.Dispatch(() =>
        {
            if (watched.Add(connection))
            {
                Pump(connection.InboundTransfersAsync(cancellationToken), p => ShowInbound(connection, p), cancellationToken);
            }
        });

        void OnDeviceChanged(object? sender, NearbyDeviceChange change)
        {
            if (change.Device.Connection is { } connection)
            {
                Watch(connection);
            }
        }

        _nearby.DeviceChanged += OnDeviceChanged;
        cancellationToken.Register(() => _nearby.DeviceChanged -= OnDeviceChanged);

        foreach (var connection in _nearby.Connections)
        {
            Watch(connection);
        }
    }

    void ShowInbound(NearbyConnection connection, NearbyTransferProgress progress)
    {
        _lastInbound = (connection, progress.PayloadId);
        _inbound.Set($"{progress.Status}:{progress.BytesTransferred}");
    }

    Task CancelInboundAsync()
        => _lastInbound is var (connection, payloadId)
            ? connection.CancelInboundTransferAsync(payloadId)
            : Task.CompletedTask;

    void Receive(NearbyInboundPayload inbound) => Dispatcher.Dispatch(() =>
    {
        _received.Set(inbound.Payload switch
        {
            NearbyBytesPayload bytes => $"bytes:{Encoding.UTF8.GetString(bytes.Data.Span)}",
            NearbyFilePayload file => $"file:{file.File.FileName}",
            _ => inbound.Payload.GetType().Name,
        });

        _receivedCount.Set((++_count).ToString(CultureInfo.InvariantCulture));
    });

    NearbyConnection FirstConnection()
        => _nearby.Connections.Count > 0
            ? _nearby.Connections[0]
            : throw new InvalidOperationException("No connection is open.");

    Task SendBytesAsync()
        => FirstConnection().SendAsync(Encoding.UTF8.GetBytes(_text.Text ?? string.Empty), PageToken);

    async Task SendFileAsync()
    {
        var path = Path.Combine(FileSystem.CacheDirectory, "payload-test.txt");
        await File.WriteAllTextAsync(path, _text.Text ?? "file payload", PageToken).ConfigureAwait(true);

        var progress = new Progress<NearbyTransferProgress>(p => _progress.Set(p.BytesTransferred, p.TotalBytes));
        await FirstConnection().SendFileAsync(path, progress, PageToken).ConfigureAwait(true);
    }

    async Task SendLargeFileAsync()
    {
        var path = Path.Combine(FileSystem.CacheDirectory, "payload-large.bin");

        using (var file = File.Create(path))
        {
            file.SetLength(LargeFileBytes);
        }

        var progress = new Progress<NearbyTransferProgress>(p => _progress.Set(p.BytesTransferred, p.TotalBytes));
        await FirstConnection().SendFileAsync(path, progress, PageToken).ConfigureAwait(true);
    }
}
