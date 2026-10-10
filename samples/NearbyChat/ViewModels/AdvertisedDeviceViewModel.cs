using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.ViewModels;

public partial class AdvertisedDeviceViewModel : NearbyDeviceViewModel
{
    readonly TimeSpan _offerWindow;
    readonly ILogger _logger;

    public AdvertisedDeviceViewModel(
        NearbyDevice device,
        TimeSpan offerWindow,
        TimeProvider timeProvider,
        ILogger logger)
        : base(device, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _offerWindow = offerWindow;
        _logger = logger;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailed))]
    [NotifyPropertyChangedFor(nameof(CanAnswer))]
    public partial string? FailureReason { get; set; }

    public bool HasFailed => FailureReason is not null;

    /// <summary>
    /// Whether the request is still answerable, and the Decline button has something to do.
    /// </summary>
    /// <remarks>
    /// False in both terminal states: once the request has lapsed or the connection is up, the
    /// Accept button spans the row on its own and states the outcome.
    /// </remarks>
    public bool CanAnswer => !HasFailed && State is DeviceRowState.Idle or DeviceRowState.Offering;

    /// <summary>
    /// The outstanding request's deadline, published by the library as
    /// <see cref="NearbyConnectionRequest.ExpiresAt"/>.
    /// </summary>
    /// <remarks>
    /// Read off the device snapshot, which resolves the request against the session on every read.
    /// A page-side copy fed by a second stream raced the device change that builds this row: the
    /// row was projected before the request arrived, so the bar started with no window and nothing
    /// restarted it.
    /// <para>
    /// Gated on <see cref="NearbyConnectionRequest.IsPending"/> so the bar disappears the moment
    /// the request is answered, rather than draining on under a card whose buttons no longer do
    /// anything.
    /// </para>
    /// </remarks>
    protected override DateTimeOffset? Deadline
        => Device.Request is { IsPending: true } request
            ? request.ExpiresAt
            : null;

    /// <inheritdoc/>
    protected override TimeSpan Window => _offerWindow;

    [RelayCommand(IncludeCancelCommand = true)]
    async Task Accept(CancellationToken cancellationToken)
    {
        FailureReason = null;

        if (Device.Request is not { } request)
        {
            MarkExpired();
            return;
        }

        try
        {
            await request.AcceptAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user cancelled.
        }
        // Mark before setting the reason: marking is what reports the untouched-expiry caption,
        // and the specific reason has to land after it.
        catch (NearbyConnectException ex)
        {
            LogAcceptFailed(_logger, Device.Id, ex);
            MarkExpired();
        }
        catch (NearbyException ex)
        {
            LogAcceptFailed(_logger, Device.Id, ex);
            MarkFailed();
            FailureReason = "Device withdrew";
        }
    }

    [RelayCommand]
    async Task Decline(CancellationToken cancellationToken)
    {
        if (Device.Request is not { } request)
        {
            return;
        }

        try
        {
            await request.DeclineAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user navigated away
        }
        catch (NearbyException ex)
        {
            LogDeclineFailed(_logger, Device.Id, ex);
        }
    }


    protected override void OnDeviceChanged()
    {
        OnPropertyChanged(nameof(HasFailed));
        OnPropertyChanged(nameof(CanAnswer));

        if (Device.Status is NearbyDeviceStatus.RequestReceived)
        {
            FailureReason = null;
            ClearExpired();
        }
        // Nothing marks the row expired here: OfferWindowButton does it when the ring empties,
        // which beats the session's own timeout to the screen.

        RaiseWindowChanged();
    }

    /// <summary>
    /// Starts the exit when the request lapses: a lapsed request has nothing left to act on, so
    /// the row goes — after a hold long enough to say why.
    /// </summary>
    protected override void OnExpiredChanged()
    {
        if (!IsExpired)
        {
            return;
        }

        // Set here, not only in Accept's catch blocks: a request that nobody touched lapses
        // without any command running, so this is the one place an untouched expiry is reported.
        FailureReason = "Request expired";

        BeginLeaving();
    }

    /// <summary>
    /// A request that arrived again during the hold cleared
    /// <see cref="NearbyDeviceViewModel.IsExpired"/>: the row is live once more and must not leave.
    /// </summary>
    protected override bool ShouldStillLeave => IsExpired || IsConnected;

    [LoggerMessage(
        EventId = 3,
        EventName = nameof(LogAcceptFailed),
        Level = LogLevel.Error,
        Message = "Accepting the request from {DeviceId} failed.")]
    static partial void LogAcceptFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(
        EventId = 4,
        EventName = nameof(LogDeclineFailed),
        Level = LogLevel.Warning,
        Message = "Declining the request from {DeviceId} failed.")]
    static partial void LogDeclineFailed(ILogger logger, string deviceId, Exception exception);
}
