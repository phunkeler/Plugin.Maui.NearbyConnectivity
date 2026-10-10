using Plugin.Maui.NearbyConnectivity;
#if DEBUG
using Microsoft.Extensions.Logging;
#endif

namespace Minimal;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();

        #region register
        builder.AddNearbyConnectivity(options =>
        {
            options.ServiceId = "nearbyminimal";
        });
        #endregion

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
