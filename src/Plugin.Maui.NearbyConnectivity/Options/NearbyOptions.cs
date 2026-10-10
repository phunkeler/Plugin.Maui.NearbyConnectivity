namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Configures nearby connectivity: display identity, service discovery, timeouts, and
/// platform-specific behavior.
/// </summary>
/// <remarks>
/// Configure this type in the delegate passed to
/// <see cref="NearbyConnectivityServiceCollectionExtensions.AddNearbyConnectivity"/>.
/// The plugin validates the options and captures an immutable copy before that call returns.
/// Changing a property afterward has no effect on the session.
/// </remarks>
/// <seealso href="https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/configuration.html">Options and limits</seealso>
/// <example>
/// <code language="csharp">
/// builder.Services.AddNearbyConnectivity(options =&gt;
/// {
///     options.ServiceId = "cardtable";                // required, identical on every device
///     options.OfferWindow = TimeSpan.FromSeconds(30);
/// });
/// </code>
/// </example>
public sealed class NearbyOptions
{
    /// <summary>
    /// Gets the Android-specific settings.
    /// </summary>
    /// <value>The Android options. Never <see langword="null"/>.</value>
    /// <remarks>
    /// Get-only, so the returned instance is configured in place and never replaced. See
    /// <see cref="NearbyAndroidOptions"/> for why it exists on every target framework.
    /// </remarks>
    public NearbyAndroidOptions Android { get; private set; } = new();

    /// <summary>
    /// Gets or sets what nearby devices see for this device before any connection exists.
    /// </summary>
    /// <value>
    /// The presence to advertise and to send with every connection request. The default is this
    /// device's own name with empty info. Never <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// Broadcast in the clear and unauthenticated — see <see cref="NearbyPresence"/>. Change it
    /// after startup with <see cref="INearbyConnectivity.SetPresence(NearbyPresence)"/>.
    /// </remarks>
    public NearbyPresence Presence { get; set; } = new();

    /// <summary>
    /// Gets or sets the service identifier that devices use to find one another.
    /// </summary>
    /// <value>
    /// The service identifier. This property has no default and must be set explicitly, on every
    /// platform: a derived default would make every application built on this plugin discoverable
    /// to every other one.
    /// </value>
    /// <remarks>
    /// <para>
    /// Only devices configured with the same service identifier discover one another.
    /// </para>
    /// <para>
    /// The identifier must be 1 to 15 characters of ASCII lowercase letters, digits, and hyphens,
    /// containing at least one letter, with no leading, trailing, or adjacent hyphens: for
    /// example, <c>"nearbychat"</c>. Registration fails with a descriptive error if the value does
    /// not meet those rules, on every platform, so a configuration that works in development
    /// cannot fail on a user's device.
    /// </para>
    /// <para>
    /// This is <b>not</b> a Bonjour <c>_name._tcp</c> service type. An iOS application also
    /// declares the longer Bonjour form in its <c>Info.plist</c> under <c>NSBonjourServices</c>;
    /// both are required and they take different forms. See
    /// <see
    /// href="https://developer.apple.com/documentation/BundleResources/Information-Property-List/NSBonjourServices">NSBonjourServices</see>.
    /// </para>
    /// </remarks>
    public string ServiceId { get; set; } = ServiceIdRules.Unset;

    /// <summary>
    /// Gets or sets how often discovery is restarted to drop devices that have gone away without the
    /// platform reporting it.
    /// </summary>
    /// <value>
    /// The interval between discovery passes, or <see langword="null"/> to never restart. The
    /// default is <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// After each restart, devices the new pass does not re-report are removed. A device that is
    /// connected, or mid-handshake, is never removed no matter how long it has been quiet.
    /// </para>
    /// <para>
    /// The default is <see langword="null"/>: discovery never restarts on its own. Set an interval
    /// to opt in, where lower values detect departures sooner at the cost of more frequent radio
    /// work.
    /// </para>
    /// </remarks>
    public TimeSpan? DiscoveryRefreshInterval { get; set; }

    /// <summary>
    /// Gets or sets how long a connection offer made by
    /// <see cref="NearbyDevice.ConnectAsync(CancellationToken)"/> lives, on both ends.
    /// </summary>
    /// <value>
    /// The offer's lifetime, measured from the call. The default is 2 minutes, and the longest
    /// permitted value is 30 minutes, the same bound a remote device applies to the window it is
    /// handed. There is no unbounded setting: <see cref="Timeout.InfiniteTimeSpan"/>, and anything
    /// above the bound, is rejected when the options are validated. Waiting past the point the
    /// remote device has already discarded the offer could only end in a wait nothing will answer.
    /// </value>
    /// <remarks>
    /// <para>
    /// The offer's one deadline, not a local patience setting, which is why this is a window rather
    /// than a timeout. The value is declared to the remote device with the request itself, so the
    /// remote prompt (<see cref="NearbyConnectionRequest.ExpiresAt"/>) and an accepted handshake
    /// expire at the same moment. A small value also gives the remote user a small decision window.
    /// </para>
    /// <para>
    /// A request answered by a consent policy rather than by a person is bounded separately: the
    /// handshake gets 30 seconds, because a policy that answers on the application's behalf removes
    /// the deciding from what the window covers. See
    /// <see cref="INearbyAdvertiser.SetConsent(NearbyConsentPolicy, Func{NearbyDevice, CancellationToken, ValueTask{bool?}})"/>.
    /// </para>
    /// <para>
    /// The window covers the request reaching the remote device, the remote user deciding, and the
    /// handshake completing. <see cref="NearbyConnectException"/> with
    /// <see cref="NearbyConnectFailure.TimedOut"/> is thrown when the window elapses, and the device
    /// returns to <see cref="NearbyDeviceStatus.Visible"/>.
    /// </para>
    /// <para>
    /// The plugin enforces this itself on both platforms, and there is no unbounded setting.
    /// Because the value is declared on the wire, a remote advertiser learns it. It carries no
    /// identity data, but a distinctive value is mildly fingerprintable.
    /// </para>
    /// </remarks>
    /// <seealso href="https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/configuration.html#limits">Options and limits</seealso>
    public TimeSpan OfferWindow { get; set; } = OfferWindowPolicy.s_default;

    /// <summary>
    /// Gets or sets how long a file transfer may report no progress before it is considered stalled
    /// and is abandoned.
    /// </summary>
    /// <value>
    /// The interval to wait between progress updates. The default is 10 seconds. Set this to
    /// <see langword="null"/> to disable the check.
    /// </value>
    /// <remarks>
    /// <para>
    /// When this interval elapses, the pending <c>SendFileAsync</c> call throws
    /// <see cref="NearbyTransferTimeoutException"/>.
    /// </para>
    /// <para>
    /// This value covers outbound transfers only. An inbound transfer that stops making progress
    /// has no time limit. To bound one, watch
    /// <see cref="NearbyConnection.InboundTransfersAsync(CancellationToken)"/> and call
    /// <see cref="NearbyConnection.CancelInboundTransferAsync(long)"/>.
    /// </para>
    /// <para>
    /// An inbound file is copied out of the platform's own
    /// storage before it reaches the application, and disposal waits for a copy that is still
    /// running. That wait has a fixed internal bound of a few seconds, so disposing a session
    /// during a large inbound transfer can take that much longer to return.
    /// </para>
    /// </remarks>
    public TimeSpan? FileTransferInactivityTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The default <see cref="NearbyPresence.DisplayName"/>: this device's name, cut to the shared
    /// cap.
    /// </summary>
    /// <remarks>
    /// Cut rather than taken raw, because the name is the user's to choose and a device named past
    /// the 63-byte cap would otherwise throw from a default the consumer never set.
    /// <see cref="DeviceInfo.Name"/> throws <see cref="NotImplementedException"/> off a device, so
    /// net10.0 falls back to the machine name, the same semantic value by another route.
    /// </remarks>
    internal static string DefaultDisplayName { get; } = GetDefaultDisplayName();

    static string GetDefaultDisplayName()
    {
        string name;

        try
        {
            name = DeviceInfo.Name;
        }
        catch (NotImplementedException)
        {
            name = Environment.MachineName;
        }

        return NearbyText.Truncate(name);
    }

    internal NearbyOptions Snapshot()
    {
        var copy = (NearbyOptions)MemberwiseClone();
        copy.Android = new NearbyAndroidOptions
        {
            Topology = Android.Topology,
            UseLowPower = Android.UseLowPower,
            ConnectionType = Android.ConnectionType,
        };

        return copy;
    }
}
