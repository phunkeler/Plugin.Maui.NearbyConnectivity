using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using NearbyChat.Data;
using NearbyChat.Messages;
using NearbyChat.Models;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.Services;

/// <summary>
/// The app's single inbound-payload consumer: reads <see cref="INearbyConnectivity.PayloadsAsync(CancellationToken)"/>,
/// persists each payload as a <see cref="ChatMessage"/>, and fans it out as a domain message.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No startup ritual.</strong> <see cref="INearbyConnectivity.PayloadsAsync(CancellationToken)"/>
/// reads every connection already open before following new ones, an unconsumed connection
/// buffers its payloads until a consumer arrives, and the connections already open are read from
/// <see cref="INearbyConnectivity.Devices"/> after subscribing to
/// <see cref="INearbyConnectivity.DeviceChanged"/> — so this service can start whenever it is
/// constructed and misses nothing.
/// The app resolves it once at startup (see <c>App</c>), which keeps ingestion running for the
/// life of the app.
/// </para>
/// <para>
/// <strong>Persistence.</strong> This singleton holds the singleton
/// <see cref="ChatMessageStore"/> directly, which is safe only because that store is a thread-safe
/// in-memory dictionary. Backing it with an EF Core <c>DbContext</c> would make it a captive
/// dependency, and this class would then have to resolve one scope per payload instead.
/// </para>
/// </remarks>
public sealed partial class NearbyIngestionService : IDisposable
{
    readonly INearbyConnectivity _nearby;
    readonly ChatMessageStore _store;
    readonly ConnectionConfirmations _confirmations;
    readonly IMessenger _messenger;
    readonly TimeProvider _timeProvider;
    readonly ILogger<NearbyIngestionService> _logger;
    readonly CancellationTokenSource _stopping = new();
    readonly HashSet<NearbyConnection> _watched = [];

    public NearbyIngestionService(
        INearbyConnectivity nearby,
        ChatMessageStore store,
        ConnectionConfirmations confirmations,
        IMessenger messenger,
        TimeProvider timeProvider,
        ILogger<NearbyIngestionService> logger)
    {
        _nearby = nearby ?? throw new ArgumentNullException(nameof(nearby));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _confirmations = confirmations ?? throw new ArgumentNullException(nameof(confirmations));
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _nearby.DeviceChanged += OnDeviceChanged;

        foreach (var device in _nearby.Devices)
        {
            Watch(device);
        }

        _ = ConsumePayloadsAsync(_stopping.Token);
    }

    async Task ConsumePayloadsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var (device, payload) in _nearby.PayloadsAsync(cancellationToken).ConfigureAwait(false))
            {
                await ProcessPayloadAsync(device, payload).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down.
        }
        catch (Exception ex)
        {
            LogIngestionFailed(ex);
        }
    }

    void OnDeviceChanged(object? sender, NearbyDeviceChange change) => Watch(change.Device);

    void Watch(NearbyDevice device)
    {
        if (device.Connection is { } connection && Track(connection))
        {
            _ = RelayInboundProgressAsync(connection, _stopping.Token);
            _ = ClearOnCloseAsync(connection);
        }
    }

    bool Track(NearbyConnection connection)
    {
        lock (_watched)
        {
            return _watched.Add(connection);
        }
    }

    async Task ClearOnCloseAsync(NearbyConnection connection)
    {
        // Closed never faults, so this needs no catch of its own.
        await connection.Closed.ConfigureAwait(false);

        lock (_watched)
        {
            _watched.Remove(connection);
        }

        _store.Clear(connection.RemoteDevice.Id);
        _confirmations.Forget(connection.RemoteDevice.Id);
    }

    async Task RelayInboundProgressAsync(NearbyConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var progress in connection.InboundTransfersAsync(cancellationToken).ConfigureAwait(false))
            {
                _messenger.Send(new InboundTransferProgress(connection.RemoteDevice, progress));

                if (DescribeLostTransfer(progress) is { } notice)
                {
                    var message = new ChatMessage(notice, NearbyDirection.Incoming, _timeProvider.GetUtcNow())
                    {
                        IsNotice = true,
                    };

                    _store.Add(connection.RemoteDevice.Id, message);
                    _messenger.Send(new ChatMessageReceived(connection.RemoteDevice, message));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down, or the connection ended.
        }
        catch (Exception ex)
        {
            LogIngestionFailed(ex);
        }
    }

    /// <summary>
    /// The line the conversation keeps for an inbound file that ended without arriving, or
    /// <see langword="null"/> for one still moving or delivered.
    /// </summary>
    /// <remarks>
    /// The receive strip disappears when the transfer ends, so without this line a cancelled file
    /// leaves no trace on the device that was receiving it.
    /// </remarks>
    static string? DescribeLostTransfer(NearbyTransferProgress progress)
    {
        var size = progress.TotalBytes is { } total ? $" ({ViewModels.DataSize.Caption(total)})" : string.Empty;

        return progress.Status switch
        {
            NearbyTransferStatus.Canceled => $"Incoming file cancelled{size}",
            NearbyTransferStatus.Failed => $"Incoming file did not arrive{size}",
            _ => null,
        };
    }

    async Task ProcessPayloadAsync(NearbyDevice device, NearbyPayload payload)
    {
        ChatMessage? message;

        try
        {
            message = await MaterializeAsync(payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogMaterializeFailed(device.Id, ex);
            return;
        }

        if (message is null)
        {
            return;
        }

        try
        {
            _store.Add(device.Id, message);
        }
        catch (Exception ex)
        {
            LogPersistFailed(device.Id, ex);
            return;
        }

        _messenger.Send(new ChatMessageReceived(device, message));
    }

    async Task<ChatMessage?> MaterializeAsync(NearbyPayload payload)
    {
        switch (payload)
        {
            case NearbyBytesPayload bytes:
                return new ChatMessage(
                    Encoding.UTF8.GetString(bytes.Data.Span),
                    NearbyDirection.Incoming,
                    _timeProvider.GetUtcNow())
                {
                    Bytes = bytes.Data.Length,
                };

            case NearbyFilePayload file:
            {
                var path = file.File.FullPath;
                var contentType = file.File.ContentType ?? string.Empty;

                var message = new ChatMessage(file.File.FileName, NearbyDirection.Incoming, _timeProvider.GetUtcNow())
                {
                    Bytes = new FileInfo(path).Length,
                };

                if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    var thumbnail = await ThumbnailService.GetPhotoThumbnailAsync(path).ConfigureAwait(false);

                    message.Attachments.Add(new PhotoAttachment
                    {
                        FilePath = path,
                        Thumbnail = thumbnail ?? ImageSource.FromFile(path)
                    });
                }
                else if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                {
                    var thumbnail = await ThumbnailService.GetVideoThumbnailAsync(path).ConfigureAwait(false);

                    message.Attachments.Add(new VideoAttachment
                    {
                        FilePath = path,
                        Thumbnail = thumbnail,
                        Duration = ThumbnailService.GetVideoDuration(path)
                    });
                }

                return message;
            }

            default:
                return null;
        }
    }

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed to persist an inbound message from {DeviceId}.")]
    partial void LogPersistFailed(string deviceId, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Failed to materialize an inbound payload from {DeviceId}; it was dropped.")]
    partial void LogMaterializeFailed(string deviceId, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Ingestion failed; no further inbound payloads will be consumed.")]
    partial void LogIngestionFailed(Exception exception);

    /// <summary>
    /// Stops ingestion. The app resolves this singleton once and keeps it for its whole lifetime,
    /// so this runs only at shutdown — it exists because the type owns a
    /// <see cref="CancellationTokenSource"/>, not because anything disposes it earlier.
    /// </summary>
    public void Dispose()
    {
        _nearby.DeviceChanged -= OnDeviceChanged;
        _stopping.Cancel();
        _stopping.Dispose();
    }
}
