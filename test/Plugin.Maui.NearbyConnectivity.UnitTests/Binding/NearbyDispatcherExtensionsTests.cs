namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Tests for <see cref="NearbyDispatcherExtensions.DispatchIfRequired"/>.
/// </summary>
[Trait("Category", "Devices")]
public sealed class NearbyDispatcherExtensionsTests
{
    // The caller is a watch loop whose catch is for stream failures. An exception from the
    // action must not land there, where it would be misreported.
    [Fact]
    public void OnTheDispatchersThread_AThrowDoesNotReachTheCaller()
    {
        // Arrange
        var dispatcher = new QueuingDispatcher();

        // Act
        var thrown = Record.Exception(
            () => dispatcher.DispatchIfRequired(() => throw new InvalidOperationException("action broke")));

        // Assert
        Assert.Null(thrown);
    }
}
