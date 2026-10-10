namespace Plugin.Maui.NearbyConnectivity.UiTests.Tests;

[Trait("Capability", "Discovery")]
[Collection(OneDeviceSuite.Name)]
public sealed class DiscoveryTests(UiTestFixture fixture) : UiTest(fixture)
{
    [Fact]
    public void StartDiscovering_TurnsDiscoveryOn()
    {
        // Arrange
        var discovery = Shell.GoToDiscovery();

        // Act
        discovery.StartDiscovering();

        // Assert
        Assert.True(discovery.IsDiscovering, $"state was '{discovery.State_}', error '{discovery.LastError}'");
    }

    [Fact]
    public void StopDiscovering_TurnsDiscoveryOff()
    {
        // Arrange
        var discovery = Shell.GoToDiscovery();
        discovery.StartDiscovering();

        // Act
        discovery.StopDiscovering();

        // Assert
        Assert.False(discovery.IsDiscovering);
    }

    [Fact]
    public void StartDiscovering_Twice_IsIdempotent()
    {
        // Arrange
        var discovery = Shell.GoToDiscovery();
        discovery.StartDiscovering();

        // Act
        discovery.StartDiscovering();

        // Assert
        Assert.True(discovery.IsDiscovering);
        Assert.Equal("none", discovery.LastError);
    }

    [Fact]
    public void StopDiscovering_ReportsRequestedReason()
    {
        // Arrange
        var discovery = Shell.GoToDiscovery();
        discovery.StartDiscovering();

        // Act
        discovery.StopDiscovering();

        // Assert
        Assert.Equal("stopped:Requested", discovery.LastChange_);
    }

    // Documented as a no-op when not discovering — the pull-to-refresh operation must not throw
    // just because nothing is running.
    [Fact]
    public void Refresh_WhenIdle_DoesNothing()
    {
        // Arrange
        var discovery = Shell.GoToDiscovery();

        // Act
        discovery.Refresh();

        // Assert
        Assert.Equal("none", discovery.LastError);
    }

    [Fact]
    public void NoDevices_WithoutAPeer()
    {
        // Arrange
        var discovery = Shell.GoToDiscovery();

        // Act
        discovery.StartDiscovering();

        // Assert
        Assert.Equal(0, discovery.DiscoveredCount);
    }

    // Advertising and discovery are independent activities on one session; running both is the
    // normal state for a peer-to-peer app and must not disturb either one's state.
    [Fact]
    public void DiscoveryAndAdvertising_RunIndependently()
    {
        // Arrange
        var discovery = Shell.GoToDiscovery();
        discovery.StartDiscovering();

        // Act
        var advertising = Shell.GoToAdvertising();
        advertising.StartAdvertising();

        // Assert
        Assert.True(advertising.IsAdvertising);
        Assert.True(Shell.GoToDiscovery().IsDiscovering);
    }
}
