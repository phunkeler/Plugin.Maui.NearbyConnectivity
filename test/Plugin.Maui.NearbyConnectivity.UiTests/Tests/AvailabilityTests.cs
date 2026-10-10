namespace Plugin.Maui.NearbyConnectivity.UiTests.Tests;

[Trait("Capability", "Availability")]
[Collection(OneDeviceSuite.Name)]
public sealed class AvailabilityTests(UiTestFixture fixture) : UiTest(fixture)
{
    // Documented never to throw: it reports a blocking condition instead, including
    // Unsupported on platforms the plugin does not cover.
    [Fact]
    public void CheckAvailability_ReportsWithoutThrowing()
    {
        // Arrange
        var availability = Shell.GoToAvailability();

        // Act
        var result = availability.CheckAvailability();

        // Assert
        Assert.NotEqual("-", result);
        Assert.Equal("none", availability.LastError);
    }

    [Fact]
    public void AvailabilityBadge_ReportsCurrentValueOnActivation()
    {
        // Act
        var availability = Shell.GoToAvailability();

        // Assert
        Assert.NotEqual("-", availability.Current_);
    }
}
