using System.Threading.Channels;
using NSubstitute;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Connections")]
public class NearbyConnectionTests
{
    public sealed class RemoteDevice : NearbyConnectionTests
    {
        [Fact]
        public void ReturnsConstructedDevice()
        {
            // Arrange
            var device = new NearbyDevice("peer-42", NearbyPresence.FromRemote("Bob", default));
            var connection = Create.Connection(device: device);

            // Act
            var result = connection.RemoteDevice;

            // Assert
            Assert.Same(device, result);
        }
    }

    public sealed class AuthenticationCode : NearbyConnectionTests
    {
        [Fact]
        public void ReturnsTheCodeTheHandshakeDerived()
        {
            // Arrange
            var connection = Create.Connection(authenticationCode: "482107");

            // Act
            var result = connection.AuthenticationCode;

            // Assert
            Assert.Equal("482107", result);
        }

        [Fact]
        public void IsEmptyRatherThanNull_WhenThePlatformSuppliedNoCode()
        {
            // Arrange
            var connection = Create.Connection(authenticationCode: null);

            // Act
            var result = connection.AuthenticationCode;

            // Assert
            Assert.Equal("", result);
        }
    }

    public sealed class SendAsyncBytes : NearbyConnectionTests
    {
        [Fact]
        public async Task SendAsync_Bytes_DelegatesToSendBytes()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);
            var payload = new byte[] { 10, 20, 30 };

            // Act
            await connection.SendAsync(payload, TestContext.Current.CancellationToken);

            // Assert
            await platform.Received(1).SendBytesAsync(
                Arg.Is<ReadOnlyMemory<byte>>(data => data.ToArray().SequenceEqual(payload)),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SendAsync_Bytes_ForwardsCancellationToken()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);
            using var cts = new CancellationTokenSource();

            // Act
            await connection.SendAsync(new byte[] { 1 }, cts.Token);

            // Assert
            await platform.Received(1).SendBytesAsync(Arg.Any<ReadOnlyMemory<byte>>(), cts.Token);
        }

        // ReadOnlyMemory<byte> is a struct, so "no bytes" is an empty buffer rather than null, and
        // the send is legal.
        [Fact]
        public async Task SendAsync_EmptyBuffer_IsSent()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);

            // Act
            await connection.SendAsync(ReadOnlyMemory<byte>.Empty, TestContext.Current.CancellationToken);

            // Assert
            await platform.Received(1).SendBytesAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        }

        // The ceiling is public API and callers branch on it, so a silent change is a breaking
        // change. Covered here rather than in the UI suite: the check is on the argument and needs
        // no peer, but SendAsync is an instance method, so on a device the host cannot reach it
        // without an open connection.
        [Fact]
        public async Task SendAsync_PastMaxBytesPayloadLength_Throws()
        {
            // Arrange
            var connection = Create.Connection();
            var oversize = new byte[NearbyConnection.MaxBytesPayloadLength + 1];

            // Act
            var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => connection.SendAsync(oversize, TestContext.Current.CancellationToken));

            // Assert
            Assert.Equal("data", ex.ParamName);
        }

        [Fact]
        public async Task SendAsync_PastMaxBytesPayloadLength_DoesNotReachPlatform()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);

            // Act
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => connection.SendAsync(
                    new byte[NearbyConnection.MaxBytesPayloadLength + 1],
                    TestContext.Current.CancellationToken));

            // Assert
            await platform.DidNotReceive().SendBytesAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SendAsync_APayloadThePeerWouldReadAsAControlFrame_Throws()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);
            var frame = ControlMessage.Encode(ControlMessageType.Disconnect);

            // Act
            var ex = await Assert.ThrowsAsync<ArgumentException>(
                () => connection.SendAsync(frame, TestContext.Current.CancellationToken));

            // Assert
            Assert.Equal("data", ex.ParamName);
            await platform.DidNotReceive().SendBytesAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SendAsync_APayloadWithTheSignatureAndAnyVersion_Throws()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);
            byte[] payload = [.. ControlMessage.Encode(ControlMessageType.Disconnect)[..4], 0xFF, 0x01, 0x00];

            // Act
            await Assert.ThrowsAsync<ArgumentException>(
                () => connection.SendAsync(payload, TestContext.Current.CancellationToken));

            // Assert
            await platform.DidNotReceive().SendBytesAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SendAsync_APayloadShorterThanTheSignature_IsSent()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);
            var payload = ControlMessage.Encode(ControlMessageType.Disconnect)[..3];

            // Act
            await connection.SendAsync(payload, TestContext.Current.CancellationToken);

            // Assert
            await platform.Received(1).SendBytesAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SendAsync_AtMaxBytesPayloadLength_IsSent()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);

            // Act
            await connection.SendAsync(
                new byte[NearbyConnection.MaxBytesPayloadLength],
                TestContext.Current.CancellationToken);

            // Assert
            await platform.Received(1).SendBytesAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        }
    }

    public sealed class SendAsyncFile : NearbyConnectionTests
    {
        [Fact]
        public async Task SendFileAsync_Path_DelegatesToSendFile()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);

            // Act
            await connection.SendFileAsync("/path/to/file.txt", cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            await platform.Received(1).SendFileAsync("/path/to/file.txt", null, Arg.Any<IProgress<NearbyTransferProgress>?>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SendFileAsync_Path_ForwardsProgress()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);
            var progress = new Progress<NearbyTransferProgress>();

            // Act
            await connection.SendFileAsync("/path/to/file.txt", progress, TestContext.Current.CancellationToken);

            // Assert
            await platform.Received(1).SendFileAsync(Arg.Any<string>(), Arg.Is<string?>(name => name == null), progress, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SendFileAsync_NullPath_ThrowsArgumentNullException()
        {
            // Arrange
            var connection = Create.Connection();

            // Act
            async Task act() => await connection.SendFileAsync((string)null!, cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            await Assert.ThrowsAsync<ArgumentNullException>(act);
        }
    }

    public sealed class ReceiveAsync : NearbyConnectionTests
    {

        [Fact]
        public async Task WritePayload_YieldsPayload()
        {
            // Arrange
            var receiveChannel = Channel.CreateUnbounded<NearbyPayload>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            var connection = Create.Connection(receiveChannel: receiveChannel);
            var payload = new NearbyBytesPayload(new byte[] { 1, 2, 3 });
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            // Act
            // Write payload then complete channel so the enumeration terminates
            receiveChannel.Writer.TryWrite(payload);
            receiveChannel.Writer.TryComplete();
            var received = new List<NearbyPayload>();
            await foreach (var item in connection.Inbound.ReadAllAsync(cts.Token))
            {
                received.Add(item);
            }

            // Assert
            Assert.Single(received);
            Assert.Same(payload, received[0]);
        }

        [Fact]
        public async Task MultiplePayloads_YieldsInOrder()
        {
            // Arrange
            var receiveChannel = Channel.CreateUnbounded<NearbyPayload>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            var connection = Create.Connection(receiveChannel: receiveChannel);

            var p1 = new NearbyBytesPayload(new byte[] { 1 });
            var p2 = new NearbyBytesPayload(new byte[] { 2 });
            var p3 = new NearbyBytesPayload(new byte[] { 3 });

            receiveChannel.Writer.TryWrite(p1);
            receiveChannel.Writer.TryWrite(p2);
            receiveChannel.Writer.TryWrite(p3);
            receiveChannel.Writer.TryComplete();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var received = new List<NearbyPayload>();

            // Act
            await foreach (var item in connection.Inbound.ReadAllAsync(cts.Token))
            {
                received.Add(item);
            }

            // Assert
            Assert.Equal(3, received.Count);
            Assert.Same(p1, received[0]);
            Assert.Same(p2, received[1]);
            Assert.Same(p3, received[2]);
        }

        [Fact]
        public async Task CancellationRequested_ThrowsOperationCanceledException()
        {
            // Arrange
            var receiveChannel = Channel.CreateUnbounded<NearbyPayload>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            var connection = Create.Connection(receiveChannel: receiveChannel);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            // Act
            Func<Task> act = async () =>
            {
                await foreach (var _ in connection.Inbound.ReadAllAsync(cts.Token))
                {
                    // drain — cancelled before first item
                }
            };

            // Assert
            // TaskCanceledException is a subclass of OperationCanceledException
            await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        }

        // A ChannelReader does not police a second reader, and the channel is created
        // SingleReader — so single-consumer is a contract the type states and the channel
        // assumes, rather than one guarded at runtime. What matters is that reading drains in
        // order and each payload is handed over once.
        [Fact]
        public async Task Inbound_DrainsInArrivalOrder()
        {
            // Arrange
            var connection = Create.Connection();
            connection.WritePayload(new NearbyBytesPayload(new byte[] { 1 }));
            connection.WritePayload(new NearbyBytesPayload(new byte[] { 2 }));
            connection.CompleteReceive();

            // Act
            var received = new List<byte>();

            await foreach (var payload in connection.Inbound.ReadAllAsync(CancellationToken.None))
            {
                received.Add(((NearbyBytesPayload)payload).Data.Span[0]);
            }

            // Assert
            Assert.Equal([1, 2], received);
        }
    }

    public sealed class CompleteReceive : NearbyConnectionTests
    {
        [Fact]
        public async Task CompletesReceiveEnumerable()
        {
            // Arrange
            var receiveChannel = Channel.CreateUnbounded<NearbyPayload>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            var connection = Create.Connection(receiveChannel: receiveChannel);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            // Start enumerating on a background task
            var received = new List<NearbyPayload>();
            var enumerateTask = Task.Run(async () =>
            {
                await foreach (var item in connection.Inbound.ReadAllAsync(cts.Token))
                {
                    received.Add(item);
                }
            }, cts.Token);

            // Act
            // Simulate platform disconnect completing the receive channel
            connection.CompleteReceive();

            // Assert
            // Enumeration terminates cleanly
            await enumerateTask.WaitAsync(cts.Token);
            Assert.Empty(received);
        }
    }

    public sealed class DisposeAsync : NearbyConnectionTests
    {
        [Fact]
        public async Task CallsDispose()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);

            // Act
            await connection.CloseAsync();

            // Assert
            await platform.Received(1).DisposeAsync();
        }

        [Fact]
        public async Task CompletesReceiveEnumerable()
        {
            // Arrange
            var receiveChannel = Channel.CreateUnbounded<NearbyPayload>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            var connection = Create.Connection(receiveChannel: receiveChannel);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            var enumerateTask = Task.Run(async () =>
            {
                await foreach (var _ in connection.Inbound.ReadAllAsync(cts.Token))
                {
                    // drain — channel completes when connection is disposed
                }
            }, cts.Token);

            // Act
            await connection.CloseAsync();

            // Assert
            await enumerateTask.WaitAsync(cts.Token);
            Assert.True(enumerateTask.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task WritePayload_AfterDispose_DoesNotThrow()
        {
            // Arrange
            var receiveChannel = Channel.CreateUnbounded<NearbyPayload>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            var connection = Create.Connection(receiveChannel: receiveChannel);
            await connection.CloseAsync();

            // Act
            connection.WritePayload(new NearbyBytesPayload(new byte[] { 1, 2, 3 }));

            // Assert
            // Payload silently dropped; channel writer is completed so nothing was queued
            Assert.False(receiveChannel.Reader.TryRead(out _));
        }

        // Pins the retention guarantee the whole late-consumer story rests on: the receive channel is
        // unbounded and WritePayload writes unconditionally, so payloads that arrive long before
        // anything reads Inbound are buffered, not dropped. A consumer that starts late drains
        // the entire backlog from connection-open, which is why the plugin needs no separate
        // payload-replay feature — only a reliable way to hand out the connection.
        [Fact]
        public async Task Inbound_ReadStartedLate_DrainsPayloadsWrittenBeforeItBegan()
        {
            // Arrange
            var connection = Create.Connection();
            connection.WritePayload(new NearbyBytesPayload(new byte[] { 1 }));
            connection.WritePayload(new NearbyBytesPayload(new byte[] { 2 }));
            connection.WritePayload(new NearbyBytesPayload(new byte[] { 3 }));

            // Act
            // The first read happens only now, after all writes
            var received = new List<byte>();
            connection.CompleteReceive();

            await foreach (var payload in connection.Inbound.ReadAllAsync(TestContext.Current.CancellationToken))
            {
                received.Add(((NearbyBytesPayload)payload).Data.Span[0]);
            }

            // Assert
            // Every pre-subscription payload is delivered, in arrival order
            Assert.Equal(3, received.Count);
            Assert.Equal(1, received[0]);
            Assert.Equal(2, received[1]);
            Assert.Equal(3, received[2]);
        }

        [Fact]
        public void PayloadsWritten_CountsWhatArrived()
        {
            // Arrange
            var connection = Create.Connection();

            // Act
            connection.WritePayload(new NearbyBytesPayload(new byte[] { 1 }));
            connection.WritePayload(new NearbyBytesPayload(new byte[] { 2 }));

            // Assert
            Assert.Equal(2, connection.PayloadsWritten);
        }

        [Fact]
        public async Task CloseAsync_DisposesStreamPayloadsNobodyRead()
        {
            // Arrange
            var connection = Create.Connection();
            var unread = new TrackingStream();
#pragma warning disable NEARBY001 // Tests the experimental stream API.
            connection.WritePayload(new NearbyStreamPayload("photo", unread));
#pragma warning restore NEARBY001

            // Act
            await connection.CloseAsync();

            // Assert
            Assert.True(unread.Disposed);
        }

        [Fact]
        public async Task CloseAsync_DeletesStagedFilesNobodyRead()
        {
            // Arrange
            using var temp = new TempDirectory();
            var staged = temp.Touch("photo.jpg");
            var connection = Create.Connection();
            connection.WritePayload(NearbyFilePayload.Staged(staged));

            // Act
            await connection.CloseAsync();

            // Assert
            Assert.False(File.Exists(staged));
        }

        [Fact]
        public async Task CloseAsync_LeavesADeliveredStreamAlone()
        {
            // Arrange
            var connection = Create.Connection();
            var delivered = new TrackingStream();
#pragma warning disable NEARBY001 // Tests the experimental stream API.
            connection.WritePayload(new NearbyStreamPayload("photo", delivered));
#pragma warning restore NEARBY001
            Assert.True(connection.Inbound.TryRead(out _));

            // Act
            await connection.CloseAsync();

            // Assert
            Assert.False(delivered.Disposed);
        }

        [Fact]
        public void WritePayload_AfterCompletion_DisposesTheStream()
        {
            // Arrange
            var connection = Create.Connection();
            var late = new TrackingStream();
            connection.CompleteReceive();

            // Act
#pragma warning disable NEARBY001 // Tests the experimental stream API.
            connection.WritePayload(new NearbyStreamPayload("late", late));
#pragma warning restore NEARBY001

            // Assert
            Assert.True(late.Disposed);
        }

        sealed class TrackingStream : MemoryStream
        {
            public bool Disposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }
    }

    public sealed class Disconnected : NearbyConnectionTests
    {
        [Fact]
        public async Task CompletesWhenCompleteReceiveCalled()
        {
            // Arrange
            var connection = Create.Connection();

            // Act
            connection.CompleteReceive();

            // Assert
            await connection.Closed.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.True(connection.Closed.IsCompleted);
        }

        [Fact]
        public async Task CompletesWhenDisposeAsyncCalled()
        {
            // Arrange
            var connection = Create.Connection();

            // Act
            await connection.CloseAsync();

            // Assert
            Assert.True(connection.Closed.IsCompleted);
        }

        [Fact]
        public async Task IsIdempotentOnDoubleCompleteAndDispose()
        {
            // Arrange
            var connection = Create.Connection();

            // Act
            // Double CompleteReceive and one DisposeAsync; none should throw
            connection.CompleteReceive();
            connection.CompleteReceive();
            await connection.CloseAsync();

            // Assert
            Assert.True(connection.Closed.IsCompleted);
        }

        [Fact]
        public async Task LocalDispose_ReportsDisconnectedByLocal()
        {
            // Arrange
            var connection = Create.Connection();

            // Act
            await connection.CloseAsync();

            // Assert
            Assert.Equal(NearbyEndReason.ClosedLocally, await connection.Closed);
        }

        [Fact]
        public async Task PlatformRelease_ReportsDisconnected()
        {
            // The release path is how a remote close or a link loss reaches the connection.

            // Arrange
            var connection = Create.Connection();

            // Act
            connection.CompleteReceive();

            // Assert
            Assert.Equal(NearbyEndReason.PeerDisconnected, await connection.Closed);
        }

        [Fact]
        public async Task FirstCompletionWins_LocalDisposeBeatsTheReleaseThatFollowsIt()
        {
            // A local dispose triggers the platform release, which completes the same source with
            // Disconnected — the reason recorded first must survive.

            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);
            platform.When(p => p.DisposeAsync().AsTask()).Do(_ => connection.CompleteReceive());

            // Act
            await connection.CloseAsync();

            // Assert
            Assert.Equal(NearbyEndReason.ClosedLocally, await connection.Closed);
        }
    }

    public sealed class DisconnectedToken : NearbyConnectionTests
    {
        [Fact]
        public void IsNotCanceled_WhileConnected()
        {
            // Arrange
            var connection = Create.Connection();

            // Assert
            Assert.False(connection.InboundWorkToken.IsCancellationRequested);
        }

        [Fact]
        public void IsCanceled_AfterCompleteReceive()
        {
            // Arrange
            var connection = Create.Connection();

            // Act
            connection.CompleteReceive();

            // Assert
            Assert.True(connection.InboundWorkToken.IsCancellationRequested);
        }

        [Fact]
        public async Task IsCanceled_AfterDisposeAsync()
        {
            // Arrange
            var connection = Create.Connection();

            // Act
            await connection.CloseAsync();

            // Assert
            Assert.True(connection.InboundWorkToken.IsCancellationRequested);
        }

        // DisconnectedToken is public and consumers hold connection references past teardown, so the
        // backing CancellationTokenSource must NOT be disposed — reading the token after
        // DisposeAsync has to keep working rather than throw ObjectDisposedException.
        [Fact]
        public async Task RemainsReadable_AfterDisposeAsync()
        {
            // Arrange
            var connection = Create.Connection();

            // Act
            await connection.CloseAsync();

            // Assert
            // Reading the token and registering on it must not throw
            var token = connection.InboundWorkToken;
            Assert.True(token.IsCancellationRequested);
            using var registration = token.Register(static () => { });
        }

        [Fact]
        public async Task Register_AfterDisposeAsync_RunsCallbackInline()
        {
            // Arrange
            var connection = Create.Connection();
            await connection.CloseAsync();
            var ranOnRegisteringThread = false;
            var registeringThreadId = Environment.CurrentManagedThreadId;

            // Act
            using var registration = connection.InboundWorkToken.Register(
                () => ranOnRegisteringThread = Environment.CurrentManagedThreadId == registeringThreadId);

            // Assert
            Assert.True(ranOnRegisteringThread);
        }

        [Fact]
        public async Task ComposesIntoLinkedSource_AfterDisposeAsync()
        {
            // Arrange
            var connection = Create.Connection();
            await connection.CloseAsync();
            using var caller = new CancellationTokenSource();

            // Act
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                connection.InboundWorkToken, caller.Token);

            // Assert
            Assert.True(linked.Token.IsCancellationRequested);
        }

        [Fact]
        public async Task ReceiveAsync_ExitsLoop_WhenPeerDisconnectsMidEnumeration()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<NearbyPayload>();
            var connection = Create.Connection(receiveChannel: channel);
            channel.Writer.TryWrite(new NearbyBytesPayload(new byte[] { 1 }));

            // Act
            // Disconnect from inside the loop after the first payload
            var received = new List<NearbyPayload>();
            await foreach (var payload in connection.Inbound.ReadAllAsync(TestContext.Current.CancellationToken))
            {
                received.Add(payload);
                connection.CompleteReceive();
            }

            // Assert
            // The loop terminated without the consumer owning a token at all
            Assert.Single(received);
        }

        // Pins the documented misuse. Passing DisconnectedToken to ReceiveAsync looks natural and is
        // wrong: ReadAllAsync observes cancellation on every iteration, so an already-cancelled token
        // throws and discards buffered payloads — the exact data loss the design must prevent. If
        // this ever stops throwing, the remarks on DisconnectedToken need revisiting.
        [Fact]
        public async Task ReceiveAsync_WithDisconnectedToken_ThrowsAndDiscardsBufferedPayloads()
        {
            // Arrange
            var channel = Channel.CreateUnbounded<NearbyPayload>();
            var connection = Create.Connection(receiveChannel: channel);
            channel.Writer.TryWrite(new NearbyBytesPayload(new byte[] { 1 }));
            connection.CompleteReceive();

            // Act & Assert
            await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            {
                await foreach (var _ in connection.Inbound.ReadAllAsync(connection.InboundWorkToken))
                {
                }
            });
        }
    }

    public sealed class InboundTransfers : NearbyConnectionTests
    {
        [Fact]
        public async Task CompleteInboundTransfer_Failed_ReportsThePositionReached()
        {
            // Arrange
            // A transfer that got to 40 of 100 before dying.
            var connection = Create.Connection();
            var enumerator = connection.InboundTransfersAsync(TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);
            connection.ReportInboundTransfer(new NearbyTransferProgress(7, 40, 100, NearbyTransferStatus.InProgress));
            Assert.True(await enumerator.MoveNextAsync());

            // Act
            connection.CompleteInboundTransfer(7, NearbyTransferStatus.Failed);

            // Assert
            Assert.True(await enumerator.MoveNextAsync());
            var terminal = enumerator.Current;
            Assert.Equal(NearbyTransferStatus.Failed, terminal.Status);
            Assert.Equal(7, terminal.PayloadId);
            Assert.Equal(40, terminal.BytesTransferred);
            Assert.Equal(100, terminal.TotalBytes);
            await enumerator.DisposeAsync();
        }

        [Fact]
        public async Task CompleteInboundTransfer_Completed_ReportsTheFullCount()
        {
            // Arrange
            // The last update landed short of the total, as the platforms' last one often does.
            var connection = Create.Connection();
            var enumerator = connection.InboundTransfersAsync(TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);
            connection.ReportInboundTransfer(new NearbyTransferProgress(7, 90, 100, NearbyTransferStatus.InProgress));
            Assert.True(await enumerator.MoveNextAsync());

            // Act
            connection.CompleteInboundTransfer(7, NearbyTransferStatus.Completed);

            // Assert
            Assert.True(await enumerator.MoveNextAsync());
            var terminal = enumerator.Current;
            Assert.Equal(NearbyTransferStatus.Completed, terminal.Status);
            Assert.Equal(100, terminal.BytesTransferred);
            Assert.Equal(1.0, terminal.Fraction);
            await enumerator.DisposeAsync();
        }

        [Fact]
        public async Task CompleteInboundTransfer_Completed_OfUnknownSize_ReportsTheCountReached()
        {
            // Arrange
            var connection = Create.Connection();
            var enumerator = connection.InboundTransfersAsync(TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);
            connection.ReportInboundTransfer(new NearbyTransferProgress(7, 90, totalBytes: -1, NearbyTransferStatus.InProgress));
            Assert.True(await enumerator.MoveNextAsync());

            // Act
            connection.CompleteInboundTransfer(7, NearbyTransferStatus.Completed);

            // Assert
            Assert.True(await enumerator.MoveNextAsync());
            var terminal = enumerator.Current;
            Assert.Equal(90, terminal.BytesTransferred);
            Assert.Null(terminal.TotalBytes);
            await enumerator.DisposeAsync();
        }

        [Fact]
        public async Task CompleteInboundTransfer_WithNoPriorProgress_StillReportsTheStatus()
        {
            // Arrange
            // A small payload the platform delivered without a single progress callback.
            var connection = Create.Connection();
            var enumerator = connection.InboundTransfersAsync(TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);

            // Act
            connection.CompleteInboundTransfer(7, NearbyTransferStatus.Completed);

            // Assert
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(NearbyTransferStatus.Completed, enumerator.Current.Status);
            Assert.Null(enumerator.Current.Fraction);
            await enumerator.DisposeAsync();
        }

        [Fact]
        public async Task CancelInboundTransfer_InFlight_ReachesThePlatform()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);
            connection.ReportInboundTransfer(new NearbyTransferProgress(7, 40, 100, NearbyTransferStatus.InProgress));

            // Act
            await connection.CancelInboundTransferAsync(7);

            // Assert
            await platform.Received(1).CancelInboundTransferAsync(7);
        }

        [Fact]
        public async Task CancelInboundTransfer_UnknownId_IsIgnored()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);
            connection.ReportInboundTransfer(new NearbyTransferProgress(7, 40, 100, NearbyTransferStatus.InProgress));

            // Act
            await connection.CancelInboundTransferAsync(8);

            // Assert
            await platform.DidNotReceive().CancelInboundTransferAsync(Arg.Any<long>());
        }

        [Theory]
        [InlineData(NearbyTransferStatus.Completed)]
        [InlineData(NearbyTransferStatus.Failed)]
        [InlineData(NearbyTransferStatus.Canceled)]
        public async Task CancelInboundTransfer_AfterTheTransferEnded_IsIgnored(NearbyTransferStatus terminal)
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);
            connection.ReportInboundTransfer(new NearbyTransferProgress(7, 40, 100, NearbyTransferStatus.InProgress));
            connection.CompleteInboundTransfer(7, terminal);

            // Act
            await connection.CancelInboundTransferAsync(7);

            // Assert
            await platform.DidNotReceive().CancelInboundTransferAsync(Arg.Any<long>());
        }

        [Fact]
        public async Task CancelInboundTransfer_AfterClose_IsIgnored()
        {
            // Arrange
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);
            connection.ReportInboundTransfer(new NearbyTransferProgress(7, 40, 100, NearbyTransferStatus.InProgress));
            await connection.CloseAsync();

            // Act
            await connection.CancelInboundTransferAsync(7);

            // Assert
            await platform.DidNotReceive().CancelInboundTransferAsync(Arg.Any<long>());
        }
    }
}
