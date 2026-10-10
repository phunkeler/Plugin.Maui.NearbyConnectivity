using System.ComponentModel;
using CommunityToolkit.Maui;
using NearbyChat.Controls;
using NearbyChat.ViewModels;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.Pages;

public partial class DiscoveryPage : BasePage<DiscoveryPageViewModel>
{
    readonly Color _inactiveColor;
    readonly Color _pulseColor;

    public DiscoveryPage(DiscoveryPageViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();

        var quaternary = (AppThemeColor)Application.Current!.Resources["TextQuaternary"];
        _inactiveColor = (Application.Current.RequestedTheme == AppTheme.Dark ? quaternary.Dark : quaternary.Light)
            ?? throw new InvalidOperationException("TextQuaternary has no colour for the current theme.");
        _pulseColor = (Color)Application.Current.Resources["AccentDiscovery"];
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        ViewModel.Status.PropertyChanged += OnStatusPropertyChanged;
        ViewModel.ConnectedRowLeft += OnConnectedRowLeft;

        if (ViewModel.Status.IsDiscovering)
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

    void OnConnectedRowLeft(object? sender, EventArgs e) => ConnectionsChip.Celebrate();

    void OnStatusPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NearbySessionState.IsDiscovering))
        {
            if (ViewModel.Status.IsDiscovering)
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
        if (SonarIcon is null || SonarIconSource is null)
        {
            return;
        }

        SonarIconSource.Color = _pulseColor;
        SonarIcon.StartPulse();
    }

    void StopPulseAnimation()
    {
        if (SonarIcon is null || SonarIconSource is null)
        {
            return;
        }

        SonarIcon.StopPulse();
        SonarIconSource.Color = _inactiveColor;
    }
}
