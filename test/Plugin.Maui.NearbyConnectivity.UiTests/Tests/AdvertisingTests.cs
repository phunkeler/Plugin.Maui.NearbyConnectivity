namespace Plugin.Maui.NearbyConnectivity.UiTests.Tests;

[Trait("Capability", "Advertising")]
[Collection(OneDeviceSuite.Name)]
public sealed class AdvertisingTests(UiTestFixture fixture) : UiTest(fixture)
{
    [Fact]
    public void StartAdvertising_TurnsAdvertisingOn()
    {
        // Arrange
        var advertising = Shell.GoToAdvertising();

        // Act
        advertising.StartAdvertising();

        // Assert
        Assert.True(advertising.IsAdvertising, $"state was '{advertising.State_}', error '{advertising.LastError}'");
    }

    [Fact]
    public void StopAdvertising_TurnsAdvertisingOff()
    {
        // Arrange
        var advertising = Shell.GoToAdvertising();
        advertising.StartAdvertising();

        // Act
        advertising.StopAdvertising();

        // Assert
        Assert.False(advertising.IsAdvertising);
    }

    // Documented as idempotent: a second start while already advertising completes without doing
    // anything, rather than throwing.
    [Fact]
    public void StartAdvertising_Twice_IsIdempotent()
    {
        // Arrange
        var advertising = Shell.GoToAdvertising();
        advertising.StartAdvertising();

        // Act
        advertising.StartAdvertising();

        // Assert
        Assert.True(advertising.IsAdvertising);
        Assert.Equal("none", advertising.LastError);
    }

    // Stopping something that is not running is documented to do nothing.
    [Fact]
    public void StopAdvertising_WhenIdle_IsIdempotent()
    {
        // Arrange
        var advertising = Shell.GoToAdvertising();

        // Act
        advertising.StopAdvertising();

        // Assert
        Assert.False(advertising.IsAdvertising);
        Assert.Equal("none", advertising.LastError);
    }

    // The reason is what a start/stop boolean pair cannot carry: a stop the application asked for
    // and one the platform forced are otherwise indistinguishable.
    [Fact]
    public void StopAdvertising_ReportsRequestedReason()
    {
        // Arrange
        var advertising = Shell.GoToAdvertising();
        advertising.StartAdvertising();

        // Act
        advertising.StopAdvertising();

        // Assert
        Assert.Equal("stopped:Requested", advertising.LastChange_);
    }

    [Fact]
    public void NoRequests_WithoutAPeer()
    {
        // Arrange
        var advertising = Shell.GoToAdvertising();

        // Act
        advertising.StartAdvertising();

        // Assert
        Assert.Equal(0, advertising.PendingRequestCount);
    }
}
