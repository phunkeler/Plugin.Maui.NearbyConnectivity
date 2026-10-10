namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Records every <see cref="INearbyConnectivity.DeviceChanged"/> raised after construction, so a
/// test can assert on the sequence of transitions a device went through.
/// </summary>
/// <remarks>
/// Subscribes in the constructor, so a change raised after the constructor returns is always
/// captured. Disposing unsubscribes.
/// </remarks>
sealed class ChangeRecorder : IAsyncDisposable
{
    readonly List<NearbyDeviceChange> _changes = [];
    readonly INearbyConnectivity? _session;

    /// <summary>Records the changes a session raises from this point on.</summary>
    public ChangeRecorder(INearbyConnectivity session)
    {
        _session = session;
        session.DeviceChanged += OnChanged;
    }

    /// <summary>Records what a test hands to <see cref="Record"/>, such as a registry's callback.</summary>
    public ChangeRecorder()
    {
    }

    /// <summary>Every change recorded so far, oldest first.</summary>
    public IReadOnlyList<NearbyDeviceChange> Changes
    {
        get { lock (_changes) { return [.. _changes]; } }
    }

    /// <summary>Records one change.</summary>
    public void Record(NearbyDeviceChange change)
    {
        lock (_changes)
        {
            _changes.Add(change);
        }
    }

    /// <summary>Every change recorded for one device, oldest first.</summary>
    public IReadOnlyList<NearbyDeviceChange> For(string deviceId)
        => [.. Changes.Where(c => c.Device.Id == deviceId)];

    /// <summary>The statuses a device has been reported in, oldest first.</summary>
    public IReadOnlyList<NearbyDeviceStatus> StatusesFor(string deviceId)
        => [.. For(deviceId).Select(c => c.Device.Status)];

    /// <summary>
    /// Waits until at least <paramref name="count"/> changes have been recorded for a device. The
    /// session raises its events on its own loop, so an assertion made immediately after an
    /// operation can otherwise race the recording.
    /// </summary>
    public Task WaitForAsync(string deviceId, int count)
        => Wait.UntilAsync(() => For(deviceId).Count >= count);

    /// <summary>Waits until at least <paramref name="count"/> changes have been recorded in total.</summary>
    public Task WaitForAsync(int count)
        => Wait.UntilAsync(() => Changes.Count >= count);

    public ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            _session.DeviceChanged -= OnChanged;
        }

        return ValueTask.CompletedTask;
    }

    void OnChanged(object? sender, NearbyDeviceChange change) => Record(change);
}
