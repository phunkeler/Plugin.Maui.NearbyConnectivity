namespace Plugin.Maui.NearbyConnectivity.DeviceTests;

/// <summary>
/// Constructs the real <see cref="NearbyTransport"/> under test, wired with real SDK-backed
/// dependencies (no fakes — see <c>Plugin.Maui.NearbyConnectivity.UnitTests.TestSupport.Create</c>
/// for why the unit suite uses <c>net10.0</c>'s stub instead).
/// </summary>
/// <remarks>
/// The platform halves live in <c>Create.android.cs</c> and <c>Create.ios.cs</c>. Both declare the
/// same member names with platform-specific parameters, so a test body reads identically on either
/// target and this file stays free of conditional compilation.
/// </remarks>
/// <param name="logger">The logger the constructed types write through.</param>
sealed partial class Create(ILogger logger)
{
    /// <summary>The service id every device test advertises under.</summary>
    const string ServiceId = "devtest";

    /// <summary>The logger the constructed types write through, scoped to one test.</summary>
    internal ILogger Logger { get; } = logger;


    /// <summary>The options the platform is wired with unless a test supplies its own.</summary>
    /// <param name="displayName">The local device's display name, for tests that assert on it.</param>
    /// <param name="info">The local device's presence info, for tests that assert on it.</param>
    internal static NearbyOptions DefaultOptions(
        string displayName = "TestDevice",
        ReadOnlyMemory<byte> info = default)
        => new()
        {
            ServiceId = ServiceId,
            Presence = new() { DisplayName = displayName, Info = info },
        };

    /// <summary>
    /// Waits until <paramref name="platform"/> has registered a pending handshake. Needed after
    /// accepting an inbound request: both platforms register the handshake inside the accept, so
    /// there is no signal to await — only the entry's appearance.
    /// </summary>
    /// <param name="platform">The platform to observe.</param>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    internal static async Task WaitForPendingHandshakeAsync(
        NearbyTransport platform, CancellationToken cancellationToken)
    {
        while (platform.Handshakes.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
        }
    }
}
