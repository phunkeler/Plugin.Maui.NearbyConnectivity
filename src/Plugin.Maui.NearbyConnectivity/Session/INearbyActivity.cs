namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// One thing this device can run on the radio: advertising or discovery.
/// </summary>
/// <remarks>
/// <para>
/// Read <see cref="INearbyConnectivity.Advertiser"/> or <see cref="INearbyConnectivity.Discoverer"/>
/// for the instance. The two run independently: either, both, or neither can be active at a time.
/// </para>
/// <para>
/// Every member is callable from any thread. <see cref="Changed"/> is raised on the session's event
/// loop, described on <see cref="INearbyConnectivity.DeviceChanged"/>.
/// </para>
/// </remarks>
public interface INearbyActivity
{
    /// <summary>
    /// Gets whether the activity is running.
    /// </summary>
    /// <value>
    /// <see langword="true"/> from the start until it stops. A discovery restart driven by
    /// <see cref="NearbyOptions.DiscoveryRefreshInterval"/> or
    /// <see cref="INearbyDiscoverer.RefreshAsync(CancellationToken)"/> is internal, so this stays
    /// <see langword="true"/> across it.
    /// </value>
    bool IsActive { get; }

    /// <summary>
    /// Gets how the activity last stopped.
    /// </summary>
    /// <value>
    /// <see langword="null"/> while the activity runs and before it first stops. Starting clears it.
    /// </value>
    /// <remarks>
    /// A <see cref="NearbyActivityStopReason.Faulted"/> stop is the one worth showing: the platform
    /// ended an activity the application did not stop, <see cref="NearbyActivityStop.Error"/> says
    /// why, and <see cref="INearbyConnectivity.Availability"/> may still read as available.
    /// </remarks>
    NearbyActivityStop? LastStop { get; }

    /// <summary>
    /// Occurs after <see cref="IsActive"/> or <see cref="LastStop"/> changes.
    /// </summary>
    /// <remarks>
    /// Both properties already hold their new values when it is raised. Re-read them in the handler.
    /// Two changes in quick succession can be observed as one.
    /// </remarks>
    event EventHandler? Changed;

    /// <summary>
    /// Starts the activity.
    /// </summary>
    /// <param name="cancellationToken">A token to abandon the attempt.</param>
    /// <returns>A task that completes once the activity has started.</returns>
    /// <remarks>
    /// <para>
    /// <b>Idempotent.</b> A second call while the activity runs completes without doing anything.
    /// </para>
    /// <para>
    /// Checks <see cref="INearbyConnectivity.Availability"/> first and reports a blocked start as an
    /// exception rather than starting something that would silently never work. Permissions are the
    /// application's to request, because prompting is a user-interface decision and this type has no
    /// user-interface affinity. See <see cref="NearbyPermission"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="NearbyException">
    /// The platform returned an error for the start, or something reported by
    /// <see cref="INearbyConnectivity.Availability"/> blocks it.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled.
    /// </exception>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the activity. Established connections are unaffected.
    /// </summary>
    /// <returns>A task that completes once the activity has stopped.</returns>
    /// <remarks>
    /// Idempotent: stopping an activity that is not running does nothing. Stopping advertising
    /// declines the requests still outstanding and cancels a handshake an accepting consent policy
    /// started. Stopping discovery removes the devices that were only visible from
    /// <see cref="INearbyConnectivity.Devices"/>; connected devices remain.
    /// </remarks>
    /// <seealso cref="INearbyConnectivity.StopAllAsync"/>
    Task StopAsync();
}
