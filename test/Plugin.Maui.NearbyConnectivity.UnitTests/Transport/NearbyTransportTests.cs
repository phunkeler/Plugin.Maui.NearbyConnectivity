using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers the channel bridge platform callbacks write into: <c>OnDeviceFound</c>,
/// <c>WriteConnectionRequest</c>, <c>ResolveConnectionTcs</c> and their siblings.
/// </summary>
/// <remarks>
/// <para>
/// <strong>These tests read internal fields, against this suite's usual rule of asserting only
/// through the surface a consumer uses.</strong> The natural observation point is
/// <c>AdvertiseAsync</c> / <c>DiscoverAsync</c>, but both call a <c>Platform*</c> start that throws
/// <see cref="PlatformNotSupportedException"/> on <c>net10.0</c>, so no enumeration can reach the
/// channel. Reading the channel directly is the only way to test this layer off-device.
/// </para>
/// <para>
/// The swap itself — <c>AdvertiseAsync</c> exchanging <c>_advertiseChannel</c> on every call —
/// is covered by the <see cref="ScriptedSeam"/> tests below, which enumerate through a
/// <see cref="ScriptedAdapter"/> whose start succeeds. That closed the gap this remark used to
/// track: the write-side tests here verify the write, the seam tests verify the swap.
/// </para>
/// </remarks>
[Trait("Category", "Connections")]
public class NearbyTransportTests
{
    public sealed class WriteConnectionRequest : NearbyTransportTests
    {
        [Fact]
        public async Task YieldsRequestOnAdvertiseChannel()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1", "Alice");

            NearbyConnectionRequest? captured = null;
            var tcs = new TaskCompletionSource<NearbyConnection>(TaskCreationOptions.RunContinuationsAsynchronously);

            var request = new NearbyConnectionRequest(
                device,
                DateTimeOffset.MaxValue,
                accept: ct => tcs.Task.WaitAsync(ct),
                reject: ct => Task.CompletedTask,
                TimeProvider.System);

            // Act
            platform.WriteConnectionRequest(request);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            // Read one item directly from the internal advertise channel reader
            var reader = platform._advertiseChannel.Reader;
            captured = await reader.ReadAsync(cts.Token);

            // Assert
            Assert.NotNull(captured);
            Assert.Same(device, captured.RemoteDevice);
        }

        [Fact]
        public async Task MultipleRequests_AllYielded()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device1 = Create.Device("peer-1", "Alice");
            var device2 = new NearbyDevice("peer-2", NearbyPresence.FromRemote("Bob", default));

            var tcs = new TaskCompletionSource<NearbyConnection>(TaskCreationOptions.RunContinuationsAsynchronously);

            platform.WriteConnectionRequest(new NearbyConnectionRequest(device1, DateTimeOffset.MaxValue, ct => tcs.Task.WaitAsync(ct), ct => Task.CompletedTask, TimeProvider.System));
            platform.WriteConnectionRequest(new NearbyConnectionRequest(device2, DateTimeOffset.MaxValue, ct => tcs.Task.WaitAsync(ct), ct => Task.CompletedTask, TimeProvider.System));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var reader = platform._advertiseChannel.Reader;

            // Act
            var first = await reader.ReadAsync(cts.Token);
            var second = await reader.ReadAsync(cts.Token);

            // Assert
            Assert.Equal("peer-1", first.RemoteDevice.Id);
            Assert.Equal("peer-2", second.RemoteDevice.Id);
        }

        // The regression: a request arriving after the advertise channel completed reaches no
        // session, so answering it through the request's own DeclineAsync goes to the session
        // gateway, which claims a request it never tracked, fails, and returns — leaving the
        // platform holding an offer nobody ever answers. The transport must call the platform
        // half itself.
        [Fact]
        public async Task ArrivingAfterTheChannelCompleted_RejectsOnThePlatform()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1", "Alice");
            var rejected = false;

            var request = new NearbyConnectionRequest(
                device,
                DateTimeOffset.MaxValue,
                accept: _ => Task.FromResult(Create.Connection()),
                reject: _ =>
                {
                    rejected = true;
                    return Task.CompletedTask;
                },
                TimeProvider.System);

            platform._advertiseChannel.Writer.TryComplete();

            // Act
            platform.WriteConnectionRequest(request);

            // Assert
            await Wait.UntilAsync(() => rejected);
            Assert.True(rejected);
            Assert.False(request.IsPending);
        }
    }

    public sealed class ConnectDeadline : NearbyTransportTests
    {
        [Fact]
        public void NoHandshake_ReportsNone()
        {
            // Arrange
            var platform = Create.NearbyTransport();

            // Act
            var found = platform.TryGetConnectDeadline("peer-1", out var deadline);

            // Assert
            Assert.False(found);
            Assert.Null(deadline);
        }

        [Fact]
        public async Task WhileConnecting_ReportsOfferWindowFromNow()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var options = new NearbyOptions { ServiceId = "svc", OfferWindow = TimeSpan.FromSeconds(30) };
            var adapter = new ScriptedAdapter { OnInitiateConnect = static (_, _) => Task.CompletedTask };
            var platform = Create.NearbyTransport(time, options, adapter);
            var device = Create.Device("peer-1", "Alice");
            var start = time.GetUtcNow();

            // Act
            // The handshake parks on its TCS, so it is still in flight when we read
            var connecting = platform.ConnectAsync(device, CancellationToken.None);
            var found = platform.TryGetConnectDeadline(device.Id, out var deadline);

            // Assert
            Assert.True(found);
            Assert.Equal(start + TimeSpan.FromSeconds(30), deadline);

            // Cleanup
            platform.ResolveConnectionTcs(device.Id, Create.Connection(device: device));
            await connecting;
        }

        [Fact]
        public async Task OverLongOfferWindow_ReportsClampedDeadline()
        {
            // Arrange
            // A window past the trust bound must surface clamped, not as declared.
            var time = new FakeTimeProvider();
            var options = new NearbyOptions { ServiceId = "svc", OfferWindow = TimeSpan.FromHours(6) };
            var adapter = new ScriptedAdapter { OnInitiateConnect = static (_, _) => Task.CompletedTask };
            var platform = Create.NearbyTransport(time, options, adapter);
            var device = Create.Device("peer-1", "Alice");
            var start = time.GetUtcNow();

            // Act
            var connecting = platform.ConnectAsync(device, CancellationToken.None);
            platform.TryGetConnectDeadline(device.Id, out var deadline);

            // Assert
            Assert.Equal(start + TimeSpan.FromMinutes(30), deadline);

            // Cleanup
            platform.ResolveConnectionTcs(device.Id, Create.Connection(device: device));
            await connecting;
        }

        [Fact]
        public async Task HandshakeSettled_ReportsNone()
        {
            // Arrange
            var adapter = new ScriptedAdapter { OnInitiateConnect = static (_, _) => Task.CompletedTask };
            var platform = Create.NearbyTransport(adapter: adapter);
            var device = Create.Device("peer-1", "Alice");
            var connecting = platform.ConnectAsync(device, CancellationToken.None);

            // Act
            platform.ResolveConnectionTcs(device.Id, Create.Connection(device: device));
            await connecting;

            // Assert
            Assert.False(platform.TryGetConnectDeadline(device.Id, out var deadline));
            Assert.Null(deadline);
        }
    }

    public sealed class ResolveConnectionTcs : NearbyTransportTests
    {
        [Fact]
        public async Task AcceptAsync_ResolveConnectionTcs_CompletesWithNearbyConnection()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1", "Alice");

            // Simulate what ConnectAsync does: register a TCS keyed by peer ID
            var tcs = platform.Handshakes.Register("peer-1", out _, deadline: null, attributedTo: CancellationToken.None);

            var connection = Create.Connection(device: device);

            // Act
            // Simulate platform callback resolving the TCS
            platform.ResolveConnectionTcs("peer-1", connection);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var result = await tcs.Task.WaitAsync(cts.Token);

            // Assert
            Assert.Same(connection, result);
        }

        [Fact]
        public async Task RegistersConnectionInActiveConnections()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1", "Alice");

            var tcs = platform.Handshakes.Register("peer-1", out _, deadline: null, attributedTo: CancellationToken.None);

            var connection = Create.Connection(device: device);

            // Act
            platform.ResolveConnectionTcs("peer-1", connection);
            await tcs.Task; // wait for resolution

            // Assert
            // The connection is now in the connection table
            Assert.True(platform.Connections.IsConnected("peer-1"));
        }

        [Fact]
        public async Task ExposesConnectionThroughTheSeam()
        {
            // The platform table is the one owner of "device X has a live connection"; the
            // session queries these two members instead of keeping a second table.

            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1", "Alice");

            var tcs = platform.Handshakes.Register("peer-1", out _, deadline: null, attributedTo: CancellationToken.None);

            var connection = Create.Connection(device: device);

            // Act
            platform.ResolveConnectionTcs("peer-1", connection);
            await tcs.Task;

            // Assert
            Assert.True(platform.TryGetConnection("peer-1", out var lookedUp));
            Assert.Same(connection, lookedUp);
            Assert.Same(connection, Assert.Single(platform.SnapshotConnections()));
        }

        [Fact]
        public async Task FaultConnectionTcs_WrapsAForeignExceptionAsConnectFailed()
        {
            // NearbyDevice.ConnectAsync documents NearbyConnectException and tells the caller to
            // branch on Reason. A platform SDK's own exception type must never reach them, so
            // anything that is not already one is wrapped here, keeping the original as the
            // inner exception.

            // Arrange
            var platform = Create.NearbyTransport();
            var tcs = platform.Handshakes.Register("peer-1", out _, deadline: null, attributedTo: CancellationToken.None);
            var sdkException = new InvalidOperationException("connection failed");

            // Act
            platform.FaultConnectionTcs("peer-1", sdkException);

            // Assert
            var ex = await Assert.ThrowsAsync<NearbyConnectException>(async () => await tcs.Task);
            Assert.Equal(NearbyConnectFailure.Failed, ex.Reason);
            Assert.Equal("connection failed", ex.Message);
            Assert.Same(sdkException, ex.InnerException);
        }

        [Fact]
        public async Task FaultConnectionTcs_PassesAConnectExceptionThrough()
        {
            // The reason a caller branches on is the adapter's to choose: a connect exception
            // that already carries one is never re-wrapped into Failed.

            // Arrange
            var platform = Create.NearbyTransport();
            var tcs = platform.Handshakes.Register("peer-1", out _, deadline: null, attributedTo: CancellationToken.None);
            var declined = new NearbyConnectException(NearbyConnectFailure.DeviceUnknown, "gone");

            // Act
            platform.FaultConnectionTcs("peer-1", declined);

            // Assert
            Assert.Same(declined, await Assert.ThrowsAsync<NearbyConnectException>(async () => await tcs.Task));
        }

        [Fact]
        public async Task NoRegisteredTcs_ClosesTheOrphanedLink()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var link = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(device: Create.Device("peer-1", "Alice"), platform: link);

            // Act
            platform.ResolveConnectionTcs("peer-1", connection);
            var reason = await connection.Closed.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            // Assert
            Assert.False(platform.Connections.IsConnected("peer-1"));
            Assert.Equal(NearbyEndReason.ClosedLocally, reason);
            await link.Received(1).DisposeAsync();
        }
    }

    public sealed class OnDeviceFound : NearbyTransportTests
    {
        [Fact]
        public async Task YieldsFoundEventOnDiscoverChannel()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1", "Alice");

            // Act
            platform.OnDeviceFound(device);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var evt = await platform._discoverChannel.Reader.ReadAsync(cts.Token);

            // Assert
            Assert.True(evt.Found);
            Assert.Same(device, evt.Device);
        }

        [Fact]
        public async Task ForgetPeer_YieldsLostEventOnDiscoverChannel()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = platform.PeerLookup.Record("peer-1", "Alice");

            // Act
            var forgotten = platform.ForgetPeer("peer-1");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var evt = await platform._discoverChannel.Reader.ReadAsync(cts.Token);

            // Assert
            Assert.Same(device, forgotten);
            Assert.False(evt.Found);
            Assert.Same(device, evt.Device);
        }

        [Fact]
        public void ForgetPeer_ForAnUnknownPeer_PublishesNothing()
        {
            // A peer already forgotten is not reported lost a second time: the mapping is the
            // one record of whether this device still owes a lost event.

            // Arrange
            var platform = Create.NearbyTransport();

            // Act
            var forgotten = platform.ForgetPeer("peer-nobody-knows");

            // Assert
            Assert.Null(forgotten);
            Assert.False(platform._discoverChannel.Reader.TryRead(out _));
        }

        [Fact]
        public async Task ThenLost_PreservesOrder()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1", "Alice");

            // Act
            platform.PeerLookup.Record(device.Id, device.Presence.DisplayName);
            platform.OnDeviceFound(device);
            platform.ForgetPeer(device.Id);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var reader = platform._discoverChannel.Reader;
            var first = await reader.ReadAsync(cts.Token);
            var second = await reader.ReadAsync(cts.Token);

            // Assert
            Assert.True(first.Found);
            Assert.False(second.Found);
        }
    }

    public sealed class WritePayload : NearbyTransportTests
    {
        [Fact]
        public async Task RoutesPayloadToActiveConnection()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1", "Alice");

            var tcs = platform.Handshakes.Register("peer-1", out _, deadline: null, attributedTo: CancellationToken.None);

            var receiveChannel = Channel.CreateUnbounded<NearbyPayload>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            var connection = Create.Connection(device: device, receiveChannel: receiveChannel);

            platform.ResolveConnectionTcs("peer-1", connection);
            await tcs.Task;

            var payload = new NearbyBytesPayload(new byte[] { 1, 2, 3 });

            // Act
            platform.Connections.WritePayload("peer-1", payload);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var received = await receiveChannel.Reader.ReadAsync(cts.Token);

            // Assert
            Assert.Same(payload, received);
        }

        [Fact]
        public void UnknownPeer_DoesNotThrow()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var payload = new NearbyBytesPayload(new byte[] { 1, 2, 3 });

            // Act
            platform.Connections.WritePayload("nonexistent-peer", payload);

            // Assert
            // Unknown peer silently ignored; no connection was registered
            Assert.False(platform.Connections.IsConnected("nonexistent-peer"));
        }

        [Fact]
        public void UnknownPeer_DeletesTheStagedFile()
        {
            // Arrange
            using var temp = new TempDirectory();
            var staged = temp.Touch("photo.jpg");
            var platform = Create.NearbyTransport();

            // Act
            platform.Connections.WritePayload("nonexistent-peer", NearbyFilePayload.Staged(staged));

            // Assert
            Assert.False(File.Exists(staged));
        }
    }

    public sealed class Construction : NearbyTransportTests
    {
        [Fact]
        public void SweepsStagingLeftByAnEarlierRun()
        {
            // Arrange
            var adapter = new ScriptedAdapter();

            // Act
            _ = Create.NearbyTransport(adapter: adapter);

            // Assert
            Assert.Equal(1, adapter.SweepStagingCount);
        }
    }

    public sealed class DisposeAsync : NearbyTransportTests
    {
        [Fact]
        public async Task SweepsStagingAgain()
        {
            // Arrange
            var adapter = new ScriptedAdapter();
            var platform = Create.NearbyTransport(adapter: adapter);

            // Act
            await platform.DisposeAsync();

            // Assert
            Assert.Equal(2, adapter.SweepStagingCount);
        }

        [Fact]
        public async Task CompletesAdvertiseChannel()
        {
            // Arrange
            var platform = Create.NearbyTransport();

            // Act
            await platform.DisposeAsync();

            // Assert
            // Channel writer is completed, so reader will complete immediately
            Assert.True(platform._advertiseChannel.Reader.Completion.IsCompleted);
        }

        [Fact]
        public async Task CompletesDiscoverChannel()
        {
            // Arrange
            var platform = Create.NearbyTransport();

            // Act
            await platform.DisposeAsync();

            // Assert
            Assert.True(platform._discoverChannel.Reader.Completion.IsCompleted);
        }

        [Fact]
        public async Task CancelsPendingConnectionTcs()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            using var cts = new CancellationTokenSource();
            var tcs = platform.Handshakes.Register("peer-1", out _, deadline: null, attributedTo: cts.Token);

            // Act
            await platform.DisposeAsync();

            // Assert
            Assert.True(tcs.Task.IsCanceled);
        }

        [Fact]
        public async Task CalledTwice_DoesNotThrow()
        {
            // Arrange
            var platform = Create.NearbyTransport();

            // Act
            await platform.DisposeAsync();
#pragma warning disable S3966 // The second dispose is the behavior under test.
            await platform.DisposeAsync();
#pragma warning restore S3966

            // Assert
            Assert.True(platform._advertiseChannel.Reader.Completion.IsCompleted);
        }
    }

    /// <summary>
    /// A second caller for a device whose handshake is already registered. It joins the pending
    /// handshake rather than starting one, and must not be able to disturb the owner's.
    /// </summary>
    public sealed class JoinedHandshake : NearbyTransportTests
    {
        [Fact]
        public void SecondCaller_JoinsRatherThanReplacing()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            using var owner = new CancellationTokenSource();
            using var joiner = new CancellationTokenSource();

            // Act
            var first = platform.Handshakes.Register("peer-1", out var firstJoined, deadline: null, attributedTo: owner.Token);
            var second = platform.Handshakes.Register("peer-1", out var secondJoined, deadline: null, attributedTo: joiner.Token);

            // Assert
            // One handshake, and only the second caller is told it joined.
            Assert.Same(first, second);
            Assert.False(firstJoined);
            Assert.True(secondJoined);
        }

        [Fact]
        public void SecondCaller_WithSameToken_StillJoins()
        {
            // Arrange
            var platform = Create.NearbyTransport();

            // Act
            var first = platform.Handshakes.Register("peer-1", out var firstJoined, deadline: null, attributedTo: CancellationToken.None);
            var second = platform.Handshakes.Register("peer-1", out var secondJoined, deadline: null, attributedTo: CancellationToken.None);

            // Assert
            Assert.Same(first, second);
            Assert.False(firstJoined);
            Assert.True(secondJoined);
        }

        [Fact]
        public async Task JoinerCancelling_LeavesOwnerHandshakeIntact()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1", "Alice");
            using var owner = new CancellationTokenSource();
            using var joiner = new CancellationTokenSource();
            var shared = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: owner.Token);
            var joining = platform.ConnectAsync(device, joiner.Token);

            // Act
            await joiner.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joining);

            // Assert
            Assert.True(platform.Handshakes.TryJoin(device.Id, out _));
            Assert.False(shared.Task.IsCompleted);
        }

        [Fact]
        public async Task JoinerCancelling_ReportsItsOwnToken()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1", "Alice");
            using var owner = new CancellationTokenSource();
            using var joiner = new CancellationTokenSource();
            _ = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: owner.Token);
            var joining = platform.ConnectAsync(device, joiner.Token);

            // Act
            await joiner.CancelAsync();

            // Assert
            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joining);
            Assert.Equal(joiner.Token, ex.CancellationToken);
        }

        [Fact]
        public async Task OwnerCancelling_DoesNotSurfaceForeignTokenToJoiner()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1", "Alice");
            using var owner = new CancellationTokenSource();
            using var joiner = new CancellationTokenSource();

            var shared = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: owner.Token);
            var joining = platform.ConnectAsync(device, joiner.Token);

            // Act
            // The owner abandons; the joiner's own token never fires.
            shared.TrySetCanceled(owner.Token);

            // Assert
            var ex = await Assert.ThrowsAsync<NearbyConnectException>(() => joining);
            Assert.Equal(NearbyConnectFailure.Failed, ex.Reason);
            Assert.False(joiner.Token.IsCancellationRequested);
        }
    }

    /// <summary>
    /// Registering a handshake against a transport that is tearing down. A platform callback can land
    /// after teardown begins — both SDKs keep dispatching briefly — so the source must never be
    /// left pending with nothing able to complete it.
    /// </summary>
    public sealed class RegisterConnectionTcsDuringDispose : NearbyTransportTests
    {
        [Fact]
        public async Task AfterDispose_ReturnsCancelledSource()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            using var cts = new CancellationTokenSource();
            await platform.DisposeAsync();

            // Act
            // The callback arrives strictly after the sweep has run.
            var tcs = platform.Handshakes.Register("peer-1", out _, deadline: null, attributedTo: cts.Token);

            // Assert
            // Cancelled, and not left behind in the swept table.
            Assert.True(tcs.Task.IsCanceled);
            Assert.False(platform.Handshakes.TryJoin("peer-1", out _));
        }

        [Fact]
        public async Task RacingDispose_NeverLeavesSourcePending()
        {
            // A plain check *before* the insert is not atomic against the sweep: a registration
            // landing between the two adds a source the sweep has already passed, so neither the
            // sweep nor a terminal callback completes it and its awaiter hangs. Both orderings must
            // resolve the source.
            for (var attempt = 0; attempt < 200; attempt++)
            {
                // Arrange
                var platform = Create.NearbyTransport();
                using var cts = new CancellationTokenSource();
                var ready = Create.Gate();

                var registering = Task.Run(
                    async () =>
                    {
                        await ready.Task;

                        return platform.Handshakes.Register("peer-1", out _, deadline: null, attributedTo: cts.Token);
                    },
                    TestContext.Current.CancellationToken);

                var disposing = Task.Run(
                    async () =>
                    {
                        await ready.Task;
                        await platform.DisposeAsync();
                    },
                    TestContext.Current.CancellationToken);

                // Act
                // Release both threads together to interleave insert and sweep.
                ready.SetResult();
                await Task.WhenAll(registering, disposing);

                // Assert
                // Resolved by whichever side won, never left pending.
                var tcs = await registering;
                Assert.True(
                    tcs.Task.IsCompleted,
                    $"attempt {attempt}: handshake source left pending after a racing dispose");
                Assert.True(tcs.Task.IsCanceled);
            }
        }

        [Fact]
        public async Task AJoinerRacingDispose_NeverCancelsTheOwnerWithItsOwnToken()
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                // Arrange
                var platform = Create.NearbyTransport();
                using var ownerCts = new CancellationTokenSource();
                using var joinerCts = new CancellationTokenSource();

                var owner = platform.Handshakes.Register("peer-1", out var ownerJoined, deadline: null, attributedTo: ownerCts.Token);
                Assert.False(ownerJoined);

                var ready = Create.Gate();

                var joining = Task.Run(
                    async () =>
                    {
                        await ready.Task;

                        return platform.Handshakes.Register("peer-1", out _, deadline: null, attributedTo: joinerCts.Token);
                    },
                    TestContext.Current.CancellationToken);

                var disposing = Task.Run(
                    async () =>
                    {
                        await ready.Task;
                        await platform.DisposeAsync();
                    },
                    TestContext.Current.CancellationToken);

                // Act
                ready.SetResult();
                await Task.WhenAll(joining, disposing);

                // Assert
                Assert.True(
                    owner.Task.IsCompleted,
                    $"attempt {attempt}: owner's handshake left pending");

                var cancelled = await Record.ExceptionAsync(() => owner.Task) as OperationCanceledException;

                Assert.NotNull(cancelled);
                Assert.NotEqual(joinerCts.Token, cancelled.CancellationToken);
            }
        }
    }

    /// <summary>
    /// The logic only the scripted adapter can reach off-device: the channel swap on restart, the
    /// teardown-versus-deadline attribution, and the drain-then-release order.
    /// </summary>
    public sealed class ScriptedSeam : NearbyTransportTests
    {
        [Fact]
        public async Task Restart_SwapsToAFreshChannel_AndTheNewEnumerationReceives()
        {
            // Arrange
            // An adapter whose start succeeds, so enumeration reaches the channel.
            var adapter = new ScriptedAdapter { OnStartAdvertising = static _ => Task.CompletedTask };
            var platform = Create.NearbyTransport(adapter: adapter);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act
            // First session receives, ends, and a second session receives on the fresh channel.
            var first = platform
                .AdvertiseAsync(Create.Gate(), cts.Token)
                .GetAsyncEnumerator(cts.Token);
            var firstMove = first.MoveNextAsync();
            platform.WriteConnectionRequest(Create.Request(Create.Device("peer-1")));
            Assert.True(await firstMove);
            var firstRequest = first.Current;
            await first.DisposeAsync();

            var second = platform
                .AdvertiseAsync(Create.Gate(), cts.Token)
                .GetAsyncEnumerator(cts.Token);
            var secondMove = second.MoveNextAsync();
            platform.WriteConnectionRequest(Create.Request(Create.Device("peer-2")));
            Assert.True(await secondMove);
            var secondRequest = second.Current;
            await second.DisposeAsync();

            // Assert
            // The restart reached a channel that was not the completed first one.
            Assert.Equal("peer-1", firstRequest.RemoteDevice.Id);
            Assert.Equal("peer-2", secondRequest.RemoteDevice.Id);
        }

        [Fact]
        public async Task DisposeMidHandshake_CancelsThePendingHandshake()
        {
            // The teardown source of cancellation, attributed as cancellation — not reported as a
            // deadline that never elapsed. The device suite pins this on-device; this is the
            // off-device twin the scripted seam makes possible.

            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1");
            var tcs = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: CancellationToken.None);
            var pending = platform.AwaitHandshakeAsync(
                device,
                tcs,
                isInitiator: false,
                TimeSpan.FromSeconds(15),
                beforeAwait: static _ => Task.CompletedTask,
                CancellationToken.None);

            // Act
            await platform.DisposeAsync();

            // Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }

        // The acceptor's half of the exception contract. The platform's accept runs as beforeAwait,
        // and whatever its SDK throws — a bare NearbyException on Android when the endpoint mapping
        // is gone, or a GMS ApiException — used to reach the caller raw, past the documented
        // catch (NearbyConnectException ex) when (ex.Reason is …).
        [Fact]
        public async Task AcceptorPlatformThrow_SurfacesAsNearbyConnectException()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1");
            var tcs = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: CancellationToken.None);
            var platformFailure = new NearbyException("the platform no longer tracks it");

            // Act
            var accepting = platform.AwaitHandshakeAsync(
                device,
                tcs,
                isInitiator: false,
                TimeSpan.FromSeconds(15),
                beforeAwait: _ => throw platformFailure,
                CancellationToken.None);

            // Assert
            // Wrapped, with the platform failure kept as the cause.
            var ex = await Assert.ThrowsAsync<NearbyConnectException>(() => accepting);
            Assert.Equal(NearbyConnectFailure.Failed, ex.Reason);
            Assert.Same(platformFailure, ex.InnerException);
        }

        // A NearbyConnectException the platform already shaped passes through unchanged rather
        // than being wrapped in a second one that loses its reason.
        [Fact]
        public async Task AcceptorConnectException_IsNotRewrapped()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1");
            var tcs = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: CancellationToken.None);
            var platformFailure = new NearbyConnectException(
                NearbyConnectFailure.DeviceUnknown,
                "the platform no longer tracks it");

            // Act
            var accepting = platform.AwaitHandshakeAsync(
                device,
                tcs,
                isInitiator: false,
                TimeSpan.FromSeconds(15),
                beforeAwait: _ => throw platformFailure,
                CancellationToken.None);

            // Assert
            var ex = await Assert.ThrowsAsync<NearbyConnectException>(() => accepting);
            Assert.Same(platformFailure, ex);
            Assert.Equal(NearbyConnectFailure.DeviceUnknown, ex.Reason);
        }

        // The leak: a start cancelled while the platform call is in flight used to rethrow past the
        // stop, because the catch filter excluded the caller's own cancellation. The platform had
        // often already started — GMS completes the start anyway, MPC is advertising as soon as the
        // advertiser is built — so the radio stayed on while IsAdvertising read false, and the next
        // start overwrote the SDK object.
        [Fact]
        public async Task StartCancelledMidFlight_StillStopsThePlatform()
        {
            // Arrange
            // A start that parks until the test cancels it.
            var started = Create.Gate();
            var adapter = new ScriptedAdapter
            {
                OnStartAdvertising = async ct =>
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                },
            };

            var platform = Create.NearbyTransport(adapter: adapter);
            using var cts = new CancellationTokenSource();

            var enumerator = platform.AdvertiseAsync(Create.Gate(), cts.Token).GetAsyncEnumerator(cts.Token);
            var moving = enumerator.MoveNextAsync();
            await started.Task;

            // Act
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await moving);

            // Assert
            Assert.Equal(1, adapter.StopAdvertisingCount);
        }

        [Fact]
        public async Task StartCancelledMidFlight_ReportsCancellationThroughStarted()
        {
            // The pump reads `started` to learn how the start ended. A cancellation must arrive as
            // cancellation, not as a fault the session would log as a start failure.

            // Arrange
            var running = Create.Gate();
            var adapter = new ScriptedAdapter
            {
                OnStartDiscovery = async ct =>
                {
                    running.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                },
            };

            var platform = Create.NearbyTransport(adapter: adapter);
            using var cts = new CancellationTokenSource();
            var started = Create.Gate();

            var enumerator = platform.DiscoverAsync(started, cts.Token).GetAsyncEnumerator(cts.Token);
            var moving = enumerator.MoveNextAsync();
            await running.Task;

            // Act
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await moving);

            // Assert
            Assert.True(started.Task.IsCanceled);
            Assert.Equal(1, adapter.StopDiscoveringCount);
        }

        [Fact]
        public async Task Release_DrainsThePeersWorkBeforeTheAdapterReleases()
        {
            // Drain, then release: the adapter must not free the peer's handles while queued
            // inbound work can still be reading them.

            // Arrange
            // A queued task the test holds open.
            var adapter = new ScriptedAdapter();
            var platform = Create.NearbyTransport(adapter: adapter);
            var gate = Create.Gate();
            _ = platform.Connections.WorkQueue.Enqueue("peer-1", () => gate.Task);
            await Task.Yield();

            // Act
            var release = platform.ReleaseConnectionAsync("peer-1").AsTask();
            await Task.Delay(50, TestContext.Current.CancellationToken);
            var releasedWhileDraining = adapter.Released.Count;
            gate.SetResult();
            await release;

            // Assert
            Assert.Equal(0, releasedWhileDraining);
            Assert.Equal("peer-1", Assert.Single(adapter.Released));
        }
    }

    /// <summary>
    /// The idle span: the stretch between the first confirmed start and the moment nothing is
    /// running, connected or being negotiated. The transport owns the decision; the adapter is only
    /// told. iOS holds a session and a local peer for exactly this span.
    /// </summary>
    public sealed class IdleSpan : NearbyTransportTests
    {
        static ScriptedAdapter Starting() => new()
        {
            OnStartAdvertising = static _ => Task.CompletedTask,
            OnStartDiscovery = static _ => Task.CompletedTask,
            OnInitiateConnect = static (_, _) => Task.CompletedTask,
        };

        [Fact]
        public async Task ARunEnding_WithNothingLive_ReleasesTheIdleState()
        {
            // Arrange
            var adapter = Starting();
            var platform = Create.NearbyTransport(adapter: adapter);
            using var cts = new CancellationTokenSource();

            var enumerator = platform.AdvertiseAsync(Create.Gate(), cts.Token).GetAsyncEnumerator(cts.Token);
            var moving = enumerator.MoveNextAsync();

            // Act
            // Cancelling ends the enumeration, which runs Step's finally.
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await moving);
            await enumerator.DisposeAsync();

            // Assert
            Assert.True(platform.IsIdle);
            Assert.Equal(1, adapter.ReleaseIdleStateCount);
        }

        [Fact]
        public async Task WhileARunIsUp_NeverReleases()
        {
            // A run with no peers is still a reason to hold the platform's state: the advertiser
            // and the local peer it was minted for are both live.

            // Arrange
            var adapter = Starting();
            var platform = Create.NearbyTransport(adapter: adapter);
            using var cts = new CancellationTokenSource();

            var enumerator = platform.AdvertiseAsync(Create.Gate(), cts.Token).GetAsyncEnumerator(cts.Token);
            var moving = enumerator.MoveNextAsync();
            await Wait.UntilAsync(() => !platform.IsIdle);

            // Act
            // A handshake opens and fails while the run is still up.
            var device = Create.Device("peer-1");
            var connecting = platform.ConnectAsync(device, CancellationToken.None);
            platform.FaultConnectionTcs(device.Id, new NearbyException("no"));
            await Assert.ThrowsAsync<NearbyConnectException>(() => connecting);

            // Assert
            Assert.False(platform.IsIdle);
            Assert.Equal(0, adapter.ReleaseIdleStateCount);

            // Cleanup
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await moving);
            await enumerator.DisposeAsync();
        }

        [Fact]
        public async Task AHandshakeInFlight_HoldsTheSpanOpenPastTheLastRun()
        {
            // The pending handshake has no entry in the connection table yet, and on iOS it is
            // exactly what the session handle is being held for.

            // Arrange
            var adapter = Starting();
            var platform = Create.NearbyTransport(adapter: adapter);
            var device = Create.Device("peer-1");
            var connecting = platform.ConnectAsync(device, CancellationToken.None);

            // Act
            // A run starts and ends underneath the handshake.
            using var cts = new CancellationTokenSource();
            var enumerator = platform.AdvertiseAsync(Create.Gate(), cts.Token).GetAsyncEnumerator(cts.Token);
            var moving = enumerator.MoveNextAsync();
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await moving);
            await enumerator.DisposeAsync();

            // Assert
            Assert.False(platform.IsIdle);
            Assert.Equal(0, adapter.ReleaseIdleStateCount);

            // Cleanup
            platform.ResolveConnectionTcs(device.Id, Create.Connection(device: device));
            await connecting;
        }

        // The hole a platform computing idleness for itself could not see: a handshake dropped by a
        // deadline or an abandon produces no terminal SDK callback, so nothing told iOS the last
        // reason to hold the session was gone. It waited for MPC's own invitation timeout instead.
        [Fact]
        public void ADroppedHandshake_ReleasesTheIdleState()
        {
            // Arrange
            var adapter = Starting();
            var platform = Create.NearbyTransport(adapter: adapter);
            _ = platform.Handshakes.Register("peer-1", out _, deadline: null, attributedTo: CancellationToken.None);

            // Act
            platform.Handshakes.Drop("peer-1");

            // Assert
            Assert.True(platform.IsIdle);
            Assert.Equal(1, adapter.ReleaseIdleStateCount);
        }

        [Fact]
        public async Task TheLastConnectionReleased_ReleasesTheIdleState()
        {
            // Arrange
            var adapter = Starting();
            var platform = Create.NearbyTransport(adapter: adapter);
            var device = Create.Device("peer-1");
            var tcs = platform.Handshakes.Register(device.Id, out _, deadline: null, attributedTo: CancellationToken.None);
            platform.ResolveConnectionTcs(device.Id, Create.Connection(device: device));
            await tcs.Task;

            Assert.False(platform.IsIdle);

            // Act
            await platform.ReleaseConnectionAsync(device.Id);

            // Assert
            Assert.True(platform.IsIdle);
            Assert.Equal(1, adapter.ReleaseIdleStateCount);
        }

        [Fact]
        public void AConnectionResolvedWithNoHandshake_LeavesTheTableEmpty()
        {
            // Arrange
            var platform = Create.NearbyTransport(adapter: Starting());
            var device = Create.Device("peer-1");

            // Act
            platform.ResolveConnectionTcs(device.Id, Create.Connection(device: device));

            // Assert
            Assert.False(platform.Connections.IsConnected(device.Id));
            Assert.True(platform.IsIdle);
        }

        [Fact]
        public async Task TheSpanEnding_ForgetsEveryPeer()
        {
            // Peer mappings are span state: a handle recorded while browsing means nothing once the
            // browse is over, and on iOS only removal disposes the MCPeerID behind it. They used to
            // live until the process ended.

            // Arrange
            var adapter = Starting();
            var platform = Create.NearbyTransport(adapter: adapter);
            using var cts = new CancellationTokenSource();

            var enumerator = platform.DiscoverAsync(Create.Gate(), cts.Token).GetAsyncEnumerator(cts.Token);
            var moving = enumerator.MoveNextAsync();
            await Wait.UntilAsync(() => !platform.IsIdle);

            platform.PeerLookup.Record("peer-1", "Alice");
            Assert.True(platform.PeerLookup.TryGetDevice("peer-1", out _));

            // Act
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await moving);
            await enumerator.DisposeAsync();

            // Assert
            Assert.False(platform.PeerLookup.TryGetDevice("peer-1", out _));
        }

        [Fact]
        public async Task AStartInFlight_HoldsTheSpanOpen()
        {
            // Arrange
            var running = Create.Gate();
            var release = Create.Gate();
            var adapter = new ScriptedAdapter
            {
                OnStartDiscovery = async _ =>
                {
                    running.TrySetResult();
                    await release.Task;
                },
            };

            var platform = Create.NearbyTransport(adapter: adapter);
            using var cts = new CancellationTokenSource();
            var enumerator = platform.DiscoverAsync(Create.Gate(), cts.Token).GetAsyncEnumerator(cts.Token);
            var moving = enumerator.MoveNextAsync();
            await running.Task;

            // Act
            _ = platform.Handshakes.Register("peer-1", out _, deadline: null, attributedTo: CancellationToken.None);
            platform.Handshakes.Drop("peer-1");

            // Assert
            Assert.False(platform.IsIdle);
            Assert.Equal(0, adapter.ReleaseIdleStateCount);

            // Cleanup
            release.TrySetResult();
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await moving);
            await enumerator.DisposeAsync();
        }

        [Fact]
        public async Task ACancelledFirstStart_ReleasesWhatThePlatformAlreadyMinted()
        {
            // The start never counted as a run, so nothing else would re-test the span — but the
            // platform can already have minted the local peer it was going to advertise under.

            // Arrange
            var running = Create.Gate();
            var adapter = new ScriptedAdapter
            {
                OnStartAdvertising = async ct =>
                {
                    running.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                },
            };

            var platform = Create.NearbyTransport(adapter: adapter);
            using var cts = new CancellationTokenSource();

            var enumerator = platform.AdvertiseAsync(Create.Gate(), cts.Token).GetAsyncEnumerator(cts.Token);
            var moving = enumerator.MoveNextAsync();
            await running.Task;

            // Act
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await moving);

            // Assert
            Assert.True(platform.IsIdle);
            Assert.Equal(1, adapter.ReleaseIdleStateCount);
        }
    }

    /// <summary>
    /// The runtime display name. The transport owns it because the platforms read it at different
    /// moments; moving the effective value only at the idle edge is what makes them agree.
    /// </summary>
    public sealed class RuntimePresence : NearbyTransportTests
    {
        static ScriptedAdapter Starting() => new()
        {
            OnStartAdvertising = static _ => Task.CompletedTask,
            OnStartDiscovery = static _ => Task.CompletedTask,
        };

        [Fact]
        public void StartsAsTheConfiguredName()
        {
            // Arrange & Act
            var platform = Create.NearbyTransport(
                options: new NearbyOptions { ServiceId = "svc", Presence = new() { DisplayName = "Alice" } });

            // Assert
            Assert.Equal("Alice", platform.Presence.DisplayName);
        }

        [Fact]
        public void SetWhileIdle_AppliesAtOnce()
        {
            // Arrange
            var platform = Create.NearbyTransport(
                options: new NearbyOptions { ServiceId = "svc", Presence = new() { DisplayName = "Alice" } });

            // Act
            platform.SetPresence(new() { DisplayName = "Bob" });

            // Assert
            Assert.Equal("Bob", platform.Presence.DisplayName);
        }

        [Fact]
        public async Task SetWhileAdvertising_HoldsUntilTheSpanEnds()
        {
            // The identity a peer connects to cannot change underneath it: iOS cannot rename a live
            // MCPeerID, and Android would advertise under one name and send a connect request under
            // another within the same run.

            // Arrange
            var adapter = Starting();
            var platform = Create.NearbyTransport(
                options: new NearbyOptions { ServiceId = "svc", Presence = new() { DisplayName = "Alice" } },
                adapter: adapter);

            using var cts = new CancellationTokenSource();
            var enumerator = platform.AdvertiseAsync(Create.Gate(), cts.Token).GetAsyncEnumerator(cts.Token);
            var moving = enumerator.MoveNextAsync();
            await Wait.UntilAsync(() => !platform.IsIdle);

            // Act
            platform.SetPresence(new() { DisplayName = "Bob" });

            // Assert
            // Peers still see the old name while the run is up.
            Assert.Equal("Alice", platform.Presence.DisplayName);

            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await moving);
            await enumerator.DisposeAsync();

            Assert.Equal("Bob", platform.Presence.DisplayName);
        }

        [Fact]
        public async Task SetTwiceWhileRunning_TheLastOneWins()
        {
            // Arrange
            var adapter = Starting();
            var platform = Create.NearbyTransport(
                options: new NearbyOptions { ServiceId = "svc", Presence = new() { DisplayName = "Alice" } },
                adapter: adapter);

            using var cts = new CancellationTokenSource();
            var enumerator = platform.AdvertiseAsync(Create.Gate(), cts.Token).GetAsyncEnumerator(cts.Token);
            var moving = enumerator.MoveNextAsync();
            await Wait.UntilAsync(() => !platform.IsIdle);

            // Act
            platform.SetPresence(new() { DisplayName = "Bob" });
            platform.SetPresence(new() { DisplayName = "Carol" });

            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await moving);
            await enumerator.DisposeAsync();

            // Assert
            Assert.Equal("Carol", platform.Presence.DisplayName);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void AnEmptyName_Throws(string? name)
        {
            // Never sanitized silently: on iOS an empty name reaches MCPeerID, which raises a
            // native exception the caller cannot catch.

            // Arrange
            var platform = Create.NearbyTransport();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => platform.SetPresence(new() { DisplayName = name! }));
            Assert.NotEqual(name, platform.Presence.DisplayName);
        }

        [Fact]
        public void AnOverlongName_Throws()
        {
            // Arrange
            var platform = Create.NearbyTransport(
                options: new NearbyOptions { ServiceId = "svc", Presence = new() { DisplayName = "Alice" } });

            // Act & Assert
            // The cap counts UTF-8 bytes, not characters.
            Assert.Throws<ArgumentException>(
                () => platform.SetPresence(new() { DisplayName = new string('x', 64) }));
            Assert.Equal("Alice", platform.Presence.DisplayName);
        }
    }

    /// <summary>
    /// The inbound offer both adapters build. Registration happens inside the accept, so the
    /// handshake is attributed to the accepting caller's token from the start.
    /// </summary>
    public sealed class CreateInboundRequest : NearbyTransportTests
    {
        [Fact]
        public async Task AcceptedHandshake_IsAttributedToTheAcceptingCallersToken()
        {
            // Android used to register on arrival with CancellationToken.None and re-point the
            // entry later. A cancellation raised before that re-point named a token the caller
            // had never seen, so `catch (OperationCanceledException) when (ct == mine)` missed it
            // and the accept surfaced as a foreign cancellation.

            // AcceptCore/RejectCore rather than the public pair: those route through the session
            // gateway, and a request built here was never published to a session.

            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1", "Alice");
            var accepted = new TaskCompletionSource();

            var request = platform.CreateInboundRequest(
                device,
                OfferWindowPolicy.s_default,
                sendAccept: () =>
                {
                    accepted.TrySetResult();

                    return Task.CompletedTask;
                },
                sendReject: () => Task.CompletedTask);

            using var cts = new CancellationTokenSource();
            var accepting = request.AcceptCore(cts.Token);

            await accepted.Task;

            // Act
            await cts.CancelAsync();

            // Assert
            // The accept unwinds as cancellation attributed to the caller, and the ledger
            // entry is gone. Registering with CancellationToken.None instead would leave the
            // entry attributed to a token the caller never held.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accepting);

            Assert.True(cts.IsCancellationRequested);
            Assert.False(platform.Handshakes.TryJoin(device.Id, out _));
        }

        [Fact]
        public async Task Reject_DropsTheRegistrationAndAnswersThePlatform()
        {
            // Arrange
            var platform = Create.NearbyTransport();
            var device = Create.Device("peer-1", "Alice");
            var rejected = false;

            var request = platform.CreateInboundRequest(
                device,
                OfferWindowPolicy.s_default,
                sendAccept: () => Task.CompletedTask,
                sendReject: () =>
                {
                    rejected = true;

                    return Task.CompletedTask;
                });

            // Act
            await request.RejectCore(TestContext.Current.CancellationToken);

            // Assert
            Assert.True(rejected);
            Assert.False(platform.Handshakes.TryJoin(device.Id, out _));
        }
    }
}
