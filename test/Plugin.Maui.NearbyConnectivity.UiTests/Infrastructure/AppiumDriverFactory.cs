using System.Globalization;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Infrastructure;

/// <summary>
/// Builds the platform driver from environment the CI script exports. This is the only place that
/// names a capability; everything above it is page objects.
/// </summary>
static class AppiumDriverFactory
{
    public const string AppId = "com.phunkeler.nearbyconnectivity.uitests";

    /// <summary>Set by scripts/device-tests.ps1 when it starts the Appium server.</summary>
    public const string ServerUrlVariable = "APPIUM_URL";

    public static bool IsConfigured => Environment.GetEnvironmentVariable(ServerUrlVariable) is not null;

    public static AppiumDriver Create()
    {
        var serverUrl = new Uri(Required(ServerUrlVariable));
        var platform = Environment.GetEnvironmentVariable("UITEST_PLATFORM") ?? "android";

        return platform.Equals("ios", StringComparison.OrdinalIgnoreCase)
            ? CreateIos(serverUrl)
            : CreateAndroid(serverUrl);
    }

    static AndroidDriver CreateAndroid(Uri serverUrl)
    {
        var options = new AppiumOptions { PlatformName = "Android", AutomationName = "UIAutomator2" };
        options.AddAdditionalAppiumOption("udid", Required("DEVICE_SERIAL"));
        options.AddAdditionalAppiumOption("appPackage", AppId);
        // .NET for Android mangles the launcher activity's Java class name, and appium:noReset
        // skips UiAutomator2's own manifest-reading auto-detection (it assumes the app is already
        // installed), so this must be supplied explicitly. device-tests.ps1 resolves it per run
        // with `adb shell cmd package resolve-activity`.
        options.AddAdditionalAppiumOption("appActivity", Required("APP_ACTIVITY"));
        options.AddAdditionalAppiumOption("noReset", true);
        options.AddAdditionalAppiumOption("newCommandTimeout", 120);
        options.AddAdditionalAppiumOption("skipUnlock", true);

        return new AndroidDriver(serverUrl, options, TimeSpan.FromSeconds(120));
    }

    static IOSDriver CreateIos(Uri serverUrl)
    {
        var options = new AppiumOptions { PlatformName = "iOS", AutomationName = "XCUITest" };
        options.AddAdditionalAppiumOption("udid", Required("DEVICE_SERIAL"));
        options.AddAdditionalAppiumOption("bundleId", AppId);
        options.AddAdditionalAppiumOption("noReset", true);
        options.AddAdditionalAppiumOption("newCommandTimeout", 120);

        return new IOSDriver(serverUrl, options, TimeSpan.FromSeconds(120));
    }

    static string Required(string key)
        => Environment.GetEnvironmentVariable(key)
            ?? throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, "Required environment variable '{0}' is not set.", key));
}
