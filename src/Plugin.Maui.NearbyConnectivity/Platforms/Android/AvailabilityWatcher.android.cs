using Android.Bluetooth;
using Android.Content;
using Android.Net.Wifi;

namespace Plugin.Maui.NearbyConnectivity;

// Raises IPlatformAdapter.AvailabilityChanged when the user turns a radio on or off. A private
// detail of AndroidAdapter.
//
// Runtime permissions have no broadcast an application can receive: a revoke kills the process,
// and a grant arrives either through the application's own request or in Settings while the app
// is in the background. The resume callback covers that second path, so MissingPermissions clears
// when the user comes back from Settings rather than on the next start. Google Play services has
// no availability broadcast; it is re-read on the same signals, because a refresh re-runs the
// whole worst-first ladder rather than patching one input.
//
// Registration is explicitly NOT-EXPORTED: these are protected system broadcasts, so no other
// application can send them. From API 34 a context-registered receiver must say so or the platform
// throws; the flag exists from 33, so below that the unflagged overload is the only one.
[SuppressMessage(
    "Usage",
    "CA2213:Disposable fields should be disposed",
    Justification = "Dispose takes _receiver out of the field, unregisters it, and disposes it " +
        "through a local.")]
sealed class AvailabilityWatcher : IDisposable
{
    readonly ILogger _logger;
    readonly Action _onChanged;

    StateReceiver? _receiver;
    int _disposeGuard;

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The IntentFilter is a Java peer the context keeps for the receiver's registration.")]
    internal AvailabilityWatcher(Action onChanged, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        ArgumentNullException.ThrowIfNull(logger);

        _onChanged = onChanged;
        _logger = logger;

        try
        {
            var context = Platform.AppContext;
            var receiver = new StateReceiver(this);

            var filter = new IntentFilter();
            filter.AddAction(BluetoothAdapter.ActionStateChanged);
            filter.AddAction(WifiManager.WifiStateChangedAction);

            // The export flag exists from API 33 and is mandatory from 34 for a context-registered
            // receiver, so below 33 the unflagged overload is the only one there is. These are
            // protected system broadcasts either way — no other application can send them.
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                context.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
            }
            else
            {
                context.RegisterReceiver(receiver, filter);
            }

            _receiver = receiver;

            Platform.ActivityStateChanged += OnActivityStateChanged;
        }
        catch (Exception ex)
        {
            // A watcher that cannot register is a degraded feature, not a broken session: the
            // cached value still updates on every check and start, exactly as it did before.
            NearbyLog.AvailabilityWatchFailed(_logger, ex);
        }
    }

    void OnActivityStateChanged(object? sender, ActivityStateChangedEventArgs e)
    {
        if (e.State == ActivityState.Resumed)
        {
            _onChanged();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeGuard, 1) != 0)
        {
            return;
        }

        if (_receiver is not { } receiver)
        {
            return;
        }

        _receiver = null;

        Platform.ActivityStateChanged -= OnActivityStateChanged;

        try
        {
            Platform.AppContext.UnregisterReceiver(receiver);
        }
        catch (Exception ex)
        {
            // Unregistering a receiver the platform already tore down throws rather than no-opping.
            NearbyLog.AvailabilityUnwatchFailed(_logger, ex);
        }
        finally
        {
            receiver.Dispose();
        }
    }

    // A nested type so the watcher's own members stay off the public Android surface — a
    // BroadcastReceiver subclass is registered with the platform and cannot be made private to a
    // method.
    sealed class StateReceiver(AvailabilityWatcher watcher) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent) => watcher._onChanged();
    }
}
