namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Identifies what kind of change a <see cref="NearbyDeviceChange"/> describes.
/// </summary>
public enum NearbyDeviceChangeAction
{
    /// <summary>
    /// The device was not previously known and has just become known.
    /// </summary>
    Added,

    /// <summary>
    /// An already-known device changed: its <see cref="NearbyDevice.Status"/> or
    /// <see cref="NearbyDevice.Presence"/> differs from before.
    /// </summary>
    /// <remarks>
    /// Every connection lifecycle transition arrives as an update: receiving a request, connecting,
    /// dropping. A device is never removed and re-added to reflect its own state changing.
    /// </remarks>
    Updated,

    /// <summary>
    /// The device is no longer known.
    /// </summary>
    Removed,
}
