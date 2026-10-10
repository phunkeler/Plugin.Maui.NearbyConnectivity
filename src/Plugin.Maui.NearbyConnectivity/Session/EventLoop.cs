using System.Threading.Channels;

namespace Plugin.Maui.NearbyConnectivity;

sealed class EventLoop
{
    internal const int BacklogWarningThreshold = 64;

    readonly Channel<Pending> _queue = Channel.CreateUnbounded<Pending>(
        new UnboundedChannelOptions { SingleReader = true });

    readonly ILogger _logger;
    int _backlog;

    internal EventLoop(ILogger logger)
    {
        _logger = logger;
        _ = Task.Run(RunAsync);
    }

    internal void Raise<T>(EventHandler<T>? handlers, object sender, T args)
        => Enqueue(handlers, handler => ((EventHandler<T>)handler)(sender, args));

    internal void Raise(EventHandler? handlers, object sender)
        => Enqueue(handlers, handler => ((EventHandler)handler)(sender, EventArgs.Empty));

    internal void Complete() => _queue.Writer.TryComplete();

    void Enqueue(Delegate? handlers, Action<Delegate> invoke)
    {
        if (handlers is null)
        {
            return;
        }

        if (Interlocked.Increment(ref _backlog) == BacklogWarningThreshold + 1)
        {
            NearbyLog.EventBacklog(_logger, BacklogWarningThreshold);
        }

        _queue.Writer.TryWrite(new Pending(handlers.GetInvocationList(), invoke));
    }

    async Task RunAsync()
    {
        await foreach (var pending in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            Interlocked.Decrement(ref _backlog);

            foreach (var handler in pending.Handlers)
            {
                try
                {
                    pending.Invoke(handler);
                }
                catch (Exception ex)
                {
                    NearbyLog.EventHandlerFailed(_logger, ex);
                }
            }
        }
    }

    readonly record struct Pending(Delegate[] Handlers, Action<Delegate> Invoke);
}
