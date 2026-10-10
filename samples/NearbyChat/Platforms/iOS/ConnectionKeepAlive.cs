namespace NearbyChat.Services;

/// <summary>
/// The iOS side of the Android ongoing connection notification. It does nothing: iOS has no
/// ongoing notification for an app that holds a connection, and the system suspends a
/// backgrounded app regardless of one.
/// </summary>
public static class ConnectionKeepAlive
{
    /// <summary>
    /// Does nothing on iOS.
    /// </summary>
    /// <param name="connectionCount">Ignored.</param>
    public static void Update(int connectionCount)
    {
    }
}
