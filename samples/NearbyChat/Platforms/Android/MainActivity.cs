using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using AndroidX.Core.View;

namespace NearbyChat;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density
)]
public class MainActivity : MauiAppCompatActivity
{
    bool _isCovered;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        MatchStatusBarToTheme(Resources?.Configuration);
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        MatchStatusBarToTheme(newConfig);
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        _isCovered = !hasFocus;
        MatchStatusBarToTheme(Resources?.Configuration);
    }

    /// <summary>
    /// Asks for dark status bar icons on the light theme and light icons on the dark one. The pages
    /// draw their own ground under the status bar, so the system cannot pick for them.
    /// </summary>
    /// <remarks>
    /// A bottom sheet is a dialog over this window, and its scrim darkens the status bar on either
    /// theme. <c>SheetScrim</c> asks for light icons before a sheet opens; this window loses focus
    /// while one is open and puts the theme's own style back when focus returns.
    /// </remarks>
    void MatchStatusBarToTheme(Configuration? configuration)
    {
        if (Window is not { } window
            || configuration is null
            || WindowCompat.GetInsetsController(window, window.DecorView) is not { } insets)
        {
            return;
        }

        var isNight = (configuration.UiMode & UiMode.NightMask) == UiMode.NightYes;
        insets.AppearanceLightStatusBars = !isNight && !_isCovered;
    }
}
