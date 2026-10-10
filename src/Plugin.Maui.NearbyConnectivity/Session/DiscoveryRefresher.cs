namespace Plugin.Maui.NearbyConnectivity;

// On each interval, asks the session to restart the discover pump, waits out the settle window,
// then evicts the devices the new pass did not re-report. Owns the interval, the settle window and
// eviction.
sealed class DiscoveryRefresher(
    TimeSpan? interval,
    TimeProvider timeProvider,
    DeviceRegistry registry,
    Func<CancellationToken, Task<bool>> refreshAsync,
    Action<Exception> onException,
    TimeSpan? settleWindow = null)
{
    static readonly TimeSpan s_defaultSettleWindow = TimeSpan.FromSeconds(2);
    readonly TimeSpan _settleWindow = settleWindow ?? s_defaultSettleWindow;
    Run? _run;

    internal async Task SettleAndEvictAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(_settleWindow, timeProvider, cancellationToken).ConfigureAwait(false);

        registry.EvictUnconfirmed();
    }

    internal void Start()
    {
        if (interval is not { } configured)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        var run = new Run
        {
            Cts = cts,
            Task = RunAsync(configured, cts.Token)
        };

        if (Interlocked.CompareExchange(ref _run, run, null) is not null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    internal Task CancelAsync() => Volatile.Read(ref _run)?.Cts.CancelAsync() ?? Task.CompletedTask;

    internal async Task DrainAsync()
    {
        if (Interlocked.Exchange(ref _run, null) is not { } run)
        {
            return;
        }

        await run.Task.ConfigureAwait(false);

        run.Cts.Dispose();
    }

    async Task RunAsync(TimeSpan configured, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(configured, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await refreshAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                await SettleAndEvictAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Discovery stopped, or the session was disposed.
        }
        catch (Exception ex)
        {
            onException(ex);
        }
    }

    sealed class Run
    {
        internal required CancellationTokenSource Cts { get; init; }
        internal required Task Task { get; init; }
    }
}
