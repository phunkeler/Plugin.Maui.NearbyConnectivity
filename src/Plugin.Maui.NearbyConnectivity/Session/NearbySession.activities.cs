namespace Plugin.Maui.NearbyConnectivity;

sealed partial class NearbySession
{
    public INearbyAdvertiser Advertiser => _advertiser;

    public INearbyDiscoverer Discoverer => _discoverer;

    abstract class SessionActivity : INearbyActivity
    {
        CancellationTokenSource? _cts;
        Task? _task;

        protected SessionActivity(NearbySession session, DiscoveryRefresher? refresher = null)
        {
            Session = session;
            Refresher = refresher;
            Flag = new ActivityFlag(() => session._events.Raise(Changed, this));
        }

        public event EventHandler? Changed;

        public bool IsActive => Flag.Value;

        public NearbyActivityStop? LastStop => Flag.LastStop;

        public ActivityFlag Flag { get; }

        public DiscoveryRefresher? Refresher { get; }

        protected NearbySession Session { get; }

        public Task StartAsync(CancellationToken cancellationToken = default)
            => Session.StartAsync(this, cancellationToken);

        public Task StopAsync() => Session.StopOneAsync(this);

        public abstract Task ClearAfterStopAsync();

        protected abstract Task RunAsync(TaskCompletionSource started, CancellationToken cancellationToken);

        public async Task StartConfirmedAsync(CancellationToken cancellationToken)
        {
            var cts = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            _cts = cts;
            Flag.Set(true);
            _task = RunAsync(started, cts.Token);

            try
            {
                await started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await StopPumpAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task StopPumpAsync(
            bool clearFlag = true,
            NearbyActivityStopReason reason = NearbyActivityStopReason.Requested)
        {
            var cts = _cts;
            var task = _task;

            _cts = null;
            _task = null;

            if (cts is not null)
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }

            if (task is not null)
            {
                await task.ConfigureAwait(false);
            }

            cts?.Dispose();

            if (clearFlag)
            {
                Flag.Set(false, reason);
            }
        }

        protected async Task PumpAsync<T>(
            IAsyncEnumerable<T> stream,
            Action<T> onItem,
            Action<Exception> logFailed,
            TaskCompletionSource started,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var item in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    onItem(item);
                }

                started.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                started.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                Flag.Set(false, NearbyActivityStopReason.Faulted, ex);

                if (!started.TrySetException(ex))
                {
                    logFailed(ex);
                }
            }
        }
    }

    sealed class SessionAdvertiser(NearbySession session) : SessionActivity(session), INearbyAdvertiser
    {
        public void SetConsent(NearbyConsentPolicy policy, Func<NearbyDevice, CancellationToken, ValueTask<bool?>>? decide = null)
            => Session._consent = new Consent(policy, decide);

        public override Task ClearAfterStopAsync() => Session.DeclineOutstandingRequestsAsync();

        protected override Task RunAsync(TaskCompletionSource started, CancellationToken cancellationToken)
            => PumpAsync(
                Session._nearbyTransport.AdvertiseAsync(started, cancellationToken),
                request => Session._tasks.Add(Session.ResolveConsentAsync(request, cancellationToken)),
                ex => NearbyLog.AdvertisingStopped(Session._logger, ex),
                started,
                cancellationToken);
    }

    sealed class SessionDiscoverer(NearbySession session) : SessionActivity(session, session._refresher), INearbyDiscoverer
    {
        public Task RefreshAsync(CancellationToken cancellationToken = default)
            => Session.RefreshDiscoveryAsync(cancellationToken);

        public override Task ClearAfterStopAsync()
        {
            Session._deviceRegistry.RemoveWhere(static d => d.Status is NearbyDeviceStatus.Visible);

            return Task.CompletedTask;
        }

        protected override Task RunAsync(TaskCompletionSource started, CancellationToken cancellationToken)
            => PumpAsync(
                Session._nearbyTransport.DiscoverAsync(started, cancellationToken),
                Session.OnDeviceEvent,
                ex => NearbyLog.DiscoveryStopped(Session._logger, ex),
                started,
                cancellationToken);
    }
}
