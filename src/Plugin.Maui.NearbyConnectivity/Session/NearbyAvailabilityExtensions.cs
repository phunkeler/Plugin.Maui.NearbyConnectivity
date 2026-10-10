namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// The start policy for <see cref="NearbyAvailability"/>, stated once.
/// </summary>
static class NearbyAvailabilityExtensions
{
    internal static bool PermitsStart(this NearbyAvailability availability)
        => availability is NearbyAvailability.Available or NearbyAvailability.WifiDisabled;
}
