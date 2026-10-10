namespace Plugin.Maui.NearbyConnectivity;

// Raises IPlatformAdapter.Suspended when iOS backgrounds the app. A private detail of IosAdapter.
//
// Why this exists at all: MPC has no background mode. A normal app is suspended within seconds of
// backgrounding, and the session fails fast and silently — roughly the first second, reporting
// MCSessionState.NotConnected with no NSError. Apple DTS is categorical that this is not a bug to
// work around: https://developer.apple.com/forums/thread/11964. Without this observer the plugin
// reports Connected for a connection that no longer exists — a zombie state the consumer has no
// way to detect.
//
// Why the advertiser and browser stop too, not just the session: Apple's prescribed handling is to
// disconnect on background and rebuild on return. In practice the advertiser/browser instances are
// often still live after resume and appear to carry on scanning, but that is observed behaviour,
// not a documented guarantee, and exactly the kind of unsupported reliance the same DTS engineer
// warns breaks on a future OS. Stopping them also keeps IsActive honest: while
// suspended nothing is scanning, so reporting true would be a second zombie state.
//
// Nothing restarts on foreground. Teardown is an explicit, observable transition, and restarting is
// the app's call, consistent with the plugin's "nothing starts on its own" contract. There is no
// MPC reconnect primitive in any case: recovery means re-advertising and re-inviting, and the retry
// policy is app-specific.
sealed class AppLifecycleObserver : IDisposable
{
    readonly ILogger _logger;
    readonly Action _onSuspended;

    NSObject? _backgroundRegistration;
    int _disposeGuard;

    internal AppLifecycleObserver(Action onSuspended, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(onSuspended);
        ArgumentNullException.ThrowIfNull(logger);

        _onSuspended = onSuspended;
        _logger = logger;

        _backgroundRegistration = NSNotificationCenter.DefaultCenter.AddObserver(
            UIApplication.DidEnterBackgroundNotification,
            OnDidEnterBackground);
    }

    void OnDidEnterBackground(NSNotification notification)
    {
        NearbyLog.BackgroundTeardown(_logger);
        _onSuspended();
    }

    // Nothing is waited for here. The teardown a signal starts runs on the session's own task set
    // and is joined by the session's teardown, which is the one place that already waits for
    // session work — so a backgrounding notification landing during disposal cannot outlive it.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeGuard, 1) != 0)
        {
            return;
        }

        if (_backgroundRegistration is not null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(_backgroundRegistration);
            _backgroundRegistration.Dispose();
            _backgroundRegistration = null;
        }
    }
}
