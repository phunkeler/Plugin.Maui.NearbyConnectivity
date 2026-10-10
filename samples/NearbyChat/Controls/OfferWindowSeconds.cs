using NearbyChat.ViewModels;

namespace NearbyChat.Controls;

public sealed class OfferWindowSeconds : Label
{
    IDispatcherTimer? _timer;
    NearbyDeviceViewModel? _row;

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_row is not null)
        {
            _row.WindowChanged -= OnWindowChanged;
        }

        _row = BindingContext as NearbyDeviceViewModel;

        if (_row is not null)
        {
            _row.WindowChanged += OnWindowChanged;
        }

        Restart();
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);

        ArgumentNullException.ThrowIfNull(args);

        if (args.NewHandler is null)
        {
            Stop();
        }
    }

    void OnWindowChanged(object? sender, EventArgs e)
        => Restart();

    void Restart()
    {
        Stop();

        if (_row is not { HasWindow: true })
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;
        Sync();
        ScheduleNextTick();
    }

    void Sync()
    {
        var remaining = _row?.WindowRemaining ?? TimeSpan.Zero;

        Text = $"{(int)Math.Ceiling(remaining.TotalSeconds)}s";
    }

    void ScheduleNextTick()
    {
        if (_row is not { HasWindow: true } row)
        {
            return;
        }

        var untilBoundary = row.WindowRemaining.Ticks % TimeSpan.TicksPerSecond;

        if (untilBoundary <= 0)
        {
            untilBoundary = TimeSpan.TicksPerSecond;
        }

        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Interval = TimeSpan.FromTicks(untilBoundary);
        _timer.Start();
    }

    IDispatcherTimer CreateTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Tick += OnTick;
        return timer;
    }

    void OnTick(object? sender, EventArgs e)
    {
        if (_row is not { HasWindow: true } row || row.WindowRemaining <= TimeSpan.Zero)
        {
            Stop();
            return;
        }

        Sync();
        ScheduleNextTick();
    }

    void Stop()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }
}
