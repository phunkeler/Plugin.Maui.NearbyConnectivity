using Foundation;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Host;

[Register(nameof(AppDelegate))]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
