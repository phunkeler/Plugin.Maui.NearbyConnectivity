using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NearbyChat.Services;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.ViewModels;

public partial class DiscoveryPageViewModel : BasePageViewModel
{
    readonly INavigationService _navigationService;
    readonly INearbyConnectivity _nearby;
    readonly ILogger<DiscoveryPageViewModel> _logger;
    readonly TimeSpan _offerWindow;
    readonly DeviceRows<DiscoveredDeviceViewModel> _rows;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleDiscoveryCommand))]
    public partial bool IsBusy { get; set; }

    public NearbySessionState Status { get; }

    public NearbyDeviceCollection Connections { get; }

    /// <summary>
    /// The rows the list shows: the session's own collection, plus any row that has just connected
    /// and is serving out <see cref="NearbyDeviceViewModel.LeaveHold"/>.
    /// </summary>
    public ObservableCollection<DiscoveredDeviceViewModel> DiscoveredDevices => _rows.Rows;

    public DiscoveryPageViewModel(
        IDispatcher dispatcher,
        INavigationService navigationService,
        INearbyConnectivity nearby,
        NearbySessionState status,
        ConnectionTracker connectionTracker,
        NearbyOptions options,
        TimeProvider timeProvider,
        ILogger<DiscoveryPageViewModel> logger)
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

        // No seeding step: the stream replays every known device as Added, so devices found while
        // the page was away arrive on their own.
        _rows = new DeviceRows<DiscoveredDeviceViewModel>(
            _nearby,
            Dispatcher,
            project: device => new DiscoveredDeviceViewModel(device, _offerWindow, timeProvider, _logger),
            filter: static device => device.Status is NearbyDeviceStatus.Visible
                or NearbyDeviceStatus.Connecting
                or NearbyDeviceStatus.Connected,
            // A connected row is kept while it holds and plays its exit: the session has moved the
            // device to the connections list, but the card should say so before it goes.
            lingers: static row => row.IsConnected,
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

    [RelayCommand(CanExecute = nameof(CanToggleDiscovery))]
    async Task ToggleDiscovery(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            await ToggleActivityAsync(
                noun: "Discovery",
                isActive: _nearby.Discoverer.IsActive,
                startAsync: _nearby.Discoverer.StartAsync,
                stopAsync: _nearby.Discoverer.StopAsync,
                availability: () => _nearby.Availability,
                permissionNeed: "find devices",
                permissionPrompt: "Tap Start Discovery again and allow the permission.",
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

    bool CanToggleDiscovery() => !IsBusy;

    protected override void OnToggleUnavailable(NearbyAvailability availability)
        => LogUnavailable(_logger, availability);

    protected override void OnToggleFailed(bool wasActive, Exception exception)
        => LogToggleFailed(_logger, wasActive, exception);

    [LoggerMessage(
        EventId = 1,
        EventName = nameof(LogToggleFailed),
        Level = LogLevel.Error,
        Message = "Toggling discovery failed. Was discovering: {WasDiscovering}.")]
    static partial void LogToggleFailed(ILogger logger, bool wasDiscovering, Exception exception);

    [LoggerMessage(
        EventId = 2,
        EventName = nameof(LogUnavailable),
        Level = LogLevel.Warning,
        Message = "Discovery was not started: {Availability}.")]
    static partial void LogUnavailable(ILogger logger, NearbyAvailability availability);
}
