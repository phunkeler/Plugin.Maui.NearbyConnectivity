namespace Plugin.Maui.NearbyConnectivity;

// Tracks a single outgoing file transfer, routing progress through a shared IProgress and
// completing an awaitable task at a terminal state. Timeout.InfiniteTimeSpan disables the
// inactivity deadline.
sealed class OutgoingTransfer(
    string deviceId,
    long payloadId,
    IProgress<NearbyTransferProgress>? progress,
    TimeSpan inactivityTimeout,
    TimeProvider timeProvider,
    ILogger logger) : IDisposable
{
    readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Lock _gate = new();
    readonly CancellationTokenSource _inactivityCts = new(inactivityTimeout, timeProvider);
    bool _disposed;
    (long BytesTransferred, long? TotalBytes) _lastProgress;

    internal Task Completion => _tcs.Task;

    // Lets a caller report a terminal status against the position the transfer actually reached. A
    // cancel at 80% of a large file should leave a bound progress bar at 80%, not snap it to zero.
    internal (long BytesTransferred, long? TotalBytes) LastProgress
    {
        get
        {
            lock (_gate)
            {
                return _lastProgress;
            }
        }
    }

    // Canceled when no update arrives within the inactivity timeout. Reset on every OnUpdate.
    internal CancellationToken InactivityToken => _inactivityCts.Token;

    // Reschedules the deadline with CancelAfter rather than replacing the source. Both platforms
    // read InactivityToken exactly once, into a linked source, then await — they never re-read it.
    // Rescheduling keeps token identity stable, so the deadline both moves and still fires;
    // replacing the source would dispose the old one and kill the timer behind the token the
    // platform already captured.
    //
    // A no-op once disposed: platform callbacks arrive on their own thread and can land after the
    // finally in the adapter's SendFileAsync has disposed this transfer. Without the guard,
    // CancelAfter would throw ObjectDisposedException onto that callback thread.
    internal void OnUpdate(NearbyTransferProgress transferProgress)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _lastProgress = (transferProgress.BytesTransferred, transferProgress.TotalBytes);
            _inactivityCts.CancelAfter(inactivityTimeout);
        }

        progress?.Report(transferProgress);

        switch (transferProgress.Status)
        {
            case NearbyTransferStatus.Completed:
                _tcs.TrySetResult();
                break;
            case NearbyTransferStatus.Failed:
                _tcs.TrySetException(
                    new NearbyTransferException($"Transfer {transferProgress.PayloadId} failed."));
                break;
            case NearbyTransferStatus.Canceled:
                _tcs.TrySetCanceled();
                break;
        }
    }

    internal void Complete(NearbyTransferStatus status)
    {
        var (bytesTransferred, totalBytes) = LastProgress;

        OnUpdate(new NearbyTransferProgress(
            payloadId,
            bytesTransferred: status is NearbyTransferStatus.Completed
                ? totalBytes ?? bytesTransferred
                : bytesTransferred,
            totalBytes,
            status));
    }

    // The one catch ladder for an outbound file transfer, so the platforms cannot drift. A foreign
    // OperationCanceledException becomes a transfer failure unless the platform reported Canceled,
    // which already reached the progress provider.
    internal async Task AwaitAsync(
        Task completion,
        Action cancelPlatformTransfer,
        CancellationToken cancellationToken)
    {
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, InactivityToken);

            using var ctr = linkedCts.Token.UnsafeRegister(
                static state => ((Action?)state)?.Invoke(),
                cancelPlatformTransfer);

            await completion.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Complete(NearbyTransferStatus.Canceled);
            throw;
        }
        catch (OperationCanceledException) when (InactivityToken.IsCancellationRequested)
        {
            Complete(NearbyTransferStatus.Failed);
            NearbyLog.SendFileStalled(logger, deviceId, inactivityTimeout.TotalSeconds);

            throw new NearbyTransferTimeoutException(
                $"Transfer stalled: no progress received for {inactivityTimeout}.");
        }
        catch (OperationCanceledException ex) when (Completion.IsCanceled)
        {
            throw new NearbyTransferCanceledException(
                $"The transfer to device '{deviceId}' was canceled by the other device or the platform.", ex);
        }
        catch (Exception ex) when (ex is not NearbyException)
        {
            Complete(NearbyTransferStatus.Failed);
            NearbyLog.SendFileFailed(logger, deviceId, ex);

            throw new NearbyTransferException(
                $"Failed to send file to device '{deviceId}'.", ex);
        }
        finally
        {
            // A terminal platform update can fault the completion after the caller already left
            // the await on a catch path above, leaving the fault unobserved and surfacing later
            // on the finalizer thread. Observing it here retires that.
            _ = Completion.Exception;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _inactivityCts.Dispose();
        }
    }

}
