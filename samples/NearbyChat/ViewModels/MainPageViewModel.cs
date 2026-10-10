using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NearbyChat.Services;
using Plugin.Maui.BottomSheet.Navigation;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.ViewModels;

public partial class MainPageViewModel : BasePageViewModel
{
    readonly INavigationService _navigationService;
    readonly IBottomSheetNavigationService _bottomSheetNavigationService;
    readonly INearbyConnectivity _nearby;
    readonly IAppInfo _appInfo;
    readonly ILogger<MainPageViewModel> _logger;

    public MainPageViewModel(
        IDispatcher dispatcher,
        INavigationService navigationService,
        IBottomSheetNavigationService bottomSheetNavigationService,
        INearbyConnectivity nearby,
        IAppInfo appInfo,
        NearbySessionState status,
        ConnectionTracker connectionTracker,
        ILogger<MainPageViewModel> logger)
        : base(dispatcher)
    {
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentNullException.ThrowIfNull(bottomSheetNavigationService);
        ArgumentNullException.ThrowIfNull(nearby);
        ArgumentNullException.ThrowIfNull(appInfo);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(connectionTracker);
        ArgumentNullException.ThrowIfNull(logger);

        _navigationService = navigationService;
        _bottomSheetNavigationService = bottomSheetNavigationService;
        _nearby = nearby;
        _appInfo = appInfo;
        _logger = logger;
        OwnPresence = nearby.Presence;
        Status = status;
        Connections = connectionTracker.Connections;
    }

    /// <summary>
    /// What other devices see for this one: the session's effective presence.
    /// </summary>
    [ObservableProperty]
    public partial NearbyPresence OwnPresence { get; private set; }

    /// <summary>
    /// The page's status line: availability, both activities, and why the last one stopped.
    /// </summary>
    public NearbySessionState Status { get; }

    public NearbyDeviceCollection Connections { get; }

    protected internal override void OnAppearing()
    {
        base.OnAppearing();
        RefreshPresence();
    }

    /// <summary>
    /// Reads the effective presence again, after the sheet saved one.
    /// </summary>
    internal void RefreshPresence() => OwnPresence = _nearby.Presence;

    [RelayCommand]
    Task NavigateToAdvertising()
        => _navigationService.GoToAsync<AdvertisingPageViewModel>();

    [RelayCommand]
    Task NavigateToDiscovery()
        => _navigationService.GoToAsync<DiscoveryPageViewModel>();

    [RelayCommand]
    Task NavigateToConnections()
        => _navigationService.GoToAsync<ConnectionsPageViewModel>();

    [RelayCommand]
    async Task ChangePresence()
    {
        var result = await _bottomSheetNavigationService.NavigateToAsync(
            nameof(PresenceViewModel),
            new BottomSheetNavigationParameters
            {
                { PresenceViewModel.OnSavedParameter, (Action)RefreshPresence }
            });

        if (result.Success || result.Cancelled)
        {
            return;
        }

        LogPresenceOpenFailed(_logger, result.Exception);
    }

    /// <summary>
    /// Asks for the nearby permissions, or opens the app's settings once the system will no longer
    /// ask. The availability warning clears on its own when the session sees the grant.
    /// </summary>
    [RelayCommand]
    async Task GrantAccess()
    {
        if (await Permissions.RequestAsync<NearbyPermission>() is PermissionStatus.Granted)
        {
            return;
        }

        if (!Permissions.ShouldShowRationale<NearbyPermission>())
        {
            _appInfo.ShowSettingsUI();
        }
    }

    [LoggerMessage(
        EventId = 1,
        EventName = nameof(LogPresenceOpenFailed),
        Level = LogLevel.Error,
        Message = "Opening the presence sheet failed.")]
    static partial void LogPresenceOpenFailed(ILogger logger, Exception? exception);
}
