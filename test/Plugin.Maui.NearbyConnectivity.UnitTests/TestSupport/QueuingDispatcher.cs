using System.Collections.Concurrent;
using Microsoft.Maui.Dispatching;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// An <see cref="IDispatcher"/> that behaves like the real one seen from its own thread: dispatch
/// is not required, and <see cref="Dispatch"/> still defers rather than running inline.
/// </summary>
/// <remarks>
/// Dispatched work runs only when a test calls <see cref="RunQueued"/>, so anything a test observes
/// before then was applied without going through <see cref="Dispatch"/>. Setting
/// <see cref="IsDispatchRequired"/> makes every caller queue, as from a background thread.
/// </remarks>
sealed class QueuingDispatcher : IDispatcher
{
    readonly ConcurrentQueue<Action> _queued = new();

    public bool IsDispatchRequired { get; init; }

    public int Queued => _queued.Count;

    public bool Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _queued.Enqueue(action);
        return true;
    }

    public void RunQueued()
    {
        while (_queued.TryDequeue(out var action))
        {
            action();
        }
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
        => throw new NotSupportedException("No test needs a delayed dispatch.");

    public IDispatcherTimer CreateTimer()
        => throw new NotSupportedException("No test needs a dispatcher timer.");
}
