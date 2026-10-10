namespace Plugin.Maui.NearbyConnectivity.UiTests.Tests;

[Trait("Capability", "Permissions")]
[Collection(OneDeviceSuite.Name)]
public sealed class PermissionsTests(UiTestFixture fixture) : UiTest(fixture)
{
    [Fact]
    public void CheckStatus_ReportsAStatus()
    {
        // Arrange
        var permissions = Shell.GoToPermissions();

        // Act
        var status = permissions.CheckStatus();

        // Assert
        Assert.NotEqual("-", status);
        Assert.Equal("none", permissions.LastError);
    }

    // CI grants the permissions with `adb shell pm grant` before the app launches, so a request
    // must resolve without a dialog. A Granted result here is what makes the advertising and
    // discovery tests meaningful rather than uniformly blocked.
    [Fact]
    public void RequestPermission_ResolvesWithoutBlocking()
    {
        // Arrange
        var permissions = Shell.GoToPermissions();

        // Act
        var status = permissions.RequestPermission();

        // Assert
        Assert.NotEqual("-", status);
        Assert.Equal("none", permissions.LastError);
    }
}
