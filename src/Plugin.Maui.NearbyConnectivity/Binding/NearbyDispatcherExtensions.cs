using System.Runtime.ExceptionServices;

namespace Plugin.Maui.NearbyConnectivity;

static class NearbyDispatcherExtensions
{
    internal static IDispatcher OrCurrentThread(this IDispatcher? dispatcher)
        => dispatcher
            ?? Dispatcher.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "No dispatcher was supplied and the current thread has none. Pass the user "
                + "interface thread's IDispatcher, or construct this on that thread. The net10.0 "
                + "target has no dispatcher at all, so it must always be passed.");

    /// <summary>
    /// Runs <paramref name="action"/> now when the caller is already on
    /// <paramref name="dispatcher"/>'s thread, and queues it otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IDispatcher.Dispatch(Action)"/> always queues, even from its own thread. A seed
    /// run from a constructor on the user interface thread would otherwise land a frame after the
    /// view first renders, so a bound list shows empty before it fills.
    /// </para>
    /// <para>
    /// An exception from <paramref name="action"/> never reaches the caller, which is typically a
    /// constructor or an event handler on the session's event loop. Run inline, it is handed back
    /// to the dispatcher and rethrown there with its original stack, exactly where a queued action
    /// would have thrown it.
    /// </para>
    /// </remarks>
    internal static void DispatchIfRequired(this IDispatcher dispatcher, Action action)
    {
        if (dispatcher.IsDispatchRequired)
        {
            dispatcher.Dispatch(action);
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            var captured = ExceptionDispatchInfo.Capture(ex);
            dispatcher.Dispatch(captured.Throw);
        }
    }
}
