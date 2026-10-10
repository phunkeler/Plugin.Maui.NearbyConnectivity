namespace Plugin.Maui.NearbyConnectivity;

public static partial class NearbyConnectivityServiceCollectionExtensions
{
    private static partial IPlatformAdapter CreateAdapter(NearbyTransport transport)
        => new IosAdapter(transport);
}
