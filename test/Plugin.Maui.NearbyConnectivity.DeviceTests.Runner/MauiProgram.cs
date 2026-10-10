using DeviceRunners.VisualRunners;
#if DEBUG
using Microsoft.Extensions.Logging;
#endif

namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Runner;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseVisualTestRunner(conf => conf
                .AddCliConfiguration()
                .AddConsoleResultChannel()
                .AddTestAssembly(typeof(Plugin.Maui.NearbyConnectivity.DeviceTests.AssemblyMarker).Assembly)
                .AddXunit3());

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
