using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Maui.Dispatching;
using NSubstitute;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Constructs the types under test. Call sites read <c>Create.Device("a")</c>, which names where the
/// value came from without the test file having to carry a factory of its own.
/// </summary>
/// <remarks>
/// Everything here builds a <em>real</em> object — these are not test doubles. The stand-ins in this
/// suite are <see cref="FakeNearby"/> and <see cref="ScriptedAdapter"/>, hand-written because they
/// drive streams, and an NSubstitute <see cref="IPlatformConnection"/> behind <see cref="Connection"/>.
/// </remarks>
static class Create
{
    /// <summary>A discovered device. Defaults to the state a device is in when first seen.</summary>
    public static NearbyDevice Device(
        string id = "peer-1",
        string? displayName = null,
        ReadOnlyMemory<byte> info = default,
        NearbyDeviceStatus status = NearbyDeviceStatus.Visible)
        => new(id, NearbyPresence.FromRemote(displayName ?? id, info)) { Status = status };

    /// <summary>
    /// A connection over an inert platform half, so a test can drive
    /// <see cref="NearbyConnection.Inbound"/> without a platform behind it.
    /// </summary>
    /// <param name="device">The remote device. Defaults to <see cref="Device"/>.</param>
    /// <param name="receiveChannel">The inbound payload channel. Defaults to an unbounded channel.</param>
    /// <param name="platform">
    /// The platform half to observe or fault. Defaults to a substitute whose sends complete at once.
    /// </param>
    /// <param name="authenticationCode">The connection's authentication code, or <see langword="null"/>.</param>
    public static NearbyConnection Connection(
        NearbyDevice? device = null,
        Channel<NearbyPayload>? receiveChannel = null,
        IPlatformConnection? platform = null,
        string? authenticationCode = null)
        => new(
            device ?? Device(),
            receiveChannel ?? Channel.CreateUnbounded<NearbyPayload>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }),
            platform ?? Substitute.For<IPlatformConnection>(),
            authenticationCode);

    /// <summary>
    /// The session under test, over a <see cref="FakeNearby"/> platform. Takes no dispatcher: every
    /// member of <see cref="INearbyConnectivity"/> is callable from any thread.
    /// </summary>
    public static NearbySession Session(
        FakeNearby platform,
        NearbyOptions? options = null,
        TimeProvider? timeProvider = null)
        => new(platform, NullLogger.Instance, options ?? new NearbyOptions(), timeProvider ?? TimeProvider.System);

    /// <summary>
    /// Discovers a device and hands back the instance that can act on the session: what a consumer
    /// holds after reading a device from <see cref="INearbyConnectivity.Devices"/>. Starts
    /// discovery if it is not already running, because the session only applies device events
    /// while discovering.
    /// </summary>
    /// <remarks>
    /// This is the only route a consumer has to a connectable device, so it is the route tests
    /// take: <c>(await Create.DiscoveredAsync(session, platform, device)).ConnectAsync(ct)</c>.
    /// </remarks>
    public static async Task<NearbyDevice> DiscoveredAsync(
        NearbySession session,
        FakeNearby platform,
        NearbyDevice device)
    {
        if (!session.Discoverer.IsActive)
        {
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        }

        return await platform.EmitDeviceFoundAsync(device);
    }

    /// <summary>
    /// The real transport over a <see cref="ScriptedAdapter"/>. The adapter's defaults mirror the
    /// shipping <c>net10.0</c> stub (starts throw), so tests that only drive the transport's own
    /// members behave as before — and a test that needs the platform side scripts it.
    /// </summary>
    public static NearbyTransport NearbyTransport(
        FakeTimeProvider? timeProvider = null,
        NearbyOptions? options = null,
        ScriptedAdapter? adapter = null)
        => new(
            timeProvider ?? new FakeTimeProvider(),
            options ?? new NearbyOptions(),
            NullLogger.Instance,
            new PeerLookup { Logger = NullLogger.Instance },
            _ => adapter ?? new ScriptedAdapter());

    /// <summary>
    /// A gate a test opens to release work it is holding. Continuations run asynchronously, so
    /// completing the gate never runs the waiter inline on the completing thread.
    /// </summary>
    public static TaskCompletionSource Gate()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// An inbound connection request with inert accept and reject callbacks, for tests that track
    /// the request itself rather than what answering it produces.
    /// </summary>
    /// <param name="device">The requesting device.</param>
    /// <param name="deadline">
    /// The offer's deadline. Defaults to the far future, which the registry clamps to the trust
    /// bound — effectively "does not expire during the test". A test that drives expiry passes a
    /// deadline built from its own clock.
    /// </param>
    /// <param name="timeProvider">The clock that drives the offer countdown. Defaults to the system clock.</param>
    public static NearbyConnectionRequest Request(
        NearbyDevice? device = null,
        DateTimeOffset? deadline = null,
        TimeProvider? timeProvider = null)
        => new(
            device ?? Device(),
            deadline ?? DateTimeOffset.MaxValue,
            accept: _ => Task.FromResult(Connection()),
            reject: _ => Task.CompletedTask,
            timeProvider ?? TimeProvider.System);

    /// <summary>
    /// The session task set on a fake clock, so a join's bound can be driven directly.
    /// </summary>
    /// <param name="time">The clock the join bound runs on.</param>
    public static SessionTaskSet TaskSet(FakeTimeProvider time) => new(time);

    /// <summary>
    /// The discovery refresher on a fake clock, over a real registry.
    /// </summary>
    /// <param name="time">The clock the interval and settle window run on.</param>
    /// <param name="registry">The registry evicted after each settle window.</param>
    /// <param name="refreshAsync">The refresh delegate — return <see langword="false"/> to end the loop.</param>
    /// <param name="interval">The refresh interval, or <see langword="null"/> to make <c>Start</c> a no-op.</param>
    /// <param name="onFailed">Receives the failure that ended the loop early.</param>
    /// <param name="settleWindow">The eviction delay after each refresh. Defaults to one second.</param>
    public static DiscoveryRefresher Refresher(
        FakeTimeProvider time,
        DeviceRegistry registry,
        Func<CancellationToken, Task<bool>> refreshAsync,
        TimeSpan? interval = null,
        Action<Exception>? onFailed = null,
        TimeSpan? settleWindow = null)
        => new(
            interval,
            time,
            registry,
            refreshAsync,
            onFailed ?? (static _ => { }),
            settleWindow ?? TimeSpan.FromSeconds(1));

    /// <summary>
    /// The request registry on a fake clock, with the expiry effects delegate a test observes.
    /// </summary>
    /// <param name="time">The clock the expiry timers run on.</param>
    /// <param name="onExpired">Receives each request whose timer wins the claim.</param>
    public static RequestRegistry RequestRegistry(
        FakeTimeProvider time,
        Func<NearbyConnectionRequest, Task>? onExpired = null)
        => new(
            time,
            onExpired ?? (static _ => Task.CompletedTask));

    /// <summary>
    /// The queue that orders per-peer work, with an error handler that discards what it receives.
    /// </summary>
    /// <param name="onError">
    /// Receives the key and the exception when queued work throws. Pass one when the test asserts
    /// on the failure.
    /// </param>
    /// <param name="timeProvider">
    /// The clock the drain bounds run on. Pass a <see cref="FakeTimeProvider"/> when the test
    /// drives a drain timeout; the real clock otherwise.
    /// </param>
    public static KeyedSerialQueue KeyedSerialQueue(
        Action<string, Exception>? onError = null,
        TimeProvider? timeProvider = null)
        => new(onError ?? ((_, _) => { }), timeProvider ?? TimeProvider.System);

    /// <summary>
    /// The inactivity timeout <see cref="Transfer"/> uses unless a test overrides it. Exposed so a
    /// test can advance the clock relative to the deadline without restating the number.
    /// </summary>
    public const double TransferTimeoutSeconds = 10;

    /// <summary>An outgoing transfer wired to a fake clock, so the deadline can be driven directly.</summary>
    public static OutgoingTransfer Transfer(
        FakeTimeProvider time,
        IProgress<NearbyTransferProgress>? progress = null,
        TimeSpan? timeout = null,
        string deviceId = "device-1",
        long payloadId = 1)
        => new(
            deviceId,
            payloadId,
            progress,
            timeout ?? TimeSpan.FromSeconds(TransferTimeoutSeconds),
            time,
            NullLogger.Instance);

    /// <summary>One progress report, as the platform would raise it mid-transfer.</summary>
    public static NearbyTransferProgress ProgressUpdate(NearbyTransferStatus status, long bytes = 0)
        => new(payloadId: 1, bytesTransferred: bytes, totalBytes: 100, status);

    /// <summary>
    /// A device collection that binds <see cref="NearbyDevice"/> rows directly — the shape a
    /// consumer writes when the view has no row type of its own.
    /// </summary>
    /// <param name="nearby">The session to watch.</param>
    /// <param name="dispatcher">
    /// Where collection mutations run. Defaults to an <see cref="InlineDispatcher"/>, which runs
    /// them on the calling thread.
    /// </param>
    /// <param name="filter">Selects which devices the collection shows. Defaults to all of them.</param>
    public static NearbyDeviceCollection Devices(
        INearbyConnectivity nearby,
        IDispatcher? dispatcher = null,
        Func<NearbyDevice, bool>? filter = null)
        => new(nearby, dispatcher ?? new InlineDispatcher(), filter);
}
