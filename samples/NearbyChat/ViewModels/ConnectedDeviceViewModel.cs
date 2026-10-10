using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using NearbyChat.Data;
using NearbyChat.Messages;
using NearbyChat.Services;
using Plugin.Maui.BottomSheet.Navigation;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.ViewModels;

/// <summary>
/// A connected device row.
/// </summary>
/// <remarks>
/// Takes the session rather than a <see cref="NearbyConnection"/>, so it can be projected from a
/// <see cref="DeviceRows{TRow}"/> like every other row type. The connection is looked
/// up per command instead of captured: a device is a value and cannot hold a live handle, and a
/// captured handle would go stale the moment the peer dropped and reconnected.
/// </remarks>
public partial class ConnectedDeviceViewModel : NearbyDeviceViewModel,
    IRecipient<ConfirmationChanged>,
    IRecipient<ChatHistoryChanged>,
    IRecipient<InboundTransferProgress>,
    IRecipient<OutboundTransferProgress>
{
    readonly IBottomSheetNavigationService _bottomSheetNavigationService;
    readonly ChatMessageStore _store;
    readonly DateTimeOffset _connectedSince;
    readonly ILogger _logger;

    public ConnectedDeviceViewModel(
        NearbyDevice device,
        IBottomSheetNavigationService bottomSheetNavigationService,
        ConnectionConfirmations confirmations,
        ChatMessageStore store,
        ConnectionTracker tracker,
        IMessenger messenger,
        TimeProvider timeProvider,
        ILogger logger)
        : base(device, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(bottomSheetNavigationService);
        ArgumentNullException.ThrowIfNull(confirmations);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(logger);

        _bottomSheetNavigationService = bottomSheetNavigationService;
        _store = store;
        _connectedSince = tracker.ConnectedSince(device.Id);
        _logger = logger;

        // A row is rebuilt whenever the page is revisited, so the answer has to come from the
        // store rather than start false and ask the same person again.
        IsConfirmed = confirmations.IsConfirmed(device.Id);

        // The store is the one writer, and a Forget can come from the ingestion loop when the peer
        // drops the connection. Registration is weak, so a dropped row needs no unsubscribe.
        messenger.Register<ConfirmationChanged>(this);
        messenger.Register<ChatHistoryChanged>(this);
        messenger.Register<InboundTransferProgress>(this);
        messenger.Register<OutboundTransferProgress>(this);

        RefreshConversation();
    }

    /// <summary>
    /// Follows the store so a confirmation forgotten elsewhere clears this row too.
    /// </summary>
    public void Receive(ConfirmationChanged confirmation)
    {
        if (confirmation.DeviceId == Id)
        {
            IsConfirmed = confirmation.IsConfirmed;
        }
    }

    /// <summary>
    /// Follows the store so a message sent or received elsewhere updates this row's preview.
    /// </summary>
    public void Receive(ChatHistoryChanged changed)
    {
        if (changed.DeviceId == Id)
        {
            RefreshConversation();
        }
    }

    /// <summary>
    /// Shows a file arriving from this device.
    /// </summary>
    public void Receive(InboundTransferProgress progress)
    {
        if (progress.Device.Id == Id)
        {
            ApplyTransfer("Receiving...", progress.Progress);
        }
    }

    /// <summary>
    /// Shows a file going to this device.
    /// </summary>
    public void Receive(OutboundTransferProgress progress)
    {
        if (progress.Device.Id != Id)
        {
            return;
        }

        if (progress.Progress is { } current)
        {
            ApplyTransfer("Sending...", current);
        }
        else
        {
            ClearTransfer();
        }
    }

    /// <inheritdoc/>
    public override void RefreshRelativeTime()
    {
        base.RefreshRelativeTime();
        OnPropertyChanged(nameof(ConnectedFor));
    }

    /// <summary>
    /// How long this connection has been up, as a caption.
    /// </summary>
    public string ConnectedFor => ConnectionDuration.Caption(TimeProvider.GetUtcNow() - _connectedSince);

    /// <summary>
    /// How many messages have crossed this connection.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessages), nameof(ShowStats))]
    public partial int MessageCount { get; private set; }

    /// <summary>
    /// How many files have crossed this connection.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFiles), nameof(ShowStats))]
    public partial int FileCount { get; private set; }

    /// <summary>
    /// How many incoming messages are waiting for this device to be verified.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeld), nameof(ShowStats), nameof(ShowChips))]
    public partial int HeldCount { get; private set; }

    /// <summary>
    /// Whether any message has crossed this connection.
    /// </summary>
    public bool HasMessages => MessageCount > 0;

    /// <summary>
    /// Whether any file has crossed this connection.
    /// </summary>
    public bool HasFiles => FileCount > 0;

    /// <summary>
    /// Whether any incoming message is waiting for verification.
    /// </summary>
    public bool HasHeld => HeldCount > 0;

    /// <summary>
    /// Whether the row has anything to say about this connection's traffic: a transfer in flight,
    /// or something already exchanged.
    /// </summary>
    public bool ShowStats => IsTransferring || HasMessages || HasFiles || HasHeld;

    /// <summary>
    /// Whether the message and file chips have the row to themselves: a transfer or held messages
    /// take their place.
    /// </summary>
    public bool ShowChips => !IsTransferring && !HasHeld;

    /// <summary>
    /// Whether any payload bytes have moved in either direction.
    /// </summary>
    [ObservableProperty]
    public partial bool HasData { get; private set; }

    /// <summary>
    /// Payload bytes moved in both directions, as a caption.
    /// </summary>
    [ObservableProperty]
    public partial string? TotalCaption { get; private set; }

    /// <summary>
    /// What this device has sent, as captions.
    /// </summary>
    [ObservableProperty]
    public partial TrafficCaption? Sent { get; private set; }

    /// <summary>
    /// What this device has received, as captions. Held messages count toward its bytes only.
    /// </summary>
    [ObservableProperty]
    public partial TrafficCaption? Received { get; private set; }

    /// <summary>
    /// Whether a file is moving to or from this device right now.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStats), nameof(ShowChips))]
    public partial bool IsTransferring { get; private set; }

    /// <summary>
    /// The direction of the file in flight, phrased for the message line.
    /// </summary>
    [ObservableProperty]
    public partial string? TransferCaption { get; private set; }

    /// <summary>
    /// How much of the file in flight has moved, from zero to one.
    /// </summary>
    [ObservableProperty]
    public partial double TransferFraction { get; private set; }

    /// <summary>
    /// Whether the size of the file in flight is known, so a bar can show real progress.
    /// </summary>
    [ObservableProperty]
    public partial bool HasTransferFraction { get; private set; }

    void RefreshConversation()
    {
        var summary = _store.Summarize(Id);

        MessageCount = summary.Messages;
        FileCount = summary.Files;
        HeldCount = summary.Held;
        HasData = summary.Bytes > 0;
        Sent = TrafficCaption.From(summary.Sent);
        Received = TrafficCaption.From(summary.Received);
        TotalCaption = DataSize.Caption(summary.Bytes);
    }

    void ApplyTransfer(string caption, NearbyTransferProgress progress)
    {
        IsTransferring = progress.Status is NearbyTransferStatus.InProgress;
        TransferCaption = caption;
        HasTransferFraction = IsTransferring && progress.Fraction is not null;
        TransferFraction = progress.Fraction ?? 0d;
    }

    void ClearTransfer()
    {
        IsTransferring = false;
        HasTransferFraction = false;
        TransferFraction = 0d;
    }

    /// <summary>
    /// Whether the person on this device has said the two codes matched.
    /// </summary>
    /// <remarks>
    /// The sample's own state, not the plugin's: nothing in the library tracks or enforces a
    /// confirmation. The comparison itself happens in <see cref="ChatViewModel"/>, which also
    /// gates sending on it; here it only turns the row's pill from Unverified to Verified.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsConfirmed { get; private set; }

    /// <summary>
    /// Whether this device's data sheet is open.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDataShown { get; private set; }

    internal void SetDataShown(bool shown) => IsDataShown = shown;

    [RelayCommand]
    async Task Chat()
    {
        // The result is the only failure signal: NavigateToAsync reports a failed open by
        // returning Success = false rather than throwing, so discarding it means a tap that
        // opens nothing and says nothing.
        var result = await _bottomSheetNavigationService.NavigateToAsync(
            nameof(ChatViewModel),
            new BottomSheetNavigationParameters
            {
                { nameof(NearbyDevice), Device }
            });

        if (result.Success || result.Cancelled)
        {
            return;
        }

        LogChatOpenFailed(_logger, Device.Id, result.Exception);
    }

    [RelayCommand]
    async Task ShowData()
    {
        var result = await _bottomSheetNavigationService.NavigateToAsync(
            nameof(PeerDataViewModel),
            new BottomSheetNavigationParameters
            {
                { nameof(ConnectedDeviceViewModel), this }
            });

        if (result.Success || result.Cancelled)
        {
            return;
        }

        LogDataOpenFailed(_logger, Device.Id, result.Exception);
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    async Task Disconnect()
    {
        // Stated before the await, not after: the call is what makes the row terminal, and the
        // user needs the button to stop offering something it is already doing.
        MarkDisconnecting();
        DisconnectCommand.NotifyCanExecuteChanged();

        try
        {
            // Resolves against the session as the tap lands, and does nothing if the connection is
            // already gone.
            await Device.DisconnectAsync();
        }
        catch (NearbyException ex)
        {
            // Reported, not surfaced: the connection is gone either way — the session drops the
            // device on both paths — so the row states the same outcome and leaves. Raising an
            // alert here would ask the user to act on a connection that no longer exists.
            LogDisconnectFailed(_logger, Device.Id, ex);
        }
        finally
        {
            // The row's own terminal state, rather than waiting for the device to leave the
            // Connected filter: the page keeps a disconnecting row on screen precisely so it can
            // say what happened, and nothing else would start its exit.
            MarkDisconnected();
        }
    }

    [LoggerMessage(
        EventId = 2,
        EventName = nameof(LogDisconnectFailed),
        Level = LogLevel.Error,
        Message = "Disconnecting from {DeviceId} failed.")]
    static partial void LogDisconnectFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(
        EventId = 1,
        EventName = nameof(LogChatOpenFailed),
        Level = LogLevel.Error,
        Message = "Opening the chat sheet for {DeviceId} failed.")]
    static partial void LogChatOpenFailed(ILogger logger, string deviceId, Exception? exception);

    [LoggerMessage(
        EventId = 3,
        EventName = nameof(LogDataOpenFailed),
        Level = LogLevel.Error,
        Message = "Opening the data sheet for {DeviceId} failed.")]
    static partial void LogDataOpenFailed(ILogger logger, string deviceId, Exception? exception);
}
