namespace Plugin.Maui.NearbyConnectivity.UiTests.Infrastructure;

/// <summary>
/// Base for every test class: resolves the page objects and skips the whole suite when no Appium
/// server is configured.
/// </summary>
public abstract class UiTest : IDisposable
{
    readonly UiTestFixture _fixture;

    protected UiTest(UiTestFixture fixture)
    {
        _fixture = fixture;

        Assert.SkipUnless(
            UiTestFixture.IsConfigured,
            $"No Appium server configured ({AppiumDriverFactory.ServerUrlVariable} is unset). " +
            "Run via scripts/device-tests.ps1 -Suite UiTests.");

        Shell = new AppShellNav(fixture.Session);
    }

    /// <summary>Entry point for navigation. Every page object is reached through this.</summary>
    protected AppShellNav Shell { get; }

    /// <summary>
    /// Captures a screenshot when a test fails, so a red CI run carries evidence rather than only a
    /// stack trace. Written to the directory device-tests.ps1 collects.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (!UiTestFixture.IsConfigured || TestContext.Current.TestState?.Result is not TestResult.Failed)
        {
            return;
        }

        var directory = Environment.GetEnvironmentVariable("UITEST_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        var name = TestContext.Current.Test?.TestDisplayName ?? "unknown";
        var safe = string.Concat(name.Split(Path.GetInvalidFileNameChars()));

        try
        {
            _fixture.Session.SaveScreenshot(Path.Combine(directory, $"{safe}.png"));
        }
        catch (WebDriverException)
        {
            // A dead session cannot be photographed; the test failure is the signal that matters.
        }
    }
}
