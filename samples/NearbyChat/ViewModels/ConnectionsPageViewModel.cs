using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using NearbyChat.Data;
using NearbyChat.Services;
using Plugin.Maui.BottomSheet.Navigation;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.ViewModels;

public partial class ConnectionsPageViewModel : BasePageViewModel
{
    readonly INavigationService _navigationService;
    readonly INearbyConnectivity _nearby;
    readonly IBottomSheetNavigationService _bottomSheetNavigationService;
    readonly ConnectionConfirmations _confirmations;
    readonly ChatMessageStore _store;
    readonly ConnectionTracker _tracker;
    readonly IMessenger _messenger;
    readonly ILogger<ConnectionsPageViewModel> _logger;

    public ConnectionsPageViewModel(
        IDispatcher dispatcher,
        INavigationService navigationService,
        INearbyConnectivity nearby,
        IBottomSheetNavigationService bottomSheetNavigationService,
        ConnectionConfirmations confirmations,
        ChatMessageStore store,
        ConnectionTracker tracker,
        IMessenger messenger,
        TimeProvider timeProvider,
        ILogger<ConnectionsPageViewModel> logger)
        : base(dispatcher)
    {
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentNullException.ThrowIfNull(nearby);
        ArgumentNullException.ThrowIfNull(bottomSheetNavigationService);
        ArgumentNullException.ThrowIfNull(confirmations);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _navigationService = navigationService;
        _nearby = nearby;
        _bottomSheetNavigationService = bottomSheetNavigationService;
        _confirmations = confirmations;
        _store = store;
        _tracker = tracker;
        _messenger = messenger;
        _logger = logger;

        _rows = new DeviceRows<ConnectedDeviceViewModel>(
            _nearby,
            Dispatcher,
            project: device => new ConnectedDeviceViewModel(device, _bottomSheetNavigationService, _confirmations, _store, _tracker, _messenger, timeProvider, _logger),
            filter: static device => device.Status is NearbyDeviceStatus.Connected,
            // A disconnecting row is kept while it plays its exit. Every other dropped row goes at
            // once — a peer that vanished on its own has no outcome of ours to state.
            lingers: static row => row.IsDisconnecting);
    }

    readonly DeviceRows<ConnectedDeviceViewModel> _rows;

    /// <summary>
    /// The rows the list shows: the session's connected devices, plus any row that is serving out
    /// its disconnect before it goes.
    /// </summary>
    /// <remarks>
    /// A device that leaves <see cref="NearbyDeviceStatus.Connected"/> stops matching the filter
    /// and its row is dropped — unless this page disconnected it, which is its own terminal state
    /// and has to outlive the session's view to say so.
    /// </remarks>
    public ObservableCollection<ConnectedDeviceViewModel> ConnectedDevices => _rows.Rows;

    protected internal override void OnAppearing()
    {
        base.OnAppearing();

        _rows.Start(AppearanceToken);
        TrackRelativeTime(_rows.Rows);
    }

    [RelayCommand]
    Task Back()
        => _navigationService.GoBackAsync();
}
