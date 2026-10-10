using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.ViewModels;

/// <summary>
/// A discovered, not-yet-connected, device row.
/// </summary>
public partial class DiscoveredDeviceViewModel : NearbyDeviceViewModel
{
    readonly TimeSpan _offerWindow;
    readonly ILogger _logger;

    public DiscoveredDeviceViewModel(
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
    public partial string? FailureReason { get; set; }

    public bool HasFailed => FailureReason is not null;
    protected override DateTimeOffset? Deadline => Device.ConnectExpiresAt;
    protected override TimeSpan Window => _offerWindow;

    [RelayCommand(IncludeCancelCommand = true)]
    async Task Connect(CancellationToken cancellationToken)
    {
        FailureReason = null;
        ClearExpired();
        RaiseWindowChanged();

        try
        {
            await Device.ConnectAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // We cancelled
        }
        catch (NearbyConnectException ex)
        {
            if (Describe(ex.Reason) is not { } described)
            {
                LogConnectFailed(_logger, Device.Id, ex);
                described = "Couldn't connect";
            }

            // Mark before setting the reason: a DataTrigger restores the value it found when it
            // applied, so HasFailed has to find "Connect", not "Awaiting...". Marking the row
            // spent closes the window, which un-applies the "Awaiting..." trigger first.
            MarkOutcome(ex.Reason);
            FailureReason = described;
        }
        catch (NearbyException ex)
        {
            LogConnectFailed(_logger, Device.Id, ex);
            MarkFailed();
            FailureReason = "Couldn't connect";
        }
        finally
        {
            RaiseWindowChanged();
        }
    }

    static string? Describe(NearbyConnectFailure reason) => reason switch
    {
        NearbyConnectFailure.TimedOut => "No answer",
        NearbyConnectFailure.DeviceUnknown => "No longer nearby",
        NearbyConnectFailure.Failed => "Didn't connect",
        _ => null,
    };

    void MarkOutcome(NearbyConnectFailure reason)
    {
        if (reason is NearbyConnectFailure.TimedOut)
        {
            MarkExpired();
        }
        else
        {
            MarkFailed();
        }
    }

    protected override void OnExpiredChanged()
    {
        if (!IsExpired)
        {
            return;
        }

        _ = HoldAsync();

        async Task HoldAsync()
        {
            await Task.Delay(LeaveHold, TimeProvider);

            if (IsExpired)
            {
                SettleExpiry();
                FailureReason = null;
            }
        }
    }

    protected override void OnDeviceChanged()
    {
        if (IsConnecting)
        {
            FailureReason = null;
        }

        RaiseWindowChanged();
    }

    [LoggerMessage(
        EventId = 1,
        EventName = nameof(LogConnectFailed),
        Level = LogLevel.Error,
        Message = "Connecting to {DeviceId} failed.")]
    static partial void LogConnectFailed(ILogger logger, string deviceId, Exception exception);
}
