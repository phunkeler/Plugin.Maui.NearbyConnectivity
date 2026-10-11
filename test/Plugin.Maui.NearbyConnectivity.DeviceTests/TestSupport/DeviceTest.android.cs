using Android.Content.PM;

namespace Plugin.Maui.NearbyConnectivity.DeviceTests;

public abstract partial class DeviceTest
{
    /// <summary>
    /// Skips a test that starts the real radio when the device declares neither Wi-Fi nor
    /// Bluetooth hardware. GMS fails every start on such a device with <c>STATUS_RADIO_ERROR</c>.
    /// </summary>
    /// <remarks>
    /// The emulator images below API 25 are such devices. They run a Wi-Fi service that reports
    /// itself enabled, so the plugin's own availability check returns <c>Available</c> on them and
    /// cannot serve as this gate.
    /// </remarks>
    private protected static void SkipUnlessRadioHardware()
    {
        var packages = Android.App.Application.Context.PackageManager;
        Assert.SkipUnless(
            packages?.HasSystemFeature(PackageManager.FeatureWifi) == true
                || packages?.HasSystemFeature(PackageManager.FeatureBluetooth) == true,
            "The device declares neither Wi-Fi nor Bluetooth hardware.");
    }
}
