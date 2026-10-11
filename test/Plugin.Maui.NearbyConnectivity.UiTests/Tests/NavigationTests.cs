namespace Plugin.Maui.NearbyConnectivity.UiTests.Tests;

// Test order is random, so these pin the overflow sequences that otherwise surface only in
// whichever capability test happens to run after them. Connect and Payloads sit behind "More".
[Trait("Capability", "Navigation")]
[Collection(OneDeviceSuite.Name)]
public sealed class NavigationTests(UiTestFixture fixture) : UiTest(fixture)
{
    [Fact]
    public void GoTo_SameOverflowPage_AfterABarTab()
    {
        // Arrange
        Shell.GoToConnections();
        Shell.GoToDiscovery();

        // Act
        var connections = Shell.GoToConnections();

        // Assert
        Assert.Equal("none", connections.LastError);
    }

    [Fact]
    public void GoTo_OtherOverflowPage_AfterABarTab()
    {
        // Arrange
        Shell.GoToConnections();
        Shell.GoToDiscovery();

        // Act
        var payloads = Shell.GoToPayloads();

        // Assert
        Assert.Equal("none", payloads.LastError);
    }
}
