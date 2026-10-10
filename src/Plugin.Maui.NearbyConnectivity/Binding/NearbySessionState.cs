namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// A bindable view of "is nearby working right now": whether this device is advertising and
/// discovering, whether anything blocks it, and why either activity last stopped.
/// </summary>
/// <remarks>
/// <para>
/// <b>Merges three events.</b> <see cref="INearbyConnectivity.AvailabilityChanged"/> carries radios
/// and permissions; <see cref="INearbyActivity.Changed"/> on the advertiser and the discoverer
/// carries a fault that ended a scan. A label bound to availability alone shows "Ready" while
/// discovery is dead, because a platform fault reaches only the activity.
/// </para>
/// <para>
/// <b>The same shape as <see cref="NearbyDeviceCollection"/>.</b> This is a bindable façade over the
/// session, constructed with the same dispatcher, seeded from the session's properties, and
/// disposed by whoever owns it. The service itself stays free of user-interface affinity.
/// </para>
/// <para>
/// Every property is raised on the dispatcher's thread and should be read there, exactly as with
/// the device collection. Constructed on that thread, every property already holds the session's
/// current value when the constructor returns.
/// </para>
/// </remarks>
/// <seealso href="https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/bind-ui.html#bind-a-status-line">Bind a status line</seealso>
/// <seealso cref="NearbyDeviceCollection"/>
/// <example>
/// <code language="csharp">
/// // Bind IsReady, Availability, IsAdvertising, IsDiscovering, DiscoveryStop.Error.
/// public NearbySessionState Status { get; } = new(nearby, dispatcher);
///
/// public void Dispose() =&gt; Status.Dispose();
/// </code>
/// </example>
public sealed class NearbySessionState : INotifyPropertyChanged, IDisposable
{
    readonly IDispatcher _dispatcher;
    readonly INearbyConnectivity _nearby;
    int _stopped;

    /// <summary>
    /// Initializes a new instance of the <see cref="NearbySessionState"/> class and begins watching.
    /// </summary>
    /// <param name="nearby">The session to watch.</param>
    /// <param name="dispatcher">
    /// Where property changes are raised. <see langword="null"/> uses the current thread's
    /// dispatcher, so a view model constructed on the UI thread can omit it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="nearby"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="dispatcher"/> is <see langword="null"/> and the current thread has none.
    /// </exception>
    /// <remarks>
    /// <see cref="IDispatcher.Dispatch(Action)"/>'s return value is ignored. A refused dispatch
    /// means the dispatcher is tearing down, when there is no bound view left to update.
    /// </remarks>
    public NearbySessionState(INearbyConnectivity nearby, IDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(nearby);

        _dispatcher = dispatcher.OrCurrentThread();

        _nearby = nearby;

        nearby.AvailabilityChanged += OnChanged;
        nearby.Advertiser.Changed += OnChanged;
        nearby.Discoverer.Changed += OnChanged;
        _dispatcher.DispatchIfRequired(Refresh);
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets whether this device is currently advertising.
    /// </summary>
    public bool IsAdvertising
    {
        get;
        private set => Set(ref field, value, nameof(IsAdvertising));
    }

    /// <summary>
    /// Gets whether this device is currently discovering.
    /// </summary>
    public bool IsDiscovering
    {
        get;
        private set => Set(ref field, value, nameof(IsDiscovering));
    }

    /// <summary>
    /// Gets what currently blocks nearby connectivity, or
    /// <see cref="NearbyAvailability.Available"/> when nothing does.
    /// </summary>
    /// <remarks>
    /// On Android this moves when the user turns a radio on or off, and is re-read when the app
    /// returns to the foreground, so a permission granted in Settings is reflected on return.
    /// </remarks>
    public NearbyAvailability Availability
    {
        get;
        private set
        {
            if (Set(ref field, value, nameof(Availability)))
            {
                Raise(nameof(IsReady));
            }
        }
    }

    /// <summary>
    /// Gets whether advertising and discovery can start right now.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when nothing blocks a start. <see cref="NearbyAvailability.WifiDisabled"/>
    /// counts as ready: it slows a connection rather than preventing one, and the start methods
    /// proceed on it.
    /// </value>
    public bool IsReady => Availability.PermitsStart();

    /// <summary>
    /// Gets how advertising last stopped, or <see langword="null"/> while it runs.
    /// </summary>
    /// <value>
    /// <see cref="INearbyActivity.LastStop"/> of <see cref="INearbyConnectivity.Advertiser"/>:
    /// <see langword="null"/> while advertising runs, and until it first stops.
    /// </value>
    /// <remarks>
    /// A <see cref="NearbyActivityStopReason.Faulted"/> stop is the one worth showing: the platform
    /// ended advertising that the application did not stop, <see cref="NearbyActivityStop.Error"/>
    /// says why, and <see cref="Availability"/> may still read as ready. Starting advertising again
    /// clears it.
    /// </remarks>
    public NearbyActivityStop? AdvertisingStop
    {
        get;
        private set => Set(ref field, value, nameof(AdvertisingStop));
    }

    /// <summary>
    /// Gets how discovery last stopped, or <see langword="null"/> while it runs.
    /// </summary>
    /// <value>
    /// <see cref="INearbyActivity.LastStop"/> of <see cref="INearbyConnectivity.Discoverer"/>:
    /// <see langword="null"/> while discovery runs, and until it first stops.
    /// </value>
    /// <remarks>
    /// A <see cref="NearbyActivityStopReason.Faulted"/> stop is the one worth showing: the platform
    /// ended a scan that the application did not stop, <see cref="NearbyActivityStop.Error"/> says
    /// why, and <see cref="Availability"/> may still read as ready. Starting discovery again clears
    /// it.
    /// </remarks>
    public NearbyActivityStop? DiscoveryStop
    {
        get;
        private set => Set(ref field, value, nameof(DiscoveryStop));
    }

    /// <summary>
    /// Stops following the session and unsubscribes from its events. Idempotent.
    /// </summary>
    /// <remarks>
    /// Whoever constructed this owns disposing it, exactly as with
    /// <see cref="NearbyDeviceCollection"/>.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _nearby.AvailabilityChanged -= OnChanged;
        _nearby.Advertiser.Changed -= OnChanged;
        _nearby.Discoverer.Changed -= OnChanged;
    }

    void OnChanged(object? sender, EventArgs e) => _dispatcher.DispatchIfRequired(Refresh);

    /// <summary>
    /// Re-reads every property from the session on the dispatcher's thread, unless this object has
    /// been disposed since the read was queued.
    /// </summary>
    void Refresh()
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        Availability = _nearby.Availability;
        IsAdvertising = _nearby.Advertiser.IsActive;
        AdvertisingStop = _nearby.Advertiser.LastStop;
        IsDiscovering = _nearby.Discoverer.IsActive;
        DiscoveryStop = _nearby.Discoverer.LastStop;
    }

    bool Set<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);

        return true;
    }

    void Raise(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
