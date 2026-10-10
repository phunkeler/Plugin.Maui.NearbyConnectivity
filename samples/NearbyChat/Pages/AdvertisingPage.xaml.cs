using System.ComponentModel;
using CommunityToolkit.Maui;
using NearbyChat.Controls;
using NearbyChat.ViewModels;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.Pages;

public partial class AdvertisingPage : BasePage<AdvertisingPageViewModel>
{
    readonly Color _inactiveColor;
    readonly Color _pulseColor;

    public AdvertisingPage(AdvertisingPageViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();

        var quaternary = (AppThemeColor)Application.Current!.Resources["TextQuaternary"];
        _inactiveColor = (Application.Current.RequestedTheme == AppTheme.Dark ? quaternary.Dark : quaternary.Light)
            ?? throw new InvalidOperationException("TextQuaternary has no colour for the current theme.");
        _pulseColor = (Color)Application.Current.Resources["AccentAdvertising"];
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        ViewModel.Status.PropertyChanged += OnStatusPropertyChanged;
        ViewModel.ConnectedRowLeft += OnConnectedRowLeft;

        if (ViewModel.Status.IsAdvertising)
        {
            StartPulseAnimation();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        ViewModel.Status.PropertyChanged -= OnStatusPropertyChanged;
        ViewModel.ConnectedRowLeft -= OnConnectedRowLeft;
        StopPulseAnimation();
    }

    void OnConnectedRowLeft(object? sender, EventArgs e)
        => ConnectionsChip.Celebrate();

    void OnStatusPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NearbySessionState.IsAdvertising))
        {
            if (ViewModel.Status.IsAdvertising)
            {
                StartPulseAnimation();
            }
            else
            {
                StopPulseAnimation();
            }
        }
    }

    void StartPulseAnimation()
    {
        if (AntennaIcon is null || AntennaIconSource is null)
        {
            return;
        }

        AntennaIconSource.Color = _pulseColor;
        AntennaIcon.StartPulse();
    }

    void StopPulseAnimation()
    {
        if (AntennaIcon is null || AntennaIconSource is null)
        {
            return;
        }

        AntennaIcon.StopPulse();
        AntennaIconSource.Color = _inactiveColor;
    }
}
