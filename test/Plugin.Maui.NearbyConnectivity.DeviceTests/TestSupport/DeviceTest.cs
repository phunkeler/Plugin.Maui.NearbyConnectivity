namespace Plugin.Maui.NearbyConnectivity.DeviceTests;

/// <summary>
/// Base class for every device-test class. Owns the per-test logger and exposes the
/// <see cref="DeviceTests.Create"/> factories as instance members, so a test body reads the same
/// as it did when those factories were static.
/// </summary>
/// <remarks>
/// <para>
/// The logger cannot be static. xUnit attaches output to a test result through that test's own
/// <see cref="ITestOutputHelper"/>, so a shared static logger has nothing to write to. Each test
/// class instance is constructed per test, which is what gives every test its own helper.
/// </para>
/// <para>
/// Reading <c>TestContext.Current</c> at write time does not work either: it flows through the
/// execution context, and this plugin's platform callbacks arrive on the iOS delegate's private
/// serial queue and on Android's GMS callback threads. Those are outside the test's execution
/// context, so the ambient lookup returns <see langword="null"/> and the line is dropped — which is
/// exactly the output a failing device test needs. Capturing the helper here, in the constructor,
/// is what makes callback-thread logging reach the TRX.
/// </para>
/// </remarks>
public abstract partial class DeviceTest : IDisposable
{
    readonly TestOutputLoggerProvider _logProvider;
    readonly ILoggerFactory _loggerFactory;
    bool _disposed;

    /// <summary>Creates the per-test logger and factories.</summary>
    protected DeviceTest()
    {
        _logProvider = new TestOutputLoggerProvider(TestContext.Current.TestOutputHelper
            ?? throw new InvalidOperationException("The test has no output helper."));
        _loggerFactory = LoggerFactory.Create(
            builder => builder.AddProvider(_logProvider).SetMinimumLevel(LogLevel.Trace));
        Create = new Create(_loggerFactory.CreateLogger("Plugin.Maui.NearbyConnectivity"));
    }

    /// <summary>The factories for the types under test, wired with this test's logger.</summary>
    private protected Create Create { get; }

    /// <summary>
    /// Detaches the output helper, then disposes the factory. Ordering matters: a platform callback
    /// can outlive the test, and writing to a finished test's helper throws.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Detaches the output helper and disposes the logger factory.</summary>
    /// <param name="disposing">Whether managed state should be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
        {
            return;
        }

        _disposed = true;
        _logProvider.Detach();
        _loggerFactory.Dispose();
    }
}
