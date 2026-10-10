using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NearbyChat.Services;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.ViewModels;

public partial class AdvertisingPageViewModel : BasePageViewModel
{
    readonly INavigationService _navigationService;
    readonly INearbyConnectivity _nearby;
    readonly ILogger<AdvertisingPageViewModel> _logger;
    readonly TimeSpan _offerWindow;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleAdvertisingCommand))]
    public partial bool IsBusy { get; set; }

    public NearbySessionState Status { get; }

    public NearbyDeviceCollection Connections { get; }

    readonly DeviceRows<AdvertisedDeviceViewModel> _rows;

    /// <summary>
    /// The rows the list shows: the session's own collection, plus any row that has settled —
    /// lapsed or connected — and is serving out <see cref="NearbyDeviceViewModel.LeaveHold"/>.
    /// </summary>
    /// <remarks>
    /// The session drops a device the moment its request stops being outstanding, and that is
    /// correct — there is nothing left to accept. It is only the card that needs to outlive it
    /// briefly, so the hold lives here, in the view, rather than in the library.
    /// </remarks>
    public ObservableCollection<AdvertisedDeviceViewModel> AdvertisedDevices => _rows.Rows;

    public AdvertisingPageViewModel(
        IDispatcher dispatcher,
        INavigationService navigationService,
        INearbyConnectivity nearby,
        NearbySessionState status,
        ConnectionTracker connectionTracker,
        NearbyOptions options,
        TimeProvider timeProvider,
        ILogger<AdvertisingPageViewModel> logger)
        : base(dispatcher)
    {
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentNullException.ThrowIfNull(nearby);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(connectionTracker);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _offerWindow = options.OfferWindow;

        _navigationService = navigationService;
        _nearby = nearby;
        _logger = logger;
        Status = status;
        Connections = connectionTracker.Connections;

        _rows = new DeviceRows<AdvertisedDeviceViewModel>(
            _nearby,
            Dispatcher,
            project: device => new AdvertisedDeviceViewModel(device, _offerWindow, timeProvider, _logger),
            // Connecting is kept so the row is reused through the handshake rather than dropped
            // and re-projected already connected — a fresh row never starts its exit.
            filter: static device => device.Status is NearbyDeviceStatus.RequestReceived
                or NearbyDeviceStatus.Connecting
                or NearbyDeviceStatus.Connected,
            // A settled row — lapsed or connected — is kept while it holds and plays its exit.
            lingers: static row => row.IsExpired || row.IsConnected,
            // But only a connection releases the row against the session's own view: a lapsed
            // request the session still lists is one it has not finished with.
            leavesOnItsOwnTerms: static row => row.IsConnected,
            onLeft: row =>
            {
                if (row.IsConnected)
                {
                    RaiseConnectedRowLeft();
                }
            });
    }

    [RelayCommand]
    Task Back()
        => _navigationService.GoBackAsync();

    [RelayCommand]
    Task NavigateToConnections()
        => _navigationService.GoToAsync<ConnectionsPageViewModel>();

    [RelayCommand(CanExecute = nameof(CanToggleAdvertising))]
    async Task ToggleAdvertising(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            await ToggleActivityAsync(
                noun: "Advertising",
                isActive: _nearby.Advertiser.IsActive,
                startAsync: _nearby.Advertiser.StartAsync,
                stopAsync: _nearby.Advertiser.StopAsync,
                availability: () => _nearby.Availability,
                permissionNeed: "advertise",
                permissionPrompt: "Tap Start Advertising again and allow the permission.",
                cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected internal override void OnAppearing()
    {
        base.OnAppearing();

        _rows.Start(AppearanceToken);
        TrackRelativeTime(_rows.Rows);
    }

    bool CanToggleAdvertising() => !IsBusy;

    protected override void OnToggleUnavailable(NearbyAvailability availability)
        => LogUnavailable(_logger, availability);

    protected override void OnToggleFailed(bool wasActive, Exception exception)
        => LogToggleFailed(_logger, wasActive, exception);

    [LoggerMessage(
        EventId = 1,
        EventName = nameof(LogToggleFailed),
        Level = LogLevel.Error,
        Message = "Toggling advertising failed. Was advertising: {WasAdvertising}.")]
    static partial void LogToggleFailed(ILogger logger, bool wasAdvertising, Exception exception);

    [LoggerMessage(
        EventId = 2,
        EventName = nameof(LogUnavailable),
        Level = LogLevel.Warning,
        Message = "Advertising was not started: {Availability}.")]
    static partial void LogUnavailable(ILogger logger, NearbyAvailability availability);
}
