[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Plugin.Maui.NearbyConnectivity.DeviceTests;

public sealed class AssemblyMarker;

[CollectionDefinition(Name)]
public sealed class StagingTests
{
    /// <summary>
    /// The collection name test classes reference.
    /// </summary>
    public const string Name = "Staging";
}
