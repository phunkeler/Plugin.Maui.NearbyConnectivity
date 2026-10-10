namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Provides the entry point to nearby connectivity: advertises this device, discovers others,
/// establishes connections, and exposes every device's lifecycle through a single collection.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton, because there is one radio and one native session per device. The
/// dependency injection container owns the instance. Call <see cref="StopAllAsync"/> to return the
/// session to its initial state rather than disposing it.
/// </para>
/// <para>
/// Advertising and discovery are <see cref="Advertiser"/> and <see cref="Discoverer"/>. This type
/// holds what belongs to neither: availability, presence, the devices, the connections, and the
/// payloads they receive.
/// </para>
/// <para>
/// A thing you watch is a property plus an event: subscribe, then read the property. A thing you
/// consume, which needs one owner and backpressure, is a stream. Only payloads are consumed, through
/// <see cref="PayloadsAsync(CancellationToken)"/>.
/// </para>
/// <para>
/// Every member is callable from any thread, and nothing here has UI thread affinity. Events are
/// raised one at a time on a thread-pool thread, never the UI thread and never the platform SDK's
/// own callback thread. A caller that binds to a user interface marshals for itself, or constructs a
/// <see cref="NearbyDeviceCollection"/> or <see cref="NearbySessionState"/>. Unsubscribe when the
/// subscriber goes away: the session lives as long as the application.
/// </para>
/// <para>
/// Android and iOS only. On every other target framework,
/// <see cref="CheckAvailabilityAsync(CancellationToken)"/> reports
/// <see cref="NearbyAvailability.Unsupported"/> and <see cref="INearbyActivity.StartAsync(CancellationToken)"/>
/// throws <see cref="NearbyException"/>.
/// </para>
/// </remarks>
/// <seealso href="https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/getting-started.html">Get started</seealso>
/// <seealso cref="NearbyDevice"/>
/// <seealso cref="NearbyConnection"/>
public interface INearbyConnectivity
{
    /// <summary>
    /// Gets what currently prevents nearby connectivity, if anything.
    /// </summary>
    /// <value>
    /// <see cref="NearbyAvailability.Available"/> when nothing blocks it, or the worst blocking
    /// condition. Never throws.
    /// </value>
    /// <remarks>
    /// A cached snapshot, refreshed as the platform reports changes, which raises
    /// <see cref="AvailabilityChanged"/>. Call <see cref="CheckAvailabilityAsync(CancellationToken)"/>
    /// to force a fresh read.
    /// </remarks>
    NearbyAvailability Availability { get; }

    /// <summary>
    /// Reads current availability from the platform.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while checking.</param>
    /// <returns>
    /// A task whose result is <see cref="NearbyAvailability.Available"/> when advertising and
    /// discovery can be started, or what is blocking them.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Reports state; it does not change it, never prompts for permissions, and never enables a
    /// radio. Request permissions with <see cref="NearbyPermission"/>, and direct the user to
    /// system settings to enable a radio.
    /// </para>
    /// <para>
    /// The result is a snapshot. The user can disable a radio immediately afterwards, so an
    /// <see cref="NearbyAvailability.Available"/> result is not a guarantee that starting will
    /// succeed. <see cref="INearbyActivity.StartAsync(CancellationToken)"/> checks again for itself.
    /// </para>
    /// </remarks>
    Task<NearbyAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Occurs after <see cref="Availability"/> changes.
    /// </summary>
    /// <remarks>
    /// <see cref="Availability"/> already holds the new value when it is raised. Re-read it in the
    /// handler. Raised on the event loop described on <see cref="DeviceChanged"/>.
    /// </remarks>
    event EventHandler? AvailabilityChanged;

    /// <summary>
    /// Gets advertising: start and stop it, watch its state, and set how inbound requests are
    /// answered.
    /// </summary>
    /// <value>The same instance for the life of the session.</value>
    INearbyAdvertiser Advertiser { get; }

    /// <summary>
    /// Gets discovery: start and stop it, watch its state, and refresh it.
    /// </summary>
    /// <value>The same instance for the life of the session.</value>
    INearbyDiscoverer Discoverer { get; }

    /// <summary>
    /// What nearby devices see for this device before any connection exists.
    /// </summary>
    /// <value>
    /// The effective presence — what peers are seeing now. After
    /// <see cref="SetPresence(NearbyPresence)"/> is called while advertising, discovering,
    /// connected or connecting, this keeps reporting the old value until that change takes effect.
    /// </value>
    /// <remarks>
    /// Starts as <see cref="NearbyOptions.Presence"/>, which defaults to this device's own name
    /// with empty info.
    /// </remarks>
    /// <seealso cref="SetPresence(NearbyPresence)"/>
    NearbyPresence Presence { get; }

    /// <summary>
    /// Changes what nearby devices see for this device.
    /// </summary>
    /// <param name="presence">
    /// The new presence. Build it with <see langword="new"/> or copy the current one with
    /// <see langword="with"/>; <see cref="NearbyPresence"/> validates both members on construction,
    /// so an invalid one cannot reach this call.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="presence"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Takes effect the next time this device is idle</b> — nothing advertising, nothing
    /// discovering, no connection open and none being negotiated. Called while idle, it applies at
    /// once and the next start announces it. Called while anything is running, it is held: peers
    /// keep seeing the old presence, and the new one takes effect once everything has stopped.
    /// <see cref="Presence"/> reports the effective value throughout, so it is the one to bind to
    /// rather than the one you passed.
    /// </para>
    /// <para>
    /// The identity a peer connects to cannot change underneath it. iOS builds a local
    /// <c>MCPeerID</c> from the display name and cannot rename a live one at all; Android would
    /// otherwise advertise under one presence and send a connect request under another within a
    /// single run. The idle boundary is what makes both platforms behave the same way.
    /// </para>
    /// <para>
    /// To change it immediately, call <see cref="StopAllAsync"/> first, then this, then start
    /// again.
    /// </para>
    /// </remarks>
    /// <seealso cref="Presence"/>
    void SetPresence(NearbyPresence presence);

    /// <summary>
    /// Gets the devices known to this session, from first discovery until they are no longer
    /// visible.
    /// </summary>
    /// <value>
    /// The current set of devices. Enumerating it yields an immutable snapshot, so it is safe to
    /// read from any thread.
    /// </value>
    /// <remarks>
    /// A single collection spans the whole device lifecycle. Devices do not move between
    /// collections as they connect. <see cref="NearbyDevice.Status"/> reports where a device is in
    /// that lifecycle. Subscribe to <see cref="DeviceChanged"/> first, then read this property, so
    /// no change falls between the two.
    /// </remarks>
    IReadOnlyList<NearbyDevice> Devices { get; }

    /// <summary>
    /// Occurs after a device joins the set, changes, or leaves it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Subscribe first, then read <see cref="Devices"/>.</b> The event carries changes, not
    /// history. A change is keyed by <see cref="NearbyDevice.Id"/>, so one already reflected in what
    /// you read is harmless to apply again.
    /// </para>
    /// <para>
    /// <see cref="Devices"/> already holds the change when the event is raised. Handlers run one at
    /// a time on a single thread-pool thread, never the UI thread, the platform SDK's callback
    /// thread, or under a session lock. Every event this session raises shares that order. A slow
    /// handler delays the ones after it, and a handler that throws is logged while the rest still
    /// run.
    /// </para>
    /// <para>
    /// Most applications bind a <see cref="NearbyDeviceCollection"/> instead of handling this.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// nearby.DeviceChanged += (_, change) =&gt;
    /// {
    ///     if (change.Device.Status is NearbyDeviceStatus.RequestReceived)
    ///     {
    ///         // show the accept/decline row
    ///     }
    /// };
    /// </code>
    /// </example>
    event EventHandler<NearbyDeviceChange>? DeviceChanged;

    /// <summary>
    /// Gets the connections currently open.
    /// </summary>
    /// <value>
    /// The open connections. Enumerating yields an immutable snapshot, so it is safe to read from
    /// any thread.
    /// </value>
    /// <remarks>
    /// A snapshot for acting on every open connection at once, such as sending to everyone. To
    /// watch connections open and close, handle <see cref="DeviceChanged"/> and read
    /// <see cref="NearbyDevice.Connection"/>. Read this at the moment of use rather than
    /// caching it: a connection that drops simply stops appearing.
    /// </remarks>
    IReadOnlyList<NearbyConnection> Connections { get; }

    /// <summary>
    /// Reads the inbound payloads from every connection as one stream.
    /// </summary>
    /// <param name="cancellationToken">A token to end the enumeration.</param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> yielding each inbound payload exactly once, from every
    /// connection this session holds, paired with the device that sent it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The application-wide receive loop.</b> Connections already open are read from the moment
    /// the enumeration starts, and each new connection is picked up as it opens, so a caller that
    /// starts late misses neither a connection nor a payload buffered before it arrived.
    /// </para>
    /// <para>
    /// <b>Single-consumer.</b> A payload is handed to one enumeration and then gone, so two
    /// enumerations of this stream split the payloads between them. Read in one place and fan out
    /// an application-level message from inside the loop.
    /// </para>
    /// <para>
    /// The loop body provides backpressure per connection: the next payload from the same
    /// connection is not dequeued until the body returns. Connections are read concurrently, so a
    /// slow body for one peer never stalls another.
    /// </para>
    /// <para>
    /// <b>Canceling ends the wait, not the buffer.</b> When a connection closes, everything it had
    /// already buffered is yielded before it leaves the stream. Canceling the token stops the
    /// enumeration once what is buffered has been handed over, so no payload is discarded by
    /// stopping.
    /// </para>
    /// <para>
    /// Payloads arrive on a thread-pool thread; see the thread-safety notes on
    /// <see cref="INearbyConnectivity"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// await foreach (var (device, payload) in nearby.PayloadsAsync(cancellationToken))
    /// {
    ///     if (payload is NearbyBytesPayload bytes)
    ///     {
    ///         Handle(device, Encoding.UTF8.GetString(bytes.Data.Span));
    ///     }
    /// }
    /// </code>
    /// </example>
    IAsyncEnumerable<NearbyInboundPayload> PayloadsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops advertising and discovery and disconnects every established connection, returning the
    /// session to its initial state.
    /// </summary>
    /// <returns>A task that completes when the session is fully stopped.</returns>
    /// <remarks>
    /// <para>
    /// The session remains usable afterwards. Advertising or discovery can be started again at any
    /// time.
    /// </para>
    /// <para>
    /// Every connection is closed with <see cref="NearbyEndReason.SessionStopped"/>, an outbound
    /// handshake still in flight fails with <see cref="NearbyConnectFailure.Failed"/>, and each
    /// close inherits its own bounded wait for in-flight inbound work. With several Android
    /// connections open, this can take a few seconds per connection. A failure tearing one
    /// connection down is logged and does not stop the rest.
    /// </para>
    /// <para>
    /// Takes no <see cref="CancellationToken"/>: teardown is work the caller wants to finish, and
    /// the waits it contains are already bounded internally. Stop first, then cancel whatever
    /// tokens drive your own streams.
    /// </para>
    /// </remarks>
    Task StopAllAsync();
}
