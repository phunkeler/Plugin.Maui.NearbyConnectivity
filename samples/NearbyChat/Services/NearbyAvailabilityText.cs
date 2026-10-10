using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.Services;

public static class NearbyAvailabilityText
{
    public static string Describe(NearbyAvailability availability) => availability switch
    {
        NearbyAvailability.Available => string.Empty,
        NearbyAvailability.Unsupported => "This device does not support nearby connections.",
        NearbyAvailability.InvalidConfiguration => "This app's nearby service identifier is not valid. This is a bug in the app.",
        NearbyAvailability.PlayServicesUnavailable => "Please update Google Play services, then try again.",
        NearbyAvailability.BluetoothDisabled => "Please turn Bluetooth on, then try again.",
        NearbyAvailability.WifiDisabled => "Turn Wi-Fi on for faster transfers.",
        NearbyAvailability.MissingPermissions => "NearbyChat needs permission to find and connect to nearby devices.",
        _ => "Nearby is unavailable for an unknown reason.",
    };
}
