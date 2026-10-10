using Android.App;
using Android.Content.PM;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Host;

// Name is explicit so the activity has a stable, greppable Java class name. Without it the binding
// generator emits a hashed name (crc64<hash>.MainActivity) that changes between builds. The UI
// tests still resolve the launcher activity at runtime via `adb shell cmd package resolve-activity`
// (see scripts/device-tests.ps1) rather than hardcoding this — appium:noReset skips UiAutomator2's
// own manifest-reading auto-detection, so the capability must be supplied explicitly.
[Activity(
    Name = "com.phunkeler.nearbyconnectivity.uitests.MainActivity",
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
}
