namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// The Info.plist preflight, against this runner's real bundle. It is what turns "advertising
/// starts and no peer is ever found" into a reported <see cref="NearbyAvailability"/> value, so the
/// contract pinned here is that a correctly configured bundle passes and each missing piece is
/// caught.
/// </summary>
public sealed class InfoPlistTests : DeviceTest
{
    const string RunnerServiceId = "devtest";

    [Fact]
    public void ThisRunnersBundle_IsConfiguredForItsServiceId()
    {
        // The runner declares _devtest._tcp and _devtest._udp plus a usage description. If this
        // fails, the bundle and Create.NearbyOptions' ServiceId have drifted apart, which is
        // exactly the consumer mistake the check exists to report.

        // Arrange
        List<string> failures = [];
        List<string> warnings = [];

        // Act
        InfoPlistRules.Validate(RunnerServiceId, failures, warnings);

        // Assert
        Assert.Empty(failures);
        Assert.Empty(warnings);
    }

    [Fact]
    public void AServiceIdTheBundleDoesNotDeclare_IsReported()
    {
        // The drift case: a correct bundle, but not for this identifier. Declared under no
        // transport at all is unambiguously broken, so it blocks a start.

        // Arrange
        List<string> failures = [];
        List<string> warnings = [];

        // Act
        InfoPlistRules.Validate("notdeclared", failures, warnings);

        // Assert
        var failure = Assert.Single(failures);
        Assert.Contains(InfoPlistRules.BonjourServicesKey, failure, StringComparison.Ordinal);
        Assert.Contains("_notdeclared._tcp", failure, StringComparison.Ordinal);
        Assert.Empty(warnings);
    }

    /// <summary>
    /// A bundle declaring the given entries, plus a usage description, so a test can pose a
    /// configuration the runner's own correct bundle cannot.
    /// </summary>
    static NSDictionary BundleDeclaring(params string[] bonjourServices)
        => NSDictionary.FromObjectsAndKeys(
            [
                NSArray.FromStrings(bonjourServices),
                new NSString("Device tests."),
            ],
            [
                new NSString(InfoPlistRules.BonjourServicesKey),
                new NSString(InfoPlistRules.LocalNetworkUsageKey),
            ]);

    [Theory]
    [InlineData("_devtest._tcp", TestDisplayName = "TCP only")]
    [InlineData("_devtest._udp", TestDisplayName = "UDP only")]
    public void OneTransport_Warns_AndStillAllowsAStart(string declared)
    {
        // Apple documents no requirement for both transports, and a DTS engineer would not confirm
        // one (forum 651842). Refusing to start here would turn a configuration that may well work
        // into a hard failure on this library's guess, so it warns instead.

        // Arrange
        List<string> failures = [];
        List<string> warnings = [];

        // Act
        InfoPlistRules.Validate(BundleDeclaring(declared), RunnerServiceId, failures, warnings);

        // Assert
        Assert.Empty(failures);
        var warning = Assert.Single(warnings);
        Assert.Contains(InfoPlistRules.BonjourServicesKey, warning, StringComparison.Ordinal);
    }

    [Fact]
    public void NeitherTransport_Fails()
    {
        // Declared under no transport at all: the ServiceId is named nowhere, which is the
        // "_name._tcp form confused with the ServiceId" mistake and is unambiguously broken.

        // Arrange
        List<string> failures = [];
        List<string> warnings = [];

        // Act
        InfoPlistRules.Validate(BundleDeclaring("_somethingelse._tcp"), RunnerServiceId, failures, warnings);

        // Assert
        Assert.Single(failures);
        Assert.Empty(warnings);
    }

    [Fact]
    public void AMissingUsageDescription_Fails_EvenWithBothTransports()
    {
        // Arrange
        List<string> failures = [];
        List<string> warnings = [];
        var info = NSDictionary.FromObjectsAndKeys(
            [NSArray.FromStrings("_devtest._tcp", "_devtest._udp")],
            [new NSString(InfoPlistRules.BonjourServicesKey)]);

        // Act
        InfoPlistRules.Validate(info, RunnerServiceId, failures, warnings);

        // Assert
        var failure = Assert.Single(failures);
        Assert.Contains(InfoPlistRules.LocalNetworkUsageKey, failure, StringComparison.Ordinal);
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task CheckAvailability_ReportsAvailable_ForThisRunnersConfiguration()
    {
        // The whole path, through the public surface: a correctly configured bundle and a valid
        // ServiceId must not report InvalidConfiguration.

        // Arrange
        await using var platform = Create.NearbyTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        var result = await platform.CheckAvailabilityAsync(cts.Token);

        // Assert
        Assert.Equal(NearbyAvailability.Available, result);
    }
}
