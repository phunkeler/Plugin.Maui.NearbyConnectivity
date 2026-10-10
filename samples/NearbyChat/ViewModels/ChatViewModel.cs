using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using NearbyChat.Data;
using NearbyChat.Messages;
using NearbyChat.Models;
using NearbyChat.Services;
using Plugin.Maui.BottomSheet.Navigation;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.ViewModels;

public partial class ChatViewModel(
    IMessenger messenger,
    IDispatcher dispatcher,
    ILauncher launcher,
    IMediaPicker mediaPicker,
    INavigationService navigationService,
    ChatMessageStore store,
    ConnectionConfirmations confirmations,
    IBottomSheetNavigationService bottomSheetNavigationService,
    TimeProvider timeProvider,
    ILogger<ChatViewModel> logger) : ObservableRecipient(messenger),
        INavigationAware,
        IRecipient<ChatMessageReceived>,
        IRecipient<InboundTransferProgress>,
        IRecipient<ConfirmationChanged>
{
    readonly ILogger<ChatViewModel> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    [MemberNotNullWhen(true, nameof(Device))]
    public bool CanSend
        => Device is not null
            && IsConfirmed
            && (!string.IsNullOrWhiteSpace(Message) || MediaAttachment is not null);

    /// <summary>
    /// Whether somebody has compared this connection's authentication code and said it matched.
    /// </summary>
    /// <remarks>
    /// This gate is the sample's, not the library's. The plugin derives the code and stops there:
    /// it will happily send over a connection nobody has confirmed. An app that wants the check to
    /// mean something has to refuse to send without it, which is what this does. While false the
    /// sheet shows the code in place of the message list's footer, with the two answers.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    public partial bool IsConfirmed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AuthenticationCode))]
    public partial NearbyDevice? Device { get; set; }

    /// <summary>
    /// The six digits the two people read to each other, split for reading aloud, or an empty
    /// string once the connection is gone.
    /// </summary>
    /// <remarks>
    /// Read through the device on every access: a captured code would survive a drop and
    /// reconnect and name the wrong handshake.
    /// </remarks>
    public string AuthenticationCode
        => Device?.Connection?.AuthenticationCode is { Length: 6 } code
            ? code.Insert(3, " ")
            : string.Empty;

    /// <summary>
    /// Records that the codes matched, which enables sending.
    /// </summary>
    [RelayCommand]
    void Confirm()
    {
        if (Device is null)
        {
            return;
        }

        confirmations.Confirm(Device.Id);
        IsConfirmed = true;
    }

    /// <summary>
    /// Ends a connection whose codes did not match and closes the sheet.
    /// </summary>
    /// <remarks>
    /// A mismatch means something terminated the handshake in the middle, so the only useful
    /// answer is to stop talking to it. The row behind the sheet leaves the Connected filter on
    /// its own, as it does for any close the peer initiates.
    /// </remarks>
    [RelayCommand]
    async Task Reject()
    {
        if (Device is null)
        {
            return;
        }

        try
        {
            await Device.DisconnectAsync();
        }
        catch (NearbyException ex)
        {
            // The connection is gone either way; the sheet still closes.
            LogRejectFailed(Device.Id, ex);
        }

        await bottomSheetNavigationService.GoBackAsync();
    }

    /// <summary>
    /// Closes the sheet. The connection stays open.
    /// </summary>
    [RelayCommand]
    async Task Close() => await bottomSheetNavigationService.GoBackAsync();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    public partial string? Message { get; set; }

    /// <summary>
    /// The photo or video picked for the next send. It goes out as its own bubble, ahead of any
    /// typed message.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(HasMediaAttachment))]
    public partial MediaAttachment? MediaAttachment { get; set; }

    public bool HasMediaAttachment => MediaAttachment is not null;

    /// <summary>
    /// Drops the picked attachment without sending it.
    /// </summary>
    [RelayCommand]
    void RemoveAttachment() => MediaAttachment = null;

    [ObservableProperty]
    public partial bool IsReceiving { get; set; }

    [ObservableProperty]
    public partial double ReceiveProgress { get; set; }

    /// <summary>Whether the inbound transfer's total size is known, so a bar can show it.</summary>
    [ObservableProperty]
    public partial bool IsReceiveSizeKnown { get; set; }

    /// <summary>How much of the inbound file has arrived, such as "Receiving 12 MB of 256 MB".</summary>
    [ObservableProperty]
    public partial string? ReceiveCaption { get; set; }

    long? _receivingPayloadId;

    /// <summary>
    /// Refuses the file arriving now. The connection stays open, and the strip clears when the
    /// plugin reports the transfer ended.
    /// </summary>
    [RelayCommand]
    Task CancelReceiveAsync()
        => _receivingPayloadId is { } payloadId && Device?.Connection is { } connection
            ? connection.CancelInboundTransferAsync(payloadId)
            : Task.CompletedTask;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    /// <summary>
    /// Raised when a bubble already in the list shows why it was not delivered. The bubble has
    /// grown by its reason and Retry button, and the list does not scroll to follow it, so the view
    /// scrolls it back into sight.
    /// </summary>
    public event EventHandler<ChatMessageViewModel>? DeliveryFailed;

    void Append(ChatMessageViewModel bubble)
    {
        if (Messages.Count > 0 && Messages[^1] is { } previous && previous.IsContinuedBy(bubble))
        {
            previous.IsFollowedInSameMinute = true;
        }

        Messages.Add(bubble);
    }

    /// <summary>
    /// Replaces a bubble with itself, so the list measures it again.
    /// </summary>
    /// <remarks>
    /// The Android list does not re-measure a cell whose content grows after it was laid out: a
    /// failure's reason and Retry button overflow the old cell and its bottom is cut off. A replace
    /// makes the list rebind the cell at its new size, and the same holds when a retry removes them.
    /// </remarks>
    void Remeasure(ChatMessageViewModel bubble)
    {
        var index = Messages.IndexOf(bubble);

        if (index >= 0)
        {
            Messages[index] = bubble;
        }
    }

    void Reload(string deviceId)
    {
        Messages.Clear();

        foreach (var message in store.GetAll(deviceId))
        {
            Append(ToBubble(message));
        }
    }

    [RelayCommand(
        CanExecute = nameof(CanSend),
        IncludeCancelCommand = true)]
    async Task Send(CancellationToken cancellationToken)
    {
        if (!CanSend)
        {
            return;
        }

        var deviceId = Device.Id;
        var attachment = MediaAttachment;
        var text = Message;

        Message = null;
        MediaAttachment = null;

        if (attachment is not null)
        {
            var media = new ChatMessage(attachment.SourceFile?.FileName, NearbyDirection.Outgoing, timeProvider.GetUtcNow())
            {
                Bytes = attachment.FilePath is { } path && File.Exists(path)
                    ? new FileInfo(path).Length
                    : 0,
            };

            media.Attachments.Add(attachment);

            await AddAndDeliverAsync(deviceId, media, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            var message = new ChatMessage(text, NearbyDirection.Outgoing, timeProvider.GetUtcNow())
            {
                Bytes = Encoding.UTF8.GetByteCount(text),
            };

            await AddAndDeliverAsync(deviceId, message, cancellationToken);
        }
    }

    // The bubble is added before delivery is awaited, so outbound transfer progress has a message
    // to render against while the file is in flight. Send awaits the file before the text, so the
    // peer receives them in the order the sender sees them.
    async Task AddAndDeliverAsync(string deviceId, ChatMessage chatMessage, CancellationToken cancellationToken)
    {
        var vm = ToBubble(chatMessage);
        Append(vm);

        store.Add(deviceId, chatMessage);

        await DeliverAsync(vm, cancellationToken);
    }

    /// <summary>
    /// Sends the message a bubble already shows, and records on that bubble why it did not arrive.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Send"/> so <see cref="Retry"/> can re-run delivery for a bubble that
    /// is already in the list and already in the store.
    /// </remarks>
    async Task DeliverAsync(ChatMessageViewModel vm, CancellationToken cancellationToken)
    {
        if (Device is not { } device)
        {
            return;
        }

        var hadFailed = vm.HasFailed;
        vm.ClearFailure();

        IProgress<NearbyTransferProgress>? progress = null;

        if (vm.MediaAttachment is not null)
        {
            vm.IsTransferring = true;
            progress = new OutboundProgressRelay(dispatcher, Messenger, device, vm);
        }

        try
        {
            if (device.Connection is not { } connection)
            {
                vm.Fail(
                    "Not delivered. The connection to this device has ended.",
                    "Reconnect from the Connections screen, then tap Retry.");
                return;
            }

            if (vm.Model.Attachments.FirstOrDefault() is MediaAttachment { SourceFile: { } sourceFile })
            {
                // The library stages this itself when the picker's path is not readable.
                await connection.SendFileAsync(sourceFile, progress, cancellationToken);
            }
            else if (vm.Model.Attachments.FirstOrDefault() is MediaAttachment { FilePath: { Length: > 0 } filePath })
            {
                await connection.SendFileAsync(filePath, progress, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(vm.Model.Text))
            {
                await connection.SendAsync(Encoding.UTF8.GetBytes(vm.Model.Text), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            vm.Fail(
                "Not delivered. You cancelled this send.",
                "Tap Retry to send it again.");
        }
        catch (NearbyTransferTimeoutException ex)
        {
            LogSendFailed(device.Id, ex);

            vm.Fail(
                "Not delivered. The transfer stalled and no data moved for too long.",
                "Move the devices closer together, then tap Retry.");
        }
        catch (NearbyTransferCanceledException) when (device.Connection is { IsOpen: true })
        {
            vm.Fail(
                "Not delivered. The other device cancelled this file.",
                "Ask them first, then tap Retry.");
        }
        catch (NearbyTransferCanceledException ex)
        {
            LogSendFailed(device.Id, ex);

            vm.Fail(
                "Not delivered. The connection dropped while sending.",
                "Reconnect from the Connections screen, then tap Retry.");
        }
        catch (NearbyTransferException ex)
        {
            LogSendFailed(device.Id, ex);

            vm.Fail(
                "Not delivered. The file could not be sent.",
                "Check the file still exists, then tap Retry.");
        }
        catch (NearbyException ex)
        {
            // The base type covers the disconnect-mid-send case, which every SendAsync overload
            // documents but no more specific exception represents.
            LogSendFailed(device.Id, ex);

            vm.Fail(
                "Not delivered. The connection dropped while sending.",
                "Reconnect from the Connections screen, then tap Retry.");
        }
        finally
        {
            vm.IsTransferring = false;

            if (progress is not null)
            {
                Messenger.Send(new OutboundTransferProgress(device, null));
            }

            store.SetUndelivered(device.Id, vm.Model, vm.HasFailed);

            if (hadFailed || vm.HasFailed)
            {
                Remeasure(vm);
            }

            if (vm.HasFailed)
            {
                DeliveryFailed?.Invoke(this, vm);
            }
        }
    }

    /// <summary>
    /// Sends a failed message again.
    /// </summary>
    [RelayCommand]
    Task Retry(ChatMessageViewModel vm)
        => DeliverAsync(vm, CancellationToken.None);

    /// <summary>
    /// Opens a bubble's attachment in whatever app the device has for it.
    /// </summary>
    [RelayCommand]
    async Task OpenFile(ChatMessageViewModel vm)
    {
        if (vm.MediaAttachment?.FilePath is not { Length: > 0 } filePath)
        {
            return;
        }

        vm.OpenFailureReason = null;

        var opened = await launcher.OpenAsync(new OpenFileRequest
        {
            Title = Path.GetFileName(filePath),
            File = new ReadOnlyFile(filePath)
        });

        if (!opened)
        {
            vm.OpenFailureReason = "No app on this device can open this file.";
        }
    }

    static ChatMessageViewModel ToBubble(ChatMessage model) => model.Attachments switch
    {
        var a when a.Any(x => x.Type == AttachmentType.Photo) => new PhotoMessageViewModel(model),
        var a when a.Any(x => x.Type == AttachmentType.Video) => new VideoMessageViewModel(model),
        _ => new ChatMessageViewModel(model)
    };

    [RelayCommand]
    async Task Attach()
    {
        const string photoOption = "Photo";
        const string videoOption = "Video";

        var choice = await navigationService.DisplayActionSheetAsync(
            title: "Attach",
            cancel: "Cancel",
            destruction: null,
            photoOption,
            videoOption);

        if (choice is not (photoOption or videoOption))
        {
            return;
        }

        var attachment = await PickAsync(video: choice is videoOption);

        if (attachment is not null)
        {
            MediaAttachment = attachment;
        }

        async Task<MediaAttachment?> PickAsync(bool video)
        {
            var files = video
                ? await mediaPicker.PickVideosAsync()
                : await mediaPicker.PickPhotosAsync();

            if (files?.FirstOrDefault() is not FileResult fileResult)
            {
                return null;
            }

            // Sending needs no local copy: SendFileAsync(FileResult) stages the file when the
            // picker's own path is not readable. This copy exists only so the sample can render a
            // thumbnail from a real path.
            var fullPath = File.Exists(fileResult.FullPath)
                ? fileResult.FullPath
                : await CopyForThumbnailAsync(fileResult);

            MediaAttachment picked = video
                ? new VideoAttachment
                {
                    FilePath = fullPath,
                    SourceFile = fileResult,
                    Thumbnail = await ThumbnailService.GetVideoThumbnailAsync(fullPath),
                    Duration = ThumbnailService.GetVideoDuration(fullPath)
                }
                : new PhotoAttachment
                {
                    FilePath = fullPath,
                    SourceFile = fileResult,
                    Thumbnail = await ThumbnailService.GetPhotoThumbnailAsync(fullPath)
                        ?? ImageSource.FromFile(fullPath)
                };

            return picked;
        }
    }

    public void OnNavigatedFrom(IBottomSheetNavigationParameters parameters)
    {
        IsActive = false;

        // An open failure describes what happened on the last tap, not a property of the message.
        // Clearing on close means a user who installs a viewer and reopens the sheet does not meet
        // a stale line about a file that now opens. The send failure is deliberately left alone:
        // that one is a property of the message and survives until it is sent again.
        foreach (var message in Messages)
        {
            message.OpenFailureReason = null;
        }
    }

    public void OnNavigatedTo(IBottomSheetNavigationParameters parameters)
    {
        IsActive = true;
        IsReceiving = false;
        ReceiveProgress = 0;

        if (parameters.TryGetValue(nameof(NearbyDevice), out var device)
            && device is NearbyDevice nearbyDevice)
        {
            Device = nearbyDevice;

            // The opening value; ConfirmationChanged keeps it current while the sheet is open,
            // including a confirmation made on the connections page behind it.
            IsConfirmed = confirmations.IsConfirmed(nearbyDevice.Id);

            Reload(nearbyDevice.Id);
        }
    }

    static async Task<string> CopyForThumbnailAsync(FileResult fileResult)
    {
        // A picker result's FullPath is not always readable — on iOS it may be a bare file name.
        // Thumbnail rendering needs a real path, so copy through the stream accessor.
        var localPath = Path.Combine(FileSystem.CacheDirectory, fileResult.FileName);
        await using var source = await fileResult.OpenReadAsync();
        await using var dest = File.Create(localPath);
        await source.CopyToAsync(dest);

        return localPath;
    }

    public void Receive(ChatMessageReceived received)
    {
        // A message from a device this person has not confirmed stays in the store until they
        // do; the reload on confirmation brings it in.
        if (received.Device.Id != Device?.Id || !IsConfirmed)
        {
            return;
        }

        // Published from the ingestion loop's thread — marshal before touching the bound
        // collection.
        dispatcher.Dispatch(() =>
        {
            IsReceiving = false;
            ReceiveProgress = 0;
            Append(ToBubble(received.Message));
        });
    }

    public void Receive(ConfirmationChanged confirmation)
    {
        if (confirmation.DeviceId != Device?.Id)
        {
            return;
        }

        // Forget is sent from the ingestion loop's thread, so this cannot assume the dispatcher.
        dispatcher.Dispatch(() =>
        {
            IsConfirmed = confirmation.IsConfirmed;

            // The store answers differently once the device is confirmed: what it held back is
            // now history, and a reload is the one way to show it in order.
            if (confirmation.IsConfirmed && Device is { } device)
            {
                Reload(device.Id);
            }
        });
    }

    public void Receive(InboundTransferProgress update)
    {
        if (update.Device.Id != Device?.Id)
        {
            return;
        }

        dispatcher.Dispatch(() =>
        {
            // Completed arrives after the message itself, which already cleared the indicator;
            // Failed and Canceled are the only way it clears when no message is coming.
            var progress = update.Progress;

            IsReceiving = progress.Status is NearbyTransferStatus.InProgress;
            _receivingPayloadId = IsReceiving ? progress.PayloadId : null;
            IsReceiveSizeKnown = progress.Fraction is not null;
            ReceiveProgress = progress.Fraction ?? 0;
            ReceiveCaption = progress.TotalBytes is { } total
                ? $"Receiving {DataSize.Caption(progress.BytesTransferred)} of {DataSize.Caption(total)}"
                : $"Receiving {DataSize.Caption(progress.BytesTransferred)}";
        });
    }

    [LoggerMessage(
        EventId = 1,
        EventName = nameof(LogSendFailed),
        Level = LogLevel.Error,
        Message = "Sending a message to {DeviceId} failed.")]
    partial void LogSendFailed(string deviceId, Exception exception);

    [LoggerMessage(
        EventId = 2,
        EventName = nameof(LogRejectFailed),
        Level = LogLevel.Error,
        Message = "Disconnecting from {DeviceId} after a code mismatch failed.")]
    partial void LogRejectFailed(string deviceId, Exception exception);

    sealed class OutboundProgressRelay(
        IDispatcher dispatcher,
        IMessenger messenger,
        NearbyDevice device,
        ChatMessageViewModel message) : IProgress<NearbyTransferProgress>
    {
        public void Report(NearbyTransferProgress value)
        {
            messenger.Send(new OutboundTransferProgress(device, value));

            dispatcher.Dispatch(() =>
            {
                if (value.Fraction is { } fraction)
                {
                    message.TransferProgress = fraction;
                }

                if (value.Status is not NearbyTransferStatus.InProgress)
                {
                    message.IsTransferring = false;
                }
            });
        }
    }
}
