namespace Plugin.Maui.NearbyConnectivity;

// The mechanism behind "drain, then release" for per-peer work. The key is a peer id. Items for
// one peer run in the order they arrive; items for different peers run independently. A drain
// completes the work already queued — anything enqueued after the drain starts is not awaited.
//
// Enqueue work that is finite, keyed by peer, and started from a platform callback that cannot
// await it. Inbound payload completion on Android is the primary case.
//
// Three things must NOT go on this queue:
//   - Waits, as opposed to work. A timer, a disconnect watcher, or an await on a
//     TaskCompletionSource completes only when something outside the queue happens, and blocks
//     every later item for that peer until it does.
//   - Anything that drains this queue. A release or a disposal waits for the queue, so an enqueued
//     wait waits for itself and never completes.
//   - Work a caller already awaits. A public async operation has an owner and a deadline of its
//     own; queueing it here puts it behind work that has neither.
//
// onError receives every failure and the queue never faults a key, so one failed item cannot stop
// the items behind it.
sealed class KeyedSerialQueue(Action<string, Exception> onError, TimeProvider timeProvider)
{
    readonly Lock _gate = new();
    readonly Dictionary<string, Task> _tails = new(StringComparer.Ordinal);

    // The work always starts on a thread pool thread, never the calling thread, so a platform
    // callback that enqueues work returns without waiting for it. This matters on Android, where
    // the callback thread may be the main thread and the work reads files. The returned task never
    // faults.
    internal Task Enqueue(string key, Func<Task> work)
    {
        lock (_gate)
        {
            var previous = _tails.GetValueOrDefault(key, Task.CompletedTask);
            var queued = Task.Run(() => RunAsync(key, previous, work));
            _tails[key] = queued;

            // Prune from a continuation, not from inside RunAsync: a task is not yet marked
            // completed while its own body runs, so self-removal there never fires.
            _ = queued.ContinueWith(
                completed => Prune(key, completed),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return queued;
        }
    }

    // The wait is always bounded, so a stuck item cannot turn a release or a disposal into a hang.
    // False when the bound elapsed first; the caller logs that.
    internal Task<bool> DrainAsync(string key, TimeSpan bound)
    {
        Task? tail;

        lock (_gate)
        {
            tail = _tails.GetValueOrDefault(key);
        }

        return tail is null
            ? Task.FromResult(true)
            : WaitAsync(tail, bound, timeProvider);
    }

    // For disposal. Returns the number of keys still holding work — a snapshot taken after the
    // wait, since a key prunes itself asynchronously, so it is a diagnostic, not a value to branch
    // on.
    internal async Task<int> DrainAllAsync(TimeSpan bound)
    {
        Task[] tails;

        lock (_gate)
        {
            tails = [.. _tails.Values];
        }

        if (tails.Length == 0
            || await WaitAsync(Task.WhenAll(tails), bound, timeProvider).ConfigureAwait(false))
        {
            return 0;
        }

        lock (_gate)
        {
            return _tails.Count;
        }
    }

    async Task RunAsync(string key, Task previous, Func<Task> work)
    {
        await previous.ConfigureAwait(false);

        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            onError(key, ex);
        }
    }

    static async Task<bool> WaitAsync(Task tail, TimeSpan bound, TimeProvider timeProvider)
    {
        try
        {
            await tail.WaitAsync(bound, timeProvider).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    void Prune(string key, Task completed)
    {
        lock (_gate)
        {
            if (_tails.TryGetValue(key, out var current)
                && ReferenceEquals(current, completed))
            {
                _tails.Remove(key);
            }
        }
    }
}
