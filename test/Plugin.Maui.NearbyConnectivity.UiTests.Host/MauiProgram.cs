using Plugin.Maui.NearbyConnectivity.UiTests.Host.Pages;
#if DEBUG
using Microsoft.Extensions.Logging;
#endif

namespace Plugin.Maui.NearbyConnectivity.UiTests.Host;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder()
            .UseMauiApp<App>();

        // ServiceId must match the NSBonjourServices entries in Platforms/iOS/Info.plist.
        // AcceptAll is deliberately NOT used: the default Hold policy is what surfaces inbound
        // requests on each device, which AdvertisingPage renders and the tests assert on.
        builder.Services.AddNearbyConnectivity(options =>
        {
            options.ServiceId = "nearbyuitests";
            options.Presence = new() { DisplayName = $"uitests-{DeviceInfo.Current.Name}" };
        });

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<PermissionsPage>();
        builder.Services.AddTransient<AvailabilityPage>();
        builder.Services.AddTransient<AdvertisingPage>();
        builder.Services.AddTransient<DiscoveryPage>();
        builder.Services.AddTransient<ConnectionsPage>();
        builder.Services.AddTransient<PayloadsPage>();

#if DEBUG
        builder.Logging.AddDebug();
        builder.Logging.AddFilter("Plugin.Maui.NearbyConnectivity", LogLevel.Trace);
#endif

        return builder.Build();
    }
}
