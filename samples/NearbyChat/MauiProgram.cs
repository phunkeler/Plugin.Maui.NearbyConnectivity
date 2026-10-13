using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Maui;
using CommunityToolkit.Mvvm.Messaging;
using NearbyChat.Controls;
using NearbyChat.Data;
using NearbyChat.Pages;
using NearbyChat.Services;
using NearbyChat.ViewModels;
using Plugin.Maui.BottomSheet.Hosting;
using Plugin.Maui.NearbyConnectivity;
#if DEBUG
using Microsoft.Extensions.Logging;
using Microsoft.Maui.DevFlow.Agent;
#endif

namespace NearbyChat;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder()
            .UseMauiApp<App>()

#if DEBUG
            .AddMauiDevFlowAgent()
#endif
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("NearbyChatIcons.ttf", "NearbyChatIcons");
            })
            .UseMauiCommunityToolkit()
            .UseBottomSheet()
            .AddNearbyConnectivity(opts =>
            {
                opts.ServiceId = "nearbychat";
                opts.OfferWindow = TimeSpan.FromSeconds(10);
            });

#if DEBUG
        builder.Logging.AddDebug();
        builder.Logging.AddFilter("Plugin.Maui.NearbyConnectivity", LogLevel.Trace);
#endif

        builder.Services.AddSingleton(MediaPicker.Default);
        builder.Services.AddSingleton(Launcher.Default);
        builder.Services.AddSingleton(DeviceDisplay.Current);
        builder.Services.AddSingleton(AppInfo.Current);
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<ChatMessageStore>();
        builder.Services.AddSingleton(services => new NearbyDeviceCollection(
            services.GetRequiredService<INearbyConnectivity>(),
            services.GetRequiredService<IDispatcher>(),
            static device => device.Status is NearbyDeviceStatus.Connected));
        builder.Services.AddSingleton<ConnectionTracker>();
        builder.Services.AddSingleton<ConnectionConfirmations>();
        builder.Services.AddSingleton(services =>
            new NearbySessionState(services.GetRequiredService<INearbyConnectivity>()));

        // Inbound payload ingestion. No initializer ritual: PayloadsAsync reads the connections
        // already open, and an unconsumed connection buffers its payloads — a consumer that starts
        // late misses nothing. App's constructor resolves this once at startup.
        builder.Services.AddSingleton<NearbyIngestionService>();

        builder.Services.AddTransientWithShellRoute<AdvertisingPage, AdvertisingPageViewModel>();
        builder.Services.AddTransientWithShellRoute<ConnectionsPage, ConnectionsPageViewModel>();
        builder.Services.AddTransientWithShellRoute<DiscoveryPage, DiscoveryPageViewModel>();
        builder.Services.AddTransientWithShellRoute<MainPage, MainPageViewModel>();

        builder.Services.AddBottomSheet<ChatBottomSheet, ChatViewModel>(nameof(ChatViewModel));
        builder.Services.AddBottomSheet<PeerDataBottomSheet, PeerDataViewModel>(nameof(PeerDataViewModel));
        builder.Services.AddBottomSheet<PresenceBottomSheet, PresenceViewModel>(nameof(PresenceViewModel));

        RemoveEntryChrome();

        return builder.Build();
    }

    /// <summary>
    /// Drops the platform's own underline or border from every entry. The chat composer and the
    /// name sheet draw a rounded field around theirs instead.
    /// </summary>
    static void RemoveEntryChrome()
        => Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping(nameof(RemoveEntryChrome), (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList =
                Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif IOS
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
        });

    static IServiceCollection AddTransientWithShellRoute<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPage,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>
        (this IServiceCollection services)
        where TPage : BasePage<TViewModel>
        where TViewModel : BasePageViewModel
    {
        return services.AddTransientWithShellRoute<TPage, TViewModel>(AppShell.GetPageRoute<TViewModel>());
    }
}
