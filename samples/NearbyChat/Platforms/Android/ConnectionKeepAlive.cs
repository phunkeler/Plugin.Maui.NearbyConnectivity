using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace NearbyChat.Services;

/// <summary>
/// Keeps an ongoing notification up while any device is connected, so the person can see that
/// NearbyChat holds the connection while the app is in the background.
/// </summary>
/// <remarks>
/// The notification belongs to a foreground service of type <c>connectedDevice</c>. Besides
/// telling the person, that tells Android the app is doing work the person asked for, which makes
/// the system less likely to stop the process, and the connection with it, while it is out of
/// sight. The notification permission is asked for when the first device connects; without it
/// the service still runs and Android lists it in the task manager instead of the shade.
/// </remarks>
public static class ConnectionKeepAlive
{
    /// <summary>
    /// Shows, updates or removes the notification for <paramref name="connectionCount"/> connected
    /// devices.
    /// </summary>
    /// <remarks>
    /// <b>Never throws.</b> Android refuses to start a foreground service from the background, and
    /// a connection that changes while the app is backgrounded must not crash it: the notification
    /// is a courtesy, the connection itself is unaffected.
    /// </remarks>
    public static void Update(int connectionCount)
    {
        var context = Platform.AppContext;
        var intent = new Intent(context, typeof(ConnectionKeepAliveService))
            .PutExtra(ConnectionKeepAliveService.ConnectionCountExtra, connectionCount);

        try
        {
            if (connectionCount <= 0)
            {
                context.StopService(intent);
                return;
            }

            AskToNotify();

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
        }
        catch (Java.Lang.IllegalStateException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Connection notification not updated: {ex}");
        }
    }

    static void AskToNotify()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() is not PermissionStatus.Granted)
                {
                    await Permissions.RequestAsync<Permissions.PostNotifications>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification permission request failed: {ex}");
            }
        });
    }
}

/// <summary>
/// The foreground service behind <see cref="ConnectionKeepAlive"/>. It does no work of its own;
/// it exists to carry the ongoing notification.
/// </summary>
[Service(
    Name = "com.phunkeler.nearbychat.ConnectionKeepAliveService",
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class ConnectionKeepAliveService : Service
{
    internal const string ConnectionCountExtra = "connectionCount";

    const string ChannelId = "connections";
    const int NotificationId = 1001;

    /// <inheritdoc/>
    public override IBinder? OnBind(Intent? intent) => null;

    /// <inheritdoc/>
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var count = Math.Max(1, intent?.GetIntExtra(ConnectionCountExtra, 1) ?? 1);

        EnsureChannel();

        var notification = BuildNotification(count);

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeConnectedDevice);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }

        return StartCommandResult.NotSticky;
    }

    /// <summary>
    /// Stops the service when the person swipes the app away, so the notification does not outlive
    /// the connections it describes.
    /// </summary>
    public override void OnTaskRemoved(Intent? rootIntent)
    {
        StopSelf();
        base.OnTaskRemoved(rootIntent);
    }

    void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)
            || GetSystemService(NotificationService) is not NotificationManager manager)
        {
            return;
        }

        var channel = new NotificationChannel(ChannelId, "Connections", NotificationImportance.Low)
        {
            Description = "Shows while a nearby device is connected.",
        };

        manager.CreateNotificationChannel(channel);
    }

    Notification BuildNotification(int count)
    {
        var open = PackageName is { } packageName ? PackageManager?.GetLaunchIntentForPackage(packageName) : null;
        var tap = open is null
            ? null
            : PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        using var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetContentTitle(count == 1 ? "Connected to 1 device" : $"Connected to {count} devices");
        builder.SetContentText("NearbyChat keeps the connection open in the background.");
        builder.SetSmallIcon(Android.Resource.Drawable.StatSysDataBluetooth);
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetContentIntent(tap);
        builder.SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate);

        return builder.Build() ?? throw new InvalidOperationException("The notification builder returned no notification.");
    }
}
