namespace Plugin.Maui.NearbyConnectivity;

sealed class SessionTaskSet(TimeProvider timeProvider)
{
    readonly ConcurrentDictionary<Task, byte> _live = new();

    internal void Add(Task task)
    {
        if (task.IsCompleted)
        {
            return;
        }

        // Track the observed wrapper, not the caller's task: the wrapper cannot fault, so
        // Task.WhenAll over the set cannot fault either, and an abandoned join leaves nothing
        // unobserved behind it.
        var tracked = ObserveAsync(task);

        _live[tracked] = 0;

        // Prune from a continuation, not from inside ObserveAsync: a task is not yet marked
        // completed while its own body runs, so self-removal there never fires.
        _ = tracked.ContinueWith(
            static (completed, state) => ((ConcurrentDictionary<Task, byte>?)state)?.TryRemove(completed, out _),
            _live,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // Loops over tasks added mid-join. False when the bound elapsed with tasks still live — the
    // caller logs that.
    internal async Task<bool> JoinAsync(TimeSpan bound)
    {
        if (_live.IsEmpty)
        {
            return true;
        }

        using var deadline = new CancellationTokenSource(bound, timeProvider);

        while (!_live.IsEmpty)
        {
            try
            {
                await Task.WhenAll(_live.Keys).WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                return false;
            }
        }

        return true;
    }

    static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // The member owns reporting its own failure.
        }
    }
}
