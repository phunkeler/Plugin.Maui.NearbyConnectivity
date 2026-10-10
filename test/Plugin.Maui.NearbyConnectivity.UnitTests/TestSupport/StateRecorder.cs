namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Records a value read inside every raise of a plain <see cref="EventHandler"/>, the way a
/// consumer's handler re-reads the property the event describes.
/// </summary>
/// <remarks>
/// Subscribes in the constructor, so a raise after the constructor returns is always captured.
/// Disposing unsubscribes.
/// </remarks>
sealed class StateRecorder<T> : IDisposable
{
    readonly List<T> _seen = [];
    readonly Func<T> _read;
    readonly Action<EventHandler> _unsubscribe;

    public StateRecorder(Action<EventHandler> subscribe, Action<EventHandler> unsubscribe, Func<T> read)
    {
        _read = read;
        _unsubscribe = unsubscribe;
        subscribe(OnRaised);
    }

    /// <summary>Every value read so far, one per raise, oldest first.</summary>
    public IReadOnlyList<T> Seen
    {
        get { lock (_seen) { return [.. _seen]; } }
    }

    /// <summary>Waits until at least <paramref name="count"/> raises have been recorded.</summary>
    public Task WaitForAsync(int count) => Wait.UntilAsync(() => Seen.Count >= count);

    public void Dispose() => _unsubscribe(OnRaised);

    void OnRaised(object? sender, EventArgs e)
    {
        lock (_seen)
        {
            _seen.Add(_read());
        }
    }
}

/// <summary>Builds the <see cref="StateRecorder{T}"/> for each watchable thing on the session.</summary>
static class Observe
{
    /// <summary>Records <see cref="INearbyActivity.IsActive"/> at each <see cref="INearbyActivity.Changed"/>.</summary>
    public static StateRecorder<bool> Activity(INearbyActivity activity)
        => new(h => activity.Changed += h, h => activity.Changed -= h, () => activity.IsActive);

    /// <summary>Records <see cref="INearbyActivity.LastStop"/> at each <see cref="INearbyActivity.Changed"/>.</summary>
    public static StateRecorder<NearbyActivityStop?> Stops(INearbyActivity activity)
        => new(h => activity.Changed += h, h => activity.Changed -= h, () => activity.LastStop);

    /// <summary>Records <see cref="INearbyConnectivity.Availability"/> at each <see cref="INearbyConnectivity.AvailabilityChanged"/>.</summary>
    public static StateRecorder<NearbyAvailability> Availability(INearbyConnectivity session)
        => new(h => session.AvailabilityChanged += h, h => session.AvailabilityChanged -= h, () => session.Availability);
}
