namespace Plugin.Maui.NearbyConnectivity.UiTests.Infrastructure;

/// <summary>
/// Owns the one driver session for the assembly. Inert when the Appium environment is unset, so the
/// suite is a no-op on a developer machine and in any CI job that has not started a server — the
/// same pattern the NearbyChat sample suite uses.
/// </summary>
/// <remarks>
/// Shared through <see cref="OneDeviceSuite"/>, never <c>IClassFixture</c>: one per test class
/// would be one driver session per class, racing for the same device.
/// </remarks>
public sealed class UiTestFixture : IDisposable
{
    AppSession? _session;

    internal AppSession Session => _session
        ?? throw new InvalidOperationException("No Appium session; guard with SkipUnlessConfigured.");

    public static bool IsConfigured => AppiumDriverFactory.IsConfigured;

    public UiTestFixture()
    {
        if (IsConfigured)
        {
            _session = new AppSession();
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
