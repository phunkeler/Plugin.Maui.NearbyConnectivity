using System.Collections;
using System.Collections.Specialized;

namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// The device set as a bindable, live collection of <see cref="NearbyDevice"/>, kept in sync by
/// handling <see cref="INearbyConnectivity.DeviceChanged"/> and applying each change on a
/// dispatcher's thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Optional.</b> <see cref="INearbyConnectivity"/> itself has no thread affinity. It hands out
/// immutable snapshots and raises its events on a thread-pool thread. This type exists only for
/// callers who want a collection they can bind to XAML, and it is the one place in the plugin that
/// assumes a UI thread exists.
/// </para>
/// <para>
/// Construct one per view that needs it, and dispose it when the view goes away. Disposal
/// unsubscribes from the session, which otherwise keeps this collection alive for as long as it
/// lives. Constructed on the dispatcher's thread, it already holds every device the session knows when the
/// constructor returns, so a bound list renders full rather than filling a frame later.
/// </para>
/// <para>
/// <b>Read this collection only on the dispatcher's thread.</b> Devices are added and removed on
/// that thread, so <see cref="Count"/>, the indexer, and <see cref="GetEnumerator"/> can observe a
/// torn collection from anywhere else. This is the same rule as any
/// <see cref="ObservableCollection{T}"/> bound to a user interface.
/// </para>
/// <para>
/// The collection is read-only to its callers. It mirrors what the session reports and cannot be
/// added to or removed from directly. It raises
/// <see cref="INotifyCollectionChanged.CollectionChanged"/>, so it binds straight to an
/// <c>ItemsSource</c>.
/// </para>
/// <para>
/// <b>A device is a value, so a change is a replacement.</b> Each row is an immutable snapshot, and
/// a changed device is assigned over the old one, raising
/// <see cref="NotifyCollectionChangedAction.Replace"/> so a bound row refreshes in place. When rows
/// need state a snapshot cannot carry, such as a command or a selection, project them yourself from
/// <see cref="INearbyConnectivity.DeviceChanged"/> into a collection you own.
/// </para>
/// <para>
/// <b>A failure is reported, not thrown.</b> If the <c>filter</c> throws, the collection keeps
/// the devices it has, stops updating, and sets <see cref="Exception"/> and
/// <see cref="IsFaulted"/> so a page can say so. The exception is also logged through the logger
/// the plugin's session was registered with; an <see cref="INearbyConnectivity"/> of your own,
/// such as a decorator, is not logged. Nothing is thrown on the dispatcher's thread, so a broken
/// filter never takes the application down.
/// </para>
/// </remarks>
/// <seealso href="https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/bind-ui.html">Bind to a MAUI page</seealso>
/// <example>
/// <code language="csharp">
/// // Constructed on the UI thread, so the dispatcher is found rather than passed.
/// public NearbyDeviceCollection Devices { get; } = new(nearby);
/// </code>
/// </example>
public sealed class NearbyDeviceCollection : IReadOnlyList<NearbyDevice>, INotifyCollectionChanged, INotifyPropertyChanged, IDisposable
{
    readonly ObservableCollection<NearbyDevice> _devices = [];
    readonly IDispatcher _dispatcher;
    readonly Func<NearbyDevice, bool> _filter;
    readonly ILogger _logger;
    readonly INearbyConnectivity _nearby;

    int _stopped;

    /// <summary>
    /// Initializes a new instance of the <see cref="NearbyDeviceCollection"/> class and begins
    /// watching the session's device set.
    /// </summary>
    /// <param name="nearby">The session to watch.</param>
    /// <param name="dispatcher">
    /// Where mutations and change events run. <see langword="null"/> uses the current thread's
    /// dispatcher, so a view model constructed on the UI thread can omit it.
    /// </param>
    /// <param name="filter">
    /// Which devices to include, or <see langword="null"/> for all of them. Re-evaluated on every
    /// change, so a device joins and leaves the collection as it starts and stops matching.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="nearby"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="dispatcher"/> is <see langword="null"/> and the current thread has none.
    /// </exception>
    public NearbyDeviceCollection(
        INearbyConnectivity nearby,
        IDispatcher? dispatcher = null,
        Func<NearbyDevice, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(nearby);

        _dispatcher = dispatcher.OrCurrentThread();

        _filter = filter ?? (static _ => true);
        _logger = NearbySession.LoggerFor(nearby);
        _nearby = nearby;

        ((INotifyCollectionChanged)_devices).CollectionChanged += (_, e) => CollectionChanged?.Invoke(this, e);
        ((INotifyPropertyChanged)_devices).PropertyChanged += (_, e) => PropertyChanged?.Invoke(this, e);

        nearby.DeviceChanged += OnDeviceChanged;
        _dispatcher.DispatchIfRequired(Seed);
    }

    /// <summary>
    /// Occurs when devices are added, replaced, or removed.
    /// </summary>
    /// <remarks>
    /// Raised from inside the dispatcher, so every handler, including a XAML binding, runs on the
    /// bound thread.
    /// </remarks>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>
    /// Raised when <see cref="Count"/> or the indexer changes.
    /// </summary>
    /// <remarks>
    /// Forwarded from the inner <see cref="ObservableCollection{T}"/>, so that
    /// <c>{Binding Devices.Count}</c> updates as devices come and go. Also raised for
    /// <see cref="Exception"/> and <see cref="IsFaulted"/>.
    /// </remarks>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets what stopped this collection following the session, or <see langword="null"/> while
    /// it still does.
    /// </summary>
    /// <value>The exception the <c>filter</c> threw, as it was raised.</value>
    /// <remarks>
    /// Once set, the collection keeps the devices it had and never changes again. Construct a new
    /// one to follow the session afresh. Raises <see cref="PropertyChanged"/>, as does
    /// <see cref="IsFaulted"/>.
    /// </remarks>
    public Exception? Exception
    {
        get;
        private set
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Exception)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFaulted)));
        }
    }

    /// <summary>
    /// Gets whether <see cref="Exception"/> is set: the collection has stopped following the session.
    /// </summary>
    public bool IsFaulted => Exception is not null;

    /// <summary>
    /// Gets the number of devices currently shown.
    /// </summary>
    /// <remarks>Read only on the dispatcher's thread.</remarks>
    public int Count => _devices.Count;

    /// <summary>
    /// Gets the device at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the device to get.</param>
    /// <returns>The device at <paramref name="index"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is outside the bounds of the collection.
    /// </exception>
    /// <remarks>
    /// Read only on the dispatcher's thread. Indexing from another thread can throw even for an
    /// index that was valid when it was chosen, because a removal can land in between.
    /// </remarks>
    public NearbyDevice this[int index] => _devices[index];

    /// <summary>
    /// Returns an enumerator that iterates through the devices currently shown.
    /// </summary>
    /// <returns>An enumerator over the collection.</returns>
    /// <remarks>Enumerate only on the dispatcher's thread.</remarks>
    public IEnumerator<NearbyDevice> GetEnumerator() => _devices.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_devices).GetEnumerator();

    /// <summary>
    /// Stops following the session and unsubscribes from <see cref="INearbyConnectivity.DeviceChanged"/>.
    /// </summary>
    /// <remarks>
    /// Idempotent. Calling this more than once performs no additional work.
    /// </remarks>
    public void Dispose()
    {
        TryStop();
    }

    /// <summary>
    /// Unsubscribes once, whether disposal or a failure asked first.
    /// </summary>
    bool TryStop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return false;
        }

        _nearby.DeviceChanged -= OnDeviceChanged;

        return true;
    }

    void OnDeviceChanged(object? sender, NearbyDeviceChange change)
        => _dispatcher.DispatchIfRequired(() => ApplyOrFail(change));

    /// <summary>
    /// Applies every device the session knows as an addition. Reads <see cref="INearbyConnectivity.Devices"/>
    /// on the dispatcher's thread, after subscribing, so no change can fall between the two.
    /// </summary>
    void Seed()
    {
        foreach (var device in _nearby.Devices)
        {
            ApplyOrFail(new NearbyDeviceChange(NearbyDeviceChangeAction.Added, device));
        }
    }

    /// <summary>
    /// Applies one change, or records the collection as faulted if the <c>filter</c> throws. Runs
    /// on the dispatcher's thread.
    /// </summary>
    /// <remarks>
    /// A device that no longer passes <c>filter</c> is removed whatever the change action says, so
    /// a status transition out of the filtered set reads as a removal rather than leaving a stale
    /// entry behind. Only the <c>filter</c> is guarded. An exception from a <see cref="CollectionChanged"/>
    /// handler belongs to that handler, so it surfaces on the dispatcher's thread as it would from
    /// any <see cref="ObservableCollection{T}"/>, rather than being blamed on this collection.
    /// </remarks>
    void ApplyOrFail(NearbyDeviceChange change)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        bool included;

        try
        {
            included = change.Action is not NearbyDeviceChangeAction.Removed && _filter(change.Device);
        }
        catch (Exception ex)
        {
            Fail(ex);
            return;
        }

        var device = change.Device;
        var index = IndexOf(device.Id);

        if (!included)
        {
            if (index >= 0)
            {
                _devices.RemoveAt(index);
            }

            return;
        }

        if (index < 0)
        {
            _devices.Add(device);
            return;
        }

        _devices[index] = device;
    }

    /// <summary>
    /// Stops following the session and reports why, on the dispatcher's thread.
    /// </summary>
    void Fail(Exception exception)
        => _dispatcher.DispatchIfRequired(() =>
        {
            if (!TryStop())
            {
                return;
            }

            NearbyLog.DeviceCollectionStopped(_logger, exception);
            Exception = exception;
        });

    int IndexOf(string deviceId)
    {
        for (var i = 0; i < _devices.Count; i++)
        {
            if (string.Equals(_devices[i].Id, deviceId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
