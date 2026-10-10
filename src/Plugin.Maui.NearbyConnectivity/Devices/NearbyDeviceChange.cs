namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// A single change to the device set, raised by
/// <see cref="INearbyConnectivity.DeviceChanged"/>.
/// </summary>
/// <param name="Action">The kind of change this is.</param>
/// <param name="Device">
/// The device as it is after the change. This instance never updates itself: a later change to the
/// same device arrives as a new <see cref="NearbyDeviceChange"/> carrying a new snapshot.
/// </param>
/// <remarks>
/// A delta, not a whole-list snapshot. A caller applies the one change described here instead of
/// re-diffing the entire collection on every transition.
/// </remarks>
public sealed record NearbyDeviceChange(NearbyDeviceChangeAction Action, NearbyDevice Device)
{
    /// <summary>
    /// Gets why the device left the set or its connection ended, when the plugin observed a reason
    /// locally.
    /// </summary>
    /// <value>
    /// The locally-observed reason, or <see langword="null"/> for a change with no ending in it: a
    /// device found, a request received, a handshake that succeeded.
    /// </value>
    public NearbyEndReason? Reason { get; init; }
}
