using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Connections")]
public class OutgoingTransferTests
{
    public sealed class InactivityTimeout : OutgoingTransferTests
    {
        [Fact]
        public void BeforeTheDeadline_TokenIsNotCancelled()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);

            // Act
            time.Advance(TimeSpan.FromSeconds(Create.TransferTimeoutSeconds) - TimeSpan.FromMilliseconds(1));

            // Assert
            Assert.False(transfer.InactivityToken.IsCancellationRequested);
        }

        [Fact]
        public void AtTheDeadline_TokenIsCancelled()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);

            // Act
            time.Advance(TimeSpan.FromSeconds(Create.TransferTimeoutSeconds));

            // Assert
            Assert.True(transfer.InactivityToken.IsCancellationRequested);
        }

        [Fact]
        public void ProgressUpdate_ResetsTheDeadline()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);

            // Act
            for (var i = 0; i < 5; i++)
            {
                time.Advance(TimeSpan.FromSeconds(Create.TransferTimeoutSeconds) - TimeSpan.FromSeconds(1));
                transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress, bytes: i * 10));

                Assert.False(
                    transfer.InactivityToken.IsCancellationRequested,
                    $"Timed out after update {i} despite continuous progress.");
            }

            // Assert
            // Total elapsed time is far past the timeout, but no single gap ever was.
            Assert.True(time.GetUtcNow() > DateTimeOffset.UnixEpoch.AddSeconds(40));
        }

        [Fact]
        public void UpdateAfterTheDeadline_LeavesTheTokenCancelled()
        {
            // A stalled transfer times out, then a late platform callback lands — the transfer has
            // not been disposed yet, so OnUpdate still runs. Rescheduling an already-cancelled
            // source must not throw and must not un-cancel it: the caller has already been handed
            // the timeout exception, and cancellation does not reverse.

            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);
            time.Advance(TimeSpan.FromSeconds(Create.TransferTimeoutSeconds));

            // Act
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress));

            // Assert
            Assert.True(transfer.InactivityToken.IsCancellationRequested);
        }

        [Fact]
        public void InfiniteTimeout_NeverCancels()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time, timeout: System.Threading.Timeout.InfiniteTimeSpan);

            // Act
            time.Advance(TimeSpan.FromDays(1));

            // Assert
            Assert.False(transfer.InactivityToken.IsCancellationRequested);
        }

        [Fact]
        public void TokenCapturedBeforeAnUpdate_DoesNotFireOnTheOriginalDeadline()
        {
            // Both platforms read InactivityToken exactly once, into a linked source, and then
            // await. They never re-read it. So a reset must not let the captured token fire on the
            // deadline it was created with, or a transfer making good progress aborts.

            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);
            var capturedEarly = transfer.InactivityToken;

            // Act
            time.Advance(TimeSpan.FromSeconds(9));
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress));
            time.Advance(TimeSpan.FromSeconds(5)); // past the original 10s deadline

            // Assert
            Assert.False(
                capturedEarly.IsCancellationRequested,
                "The reset must move the deadline, or a progressing transfer aborts.");
        }

        [Fact]
        public void TokenCapturedBeforeAnUpdate_StillFiresOnTheResetDeadline()
        {
            // The other half of the rule above. Moving the deadline must not retire the captured
            // token altogether: the platform code is awaiting on it, so a token that can never
            // fire again turns a stalled transfer into a permanent hang. SendAsync is bounded by
            // TransferInactivityTimeout.
            //
            // Swapping in a fresh source and disposing the old one would satisfy the sibling test
            // above by neutralising the captured token rather than by rescheduling it. That is why
            // both halves are asserted, and separately.

            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);
            var capturedEarly = transfer.InactivityToken;

            // Act
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress));
            time.Advance(TimeSpan.FromSeconds(Create.TransferTimeoutSeconds));

            // Assert
            Assert.True(
                capturedEarly.IsCancellationRequested,
                "A stalled transfer must still time out on the token the platform captured.");
        }
    }

    public sealed class TerminalStates : OutgoingTransferTests
    {
        [Fact]
        public async Task Success_CompletesTheTask()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);

            // Act
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.Completed));

            // Assert
            await transfer.Completion;
            Assert.True(transfer.Completion.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task Failure_FaultsTheTask()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);

            // Act
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.Failed));

            // Assert
            var ex = await Assert.ThrowsAsync<NearbyTransferException>(() => transfer.Completion);
            Assert.Contains("1", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Canceled_CancelsTheTask()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);

            // Act
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.Canceled));

            // Assert
            await Assert.ThrowsAsync<TaskCanceledException>(() => transfer.Completion);
        }

        [Fact]
        public void InProgress_LeavesTheTaskPending()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);

            // Act
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress));

            // Assert
            Assert.False(transfer.Completion.IsCompleted);
        }

        [Fact]
        public async Task FirstTerminalStatusWins()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);

            // Act
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.Completed));
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.Failed));
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.Canceled));

            // Assert
            await transfer.Completion;
            Assert.True(transfer.Completion.IsCompletedSuccessfully);
        }
    }

    public sealed class ProgressReporting : OutgoingTransferTests
    {
        [Fact]
        public void EveryUpdate_IsForwardedInOrder()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var recorder = new RecordingProgress();
            using var transfer = Create.Transfer(time, recorder);

            // Act
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress, bytes: 10));
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress, bytes: 50));
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.Completed, bytes: 100));

            // Assert
            Assert.Equal(3, recorder.Reports.Count);
            Assert.Equal(
                new long[] { 10, 50, 100 },
                recorder.Reports.Select(r => r.BytesTransferred).ToArray());
        }

        [Fact]
        public void NullProgress_IsTolerated()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time, progress: null);

            // Act
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.Completed));

            // Assert
            Assert.True(transfer.Completion.IsCompletedSuccessfully);
        }
    }

    public sealed class Disposal : OutgoingTransferTests
    {
        [Fact]
        public void Dispose_IsIdempotent()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var transfer = Create.Transfer(time);

            // Act
            transfer.Dispose();
            transfer.Dispose();
        }

        [Fact]
        public void DisposeAfterTimeout_DoesNotThrow()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var transfer = Create.Transfer(time);

            // Act
            time.Advance(TimeSpan.FromSeconds(Create.TransferTimeoutSeconds));
            transfer.Dispose();
        }
    }

    // The terminal shape both adapters share. The ladder is tested here against the state it
    // branches on, not only through the device suites.
    public sealed class AwaitAsync : OutgoingTransferTests
    {
        [Fact]
        public async Task WhenTheCompletionSucceeds_ReportsNothingTerminalItself()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);

            // Act
            await transfer.AwaitAsync(
                Task.CompletedTask,
                cancelPlatformTransfer: () => { },
                TestContext.Current.CancellationToken);

            // Assert
            // The caller reports Completed; the ladder only handles the failure paths.
            Assert.False(transfer.Completion.IsCompleted);
        }

        [Fact]
        public async Task WhenTheCallerCancels_ReportsCanceledAndCancelsThePlatform()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var progress = new RecordingProgress();
            using var transfer = Create.Transfer(time, progress);
            using var cts = new CancellationTokenSource();
            var cancelled = false;

            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress, bytes: 80));
            await cts.CancelAsync();

            // Act
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => transfer.AwaitAsync(
                    new TaskCompletionSource().Task,
                    cancelPlatformTransfer: () => cancelled = true,
                    cts.Token));

            // Assert
            // Cancelled at the position it reached, not snapped back to zero.
            Assert.True(cancelled);
            var terminal = progress.Reports[^1];
            Assert.Equal(NearbyTransferStatus.Canceled, terminal.Status);
            Assert.Equal(80, terminal.BytesTransferred);
        }

        [Fact]
        public async Task WhenTheTransferStalls_ThrowsTheTimeoutAndReportsFailed()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var progress = new RecordingProgress();
            using var transfer = Create.Transfer(time, progress);

            var pending = transfer.AwaitAsync(
                new TaskCompletionSource().Task,
                cancelPlatformTransfer: () => { },
                TestContext.Current.CancellationToken);

            // Act
            // Advance past the inactivity deadline with no update.
            time.Advance(TimeSpan.FromSeconds(Create.TransferTimeoutSeconds + 1));

            // Assert
            await Assert.ThrowsAsync<NearbyTransferTimeoutException>(() => pending);
            Assert.Equal(NearbyTransferStatus.Failed, progress.Reports[^1].Status);
        }

        [Fact]
        public async Task WhenTheCompletionFaults_WrapsItAsATransferFailure()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var progress = new RecordingProgress();
            using var transfer = Create.Transfer(time, progress);
            var boom = new InvalidOperationException("the SDK refused the send");

            // Act
            var ex = await Assert.ThrowsAsync<NearbyTransferException>(
                () => transfer.AwaitAsync(
                    Task.FromException(boom),
                    cancelPlatformTransfer: () => { },
                    TestContext.Current.CancellationToken));

            // Assert
            // The caller sees the library's type, with the SDK's as the cause.
            Assert.Same(boom, ex.InnerException);
            Assert.Equal(NearbyTransferStatus.Failed, progress.Reports[^1].Status);
        }

        [Fact]
        public async Task WhenThePlatformReportsCanceled_ThrowsTheCancelAndKeepsCanceledLast()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var progress = new RecordingProgress();
            using var transfer = Create.Transfer(time, progress);
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress, bytes: 22));

            var pending = transfer.AwaitAsync(
                transfer.Completion,
                cancelPlatformTransfer: () => { },
                TestContext.Current.CancellationToken);

            // Act
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.Canceled, bytes: 22));

            // Assert
            await Assert.ThrowsAsync<NearbyTransferCanceledException>(() => pending);
            Assert.Equal(NearbyTransferStatus.Canceled, progress.Reports[^1].Status);
            Assert.Equal(22, progress.Reports[^1].BytesTransferred);
        }

        [Fact]
        public async Task WhenTheCompletionFaultsWithANearbyException_PassesItThrough()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);
            var mine = new NearbyTransferException("already ours");

            // Act
            var ex = await Assert.ThrowsAsync<NearbyTransferException>(
                () => transfer.AwaitAsync(
                    Task.FromException(mine),
                    cancelPlatformTransfer: () => { },
                    TestContext.Current.CancellationToken));

            // Assert
            Assert.Same(mine, ex);
        }
    }

    public sealed class LastProgress : OutgoingTransferTests
    {
        [Fact]
        public void BeforeAnyUpdate_IsZeroOfUnknown()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);

            // Act
            var last = transfer.LastProgress;

            // Assert
            Assert.Equal(0, last.BytesTransferred);
            Assert.Null(last.TotalBytes);
        }

        [Fact]
        public void AfterAnUpdate_CarriesThatUpdatesCounts()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);

            // Act
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress, bytes: 80));

            // Assert
            var last = transfer.LastProgress;
            Assert.Equal(80, last.BytesTransferred);
            Assert.Equal(100, last.TotalBytes);
        }

        [Fact]
        public void AfterSeveralUpdates_CarriesTheMostRecent()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var transfer = Create.Transfer(time);
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress, bytes: 10));

            // Act
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress, bytes: 60));

            // Assert
            Assert.Equal(60, transfer.LastProgress.BytesTransferred);
        }

        // A late platform callback lands after the caller's finally disposed the transfer. OnUpdate
        // is a no-op then, so the recorded position must stay where it was rather than move.
        [Fact]
        public void UpdateAfterDispose_LeavesTheRecordedPositionAlone()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var transfer = Create.Transfer(time);
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress, bytes: 40));
            transfer.Dispose();

            // Act
            transfer.OnUpdate(Create.ProgressUpdate(NearbyTransferStatus.InProgress, bytes: 90));

            // Assert
            Assert.Equal(40, transfer.LastProgress.BytesTransferred);
        }
    }
}
