namespace Plugin.Maui.NearbyConnectivity.UiTests.Tests;

[Trait("Capability", "Connections")]
[Collection(OneDeviceSuite.Name)]
public sealed class ConnectionsTests(UiTestFixture fixture) : UiTest(fixture)
{
    [Fact]
    public void NoConnections_WithoutAPeer()
    {
        // Act
        var connections = Shell.GoToConnections();

        // Assert
        Assert.Equal(0, connections.ConnectionCount);
    }

    // StopAllAsync returns the session to its initial state and is documented to leave it usable:
    // advertising can be started again straight afterwards.
    [Fact]
    public void StopAll_ReturnsSessionToInitialState()
    {
        // Arrange
        var advertising = Shell.GoToAdvertising();
        advertising.StartAdvertising();
        Shell.GoToDiscovery().StartDiscovering();

        // Act
        Shell.GoToConnections().StopAll();

        // Assert
        Assert.False(Shell.GoToAdvertising().IsAdvertising);
        Assert.False(Shell.GoToDiscovery().IsDiscovering);
    }

    [Fact]
    public void StopAll_LeavesSessionUsable()
    {
        // Arrange
        Shell.GoToConnections().StopAll();

        // Act
        var advertising = Shell.GoToAdvertising();
        advertising.StartAdvertising();

        // Assert
        Assert.True(advertising.IsAdvertising);
    }

    [Fact]
    [Trait("Category", "RequiresRealPeer")]
    public void Connect_OpensAConnection()
    {
        // Arrange
        var deviceId = PeerDeviceId;

        // Act
        var discovery = Shell.GoToDiscovery();
        discovery.StartDiscovering();
        discovery.WaitForDevice(deviceId, TimeSpan.FromSeconds(60)).InvokeAction();

        // Assert
        Assert.True(Shell.GoToConnections().WaitForConnection(deviceId, TimeSpan.FromSeconds(60)).Exists);
    }

    [Fact]
    [Trait("Category", "RequiresRealPeer")]
    public void Disconnect_ClosesTheConnection()
    {
        // Arrange
        var deviceId = PeerDeviceId;

        var discovery = Shell.GoToDiscovery();
        discovery.StartDiscovering();
        discovery.WaitForDevice(deviceId, TimeSpan.FromSeconds(60)).InvokeAction();

        // Act
        var connections = Shell.GoToConnections();
        connections.WaitForConnection(deviceId, TimeSpan.FromSeconds(60)).InvokeAction();

        // Assert
        Assert.Equal(0, connections.ConnectionCount);
    }

    // The caller supplies the peer's device id: it is the remote endpoint's id, which only
    // exists once a second radio is advertising.
    static string PeerDeviceId =>
        Environment.GetEnvironmentVariable("PEER_DEVICE_ID")
        ?? throw new InvalidOperationException(
            "PEER_DEVICE_ID must be set for RequiresRealPeer tests.");
}
