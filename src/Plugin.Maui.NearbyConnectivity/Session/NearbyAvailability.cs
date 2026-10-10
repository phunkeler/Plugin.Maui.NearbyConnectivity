namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// What prevents nearby connectivity from working, worst first.
/// </summary>
/// <remarks>
/// <para>
/// Ordered rather than a bit field, and shaped like <c>NetworkAccess</c>: a device can fail more
/// than one condition at once, but a user can be told only one thing to fix first. Compare against
/// <see cref="Available"/> to test readiness.
/// </para>
/// <para>
/// Which values a platform can report differs. The documentation on each says so. Render guidance
/// from the value actually returned rather than assuming which ones a platform produces.
/// </para>
/// </remarks>
/// <seealso href="https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/troubleshooting.html">Troubleshooting</seealso>
/// <seealso cref="INearbyConnectivity.CheckAvailabilityAsync(CancellationToken)"/>
/// <example>
/// <code language="csharp">
/// var message = await nearby.CheckAvailabilityAsync(cancellationToken) switch
/// {
///     NearbyAvailability.Available =&gt; null,
///     NearbyAvailability.BluetoothDisabled =&gt; "Turn on Bluetooth.",
///     NearbyAvailability.WifiDisabled =&gt; "Turn on Wi-Fi.",
///     NearbyAvailability.MissingPermissions =&gt; "Grant nearby devices permission.",
///     _ =&gt; "Nearby connectivity is unavailable on this device.",
/// };
/// </code>
/// </example>
public enum NearbyAvailability
{
    /// <summary>
    /// Nothing blocks advertising or discovery.
    /// </summary>
    Available = 0,

    /// <summary>
    /// Wi-Fi is off. Connections still work over Bluetooth alone, at substantially lower
    /// throughput.
    /// </summary>
    /// <remarks>
    /// <b>A warning rather than a blocker</b>, and the lowest-severity value for that reason: the
    /// start methods proceed on it, because blocking it would block a working setup. Android only.
    /// Multipeer Connectivity gives no equivalent signal, and being Wi-Fi only on modern systems it
    /// has no slower fallback to report.
    /// </remarks>
    WifiDisabled = 1,

    /// <summary>
    /// Bluetooth is supported but currently turned off. Prompt the user to turn it on; an
    /// application cannot enable it directly.
    /// </summary>
    /// <remarks>
    /// <b>Android only</b>, where Nearby Connections uses Bluetooth for discovery. Multipeer
    /// Connectivity has not used Bluetooth since roughly 2016. Apple removed the implementation
    /// and its peer-to-peer support is Wi-Fi only, so iOS never reports this, and telling an iOS
    /// user to enable Bluetooth would be advice about a radio the platform does not use.
    /// </remarks>
    BluetoothDisabled = 2,

    /// <summary>
    /// One or more required permissions have not been granted.
    /// </summary>
    /// <remarks>
    /// Request them with <see cref="NearbyPermission"/>, which resolves the right set for the
    /// current platform and OS version. This reports only that a permission is missing. It does
    /// not distinguish one the user has not been asked for from one they permanently denied.
    /// </remarks>
    MissingPermissions = 3,

    /// <summary>
    /// Google Play services is missing, disabled, or requires an update.
    /// </summary>
    /// <remarks>
    /// Android only, and a hard failure there: Nearby Connections is provided by Google Play
    /// services.
    /// </remarks>
    PlayServicesUnavailable = 4,

    /// <summary>
    /// This application is not configured for nearby connectivity on the current platform.
    /// </summary>
    /// <remarks>
    /// <para>
    /// iOS only, and a build-time fault rather than something to handle at run time. Two causes,
    /// both reported with the detail written to the log:
    /// </para>
    /// <para>
    /// <see cref="NearbyOptions.ServiceId"/> is not valid. Starting with an invalid service
    /// identifier raises an exception inside Multipeer Connectivity that terminates the process and
    /// cannot be caught. Options validation normally rejects one during application startup; this
    /// covers callers who construct the session outside that pipeline.
    /// </para>
    /// <para>
    /// The application's <c>Info.plist</c> is incomplete: <c>NSLocalNetworkUsageDescription</c> is
    /// missing, or <c>NSBonjourServices</c> names the configured service identifier under no
    /// transport at all. Neither can be validated at startup, because both live in the
    /// application's bundle rather than in <see cref="NearbyOptions"/>. Without them advertising
    /// and discovery start and never find a peer, which is the silent failure this value exists to
    /// make visible. Declaring only one of <c>_&lt;ServiceId&gt;._tcp</c> and
    /// <c>_&lt;ServiceId&gt;._udp</c> is reported as a warning in the log rather than here, because
    /// Apple does not document whether both are required.
    /// </para>
    /// </remarks>
    InvalidConfiguration = 5,

    /// <summary>
    /// The current platform does not support nearby connectivity.
    /// </summary>
    /// <remarks>
    /// Reported on every target other than Android and iOS. Unlike every other value, this cannot
    /// be resolved at run time.
    /// </remarks>
    Unsupported = 6,
}
