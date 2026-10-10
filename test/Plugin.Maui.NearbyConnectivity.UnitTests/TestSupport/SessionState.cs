namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Reads current device state through <see cref="INearbyConnectivity.Devices"/> — the same surface a consumer
/// uses.
/// </summary>
/// <remarks>
/// A <see cref="NearbyDevice"/> handed to the session is an immutable value, so the local variable a
/// test holds never updates. Every status assertion has to re-read through the session, and these
/// extensions make that read the obvious thing to write.
/// </remarks>
static class SessionState
{
    /// <summary>The session's current snapshot of a device, or <see langword="null"/> if unknown.</summary>
    public static NearbyDevice? Current(this INearbyConnectivity session, string deviceId)
        => session.Devices.FirstOrDefault(d => d.Id == deviceId);

    /// <summary>The device's current status, or <see langword="null"/> if the session does not know it.</summary>
    public static NearbyDeviceStatus? StatusOf(this INearbyConnectivity session, string deviceId)
        => session.Current(deviceId)?.Status;

    /// <summary>
    /// The open connection to a device, or <see langword="null"/> when there is none. The id-keyed
    /// route a consumer writes, since the facade has no connection lookup of its own.
    /// </summary>
    public static NearbyConnection? ConnectionOf(this INearbyConnectivity session, string deviceId)
        => session.Current(deviceId)?.Connection;
}
