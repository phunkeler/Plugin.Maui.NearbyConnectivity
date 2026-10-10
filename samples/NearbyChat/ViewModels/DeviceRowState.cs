namespace NearbyChat.ViewModels;

/// <summary>
/// The one state a device row is showing, with precedence resolved here rather than by trigger
/// order in XAML.
/// </summary>
public enum DeviceRowState
{
    Idle,
    Offering,
    Connecting,
    Expired,
    Connected,
    Disconnecting,
    Disconnected,
}
