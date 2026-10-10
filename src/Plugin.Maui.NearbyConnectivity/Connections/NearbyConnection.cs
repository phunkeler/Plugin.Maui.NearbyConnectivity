using System.Threading.Channels;

namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Represents an established connection to a nearby device, over which payloads can be sent and
/// received.
/// </summary>
/// <remarks>
/// <para>
/// Obtain an instance from <see cref="NearbyDevice.ConnectAsync(CancellationToken)"/>,
/// <see cref="NearbyConnectionRequest.AcceptAsync(CancellationToken)"/>, or
/// <see cref="NearbyDevice.Connection"/>. All three return the same instance for a given remote
/// device.
/// </para>
/// <para>
/// <b>Deliberately not disposable.</b> The session owns the connection and every holder shares one
/// instance, so a <c>using</c> block would let whichever holder exited first disconnect everyone
/// else, a bug with no compile-time signal, since the code reads exactly like correct C#. Closing
/// is a network operation rather than a resource-cleanup one, so it is named as one:
/// <see cref="CloseAsync"/>, which any holder may call and which closes the
/// connection for all of them. Shaped after <c>ClientWebSocket</c>, where the same distinction
/// applies.
/// </para>
/// </remarks>
/// <seealso href="https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/payloads.html">Send and receive payloads</seealso>
/// <seealso cref="INearbyConnectivity"/>
/// <seealso cref="NearbyDevice"/>
public sealed class NearbyConnection
{
    /// <summary>
    /// Gets the largest byte payload <see cref="SendAsync(ReadOnlyMemory{byte}, CancellationToken)"/>
    /// accepts.
    /// </summary>
    /// <remarks>
    /// One cap on every platform, so a payload that sends on one sends on the other. It is the
    /// lowest any supported backend accepts rather than the most any one of them could carry. Send
    /// anything larger as a file.
    /// </remarks>
    public const int MaxBytesPayloadLength = 32 * 1024;

    readonly Channel<NearbyPayload> _receiveChannel;
    readonly IPlatformConnection _platformConnection;

    readonly TaskCompletionSource<NearbyEndReason> _closedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Broadcast<NearbyTransferProgress> _inboundTransfers = new();
    readonly ConcurrentDictionary<long, (long BytesTransferred, long? TotalBytes)> _inboundPositions = [];

    CancellationTokenSource? _closedCts;
    int _closeGuard;
    int _payloadsWritten;

    internal NearbyConnection(
        NearbyDevice remoteDevice,
        Channel<NearbyPayload> receiveChannel,
        IPlatformConnection platformConnection,
        string? authenticationCode = null)
    {
        RemoteDevice = remoteDevice;
        _receiveChannel = receiveChannel;
        _platformConnection = platformConnection;
        AuthenticationCode = authenticationCode ?? "";
    }

    /// <summary>
    /// Gets the remote device this connection is established with.
    /// </summary>
    /// <value>The device on the other end of the connection.</value>
    public NearbyDevice RemoteDevice { get; }

    /// <summary>
    /// Gets a six-digit code derived from the platform's handshake, for the two people to compare
    /// out loud before they trust the connection.
    /// </summary>
    /// <value>
    /// Six ASCII digits, such as <c>"482107"</c>, identical on both ends of the same connection.
    /// Empty when the platform handshake produced nothing to derive a code from; the plugin logs a
    /// warning, and the two people have nothing to compare.
    /// </value>
    /// <remarks>
    /// <para>
    /// Encryption alone proves that nobody can read the traffic, not who is on the other end: a
    /// machine in the middle can hold one encrypted connection to each person and relay between
    /// them. It cannot make the two codes match, because each is derived from the handshake it
    /// terminated. Two people who read the same six digits to each other have ruled that out; two
    /// who read different ones should close the connection.
    /// </para>
    /// <para>
    /// The comparison has to happen over a channel the relay does not control — out loud, across a
    /// table, or over a call where the voice is recognised. Comparing the codes by sending them
    /// over this connection proves nothing.
    /// </para>
    /// <para>
    /// Nothing enforces the check. The plugin derives and shows the code; whether a connection may
    /// carry payloads before somebody confirms it is the application's decision.
    /// </para>
    /// <para>
    /// The derivation is an implementation detail and may change between releases; both ends of a
    /// connection run the same build, so only the comparison is guaranteed, never a specific value.
    /// </para>
    /// </remarks>
    /// <seealso href="https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/security.html">Security</seealso>
    public string AuthenticationCode { get; }

    /// <summary>
    /// Gets whether this connection is still open.
    /// </summary>
    /// <value>
    /// <see langword="true"/> until the connection ends, from either side and for any reason.
    /// </value>
    /// <remarks>
    /// A snapshot. The connection can end between reading this and acting on the result, so a send
    /// on a connection this reported open can still fail. Treat it as a display value, and let
    /// <see cref="SendAsync(ReadOnlyMemory{byte}, CancellationToken)"/> report the authoritative
    /// answer.
    /// </remarks>
    public bool IsOpen => !_closedTcs.Task.IsCompleted;

    /// <summary>
    /// Gets a task that completes once this connection ends, carrying why.
    /// </summary>
    /// <value>
    /// A task that completes when the connection ends, from either side and for any reason.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Four of the ten <see cref="NearbyEndReason"/> values reach here:</b>
    /// <see cref="NearbyEndReason.ClosedLocally"/>,
    /// <see cref="NearbyEndReason.PeerDisconnected"/>,
    /// <see cref="NearbyEndReason.SessionStopped"/> and
    /// <see cref="NearbyEndReason.AppSuspended"/>. The rest describe a handshake that never opened
    /// or a device leaving the device set, neither of which this task reports.
    /// </para>
    /// <para>
    /// Safe to await alongside a read of
    /// <see cref="INearbyConnectivity.PayloadsAsync(CancellationToken)"/>. It does not consume the
    /// payload stream. <b>Never faults or is canceled:</b> a dropped connection is
    /// not an error at this boundary, so this task always completes successfully. Continuations are
    /// queued to the thread pool, never run inline on the platform callback that ended the
    /// connection.
    /// </para>
    /// </remarks>
    public Task<NearbyEndReason> Closed => _closedTcs.Task;

    internal ChannelReader<NearbyPayload> Inbound => _receiveChannel.Reader;

    /// <summary>
    /// Watches the progress of inbound file transfers on this connection.
    /// </summary>
    /// <param name="cancellationToken">A token to end the enumeration.</param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> yielding each progress snapshot in order, for every
    /// inbound transfer on this connection. The last snapshot for a transfer carries a terminal
    /// <see cref="NearbyTransferProgress.Status"/>: <see cref="NearbyTransferStatus.Completed"/>
    /// once the payload has been delivered, or <see cref="NearbyTransferStatus.Failed"/> or
    /// <see cref="NearbyTransferStatus.Canceled"/> when it never will be.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A stream rather than a settable <c>IProgress</c> property, so it follows the same rule as
    /// everything else that changes over time here, and so several components can watch
    /// independently: each enumeration gets its own feed.
    /// </para>
    /// <para>
    /// Outbound progress is a parameter on each <c>SendAsync</c> overload instead, because the
    /// caller starting the transfer already knows which provider to use. An inbound transfer begins
    /// on a platform callback before any caller could supply one.
    /// </para>
    /// <para>
    /// Snapshots arrive on a thread-pool thread; see the thread-safety notes on
    /// <see cref="INearbyConnectivity"/>.
    /// </para>
    /// </remarks>
    public IAsyncEnumerable<NearbyTransferProgress> InboundTransfersAsync(CancellationToken cancellationToken = default)
        => _inboundTransfers.Stream(cancellationToken);

    /// <summary>
    /// Sends raw bytes to the remote device.
    /// </summary>
    /// <param name="data">The bytes to send.</param>
    /// <param name="cancellationToken">A token to abandon the send.</param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous operation. The task completes once the
    /// bytes are handed off to the platform.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Completing is not a delivery receipt.</b> It means the platform accepted the bytes for
    /// sending. The peer may have left range immediately afterwards, and nothing raises an
    /// error. This differs from <c>SendFileAsync</c>, which completes when the transfer itself
    /// finished.
    /// </para>
    /// <para>
    /// To know the peer received something, acknowledge it in your own protocol: send an
    /// identifier, and have the peer send it back once it has stored the work. That is also the
    /// only acknowledgement that means the receiving application kept it, which no transport-level
    /// signal can promise. See the payloads guide.
    /// </para>
    /// <para>
    /// Takes a <see cref="ReadOnlyMemory{T}"/> rather than an array, so the caller keeps ownership
    /// of the buffer and can send a slice without slicing into a new array first. Both platform
    /// SDKs need contiguous native memory, so the bytes are copied once at that boundary.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled.
    /// </exception>
    /// <exception cref="NearbyTransferException">
    /// The platform returned an error for the send.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="data"/> is longer than <see cref="MaxBytesPayloadLength"/> bytes.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="data"/> starts with the 4-byte signature of the plugin's control frames, so
    /// the remote device would consume or drop it and not deliver it.
    /// </exception>
    /// <exception cref="NearbyException">
    /// The remote device disconnected before this call, so no active connection remains to send on.
    /// </exception>
    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (data.Length > MaxBytesPayloadLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(data),
                data.Length,
                $"A byte payload may be at most {MaxBytesPayloadLength} bytes. Send larger data as a " +
                $"file with {nameof(SendFileAsync)}, which streams rather than buffering.");
        }

        if (ControlMessage.HasSignature(data.Span))
        {
            throw new ArgumentException(
                "A byte payload must not start with the signature of the plugin's control frames. The " +
                "remote device would consume it and not deliver it. Change the first bytes of the payload.",
                nameof(data));
        }

        return _platformConnection.SendBytesAsync(data, cancellationToken);
    }

    /// <summary>
    /// Sends the file at the specified path to the remote device.
    /// </summary>
    /// <param name="path">
    /// The file to send: a file-system path on every platform, or a <c>content://</c> URI on
    /// Android.
    /// </param>
    /// <param name="progress">
    /// An optional provider that receives progress updates for the outgoing transfer, or
    /// <see langword="null"/> to ignore progress. <b>Reported on the platform SDK's own callback
    /// thread</b>, so pass a <see cref="Progress{T}"/>, which marshals to the thread it was
    /// constructed on, rather than a hand-written <see cref="IProgress{T}"/> that touches the
    /// user interface or blocks, which would stall the SDK's callback.
    /// </param>
    /// <param name="cancellationToken">A token to abandon the transfer.</param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous operation. The task completes once the
    /// transfer finishes.
    /// </returns>
    /// <remarks>
    /// The lower-level of the two file overloads: it sends what the path names, with no staging.
    /// Use it whenever the application already holds a real, readable file-system path: a file it
    /// wrote itself, one downloaded to its cache, one produced by a background job. Prefer
    /// <see cref="SendFileAsync(FileResult, IProgress{NearbyTransferProgress}, CancellationToken)"/>
    /// only for a picker result, whose path is not always real.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled.
    /// </exception>
    /// <exception cref="NearbyTransferTimeoutException">
    /// No transfer progress was reported within
    /// <see cref="NearbyOptions.FileTransferInactivityTimeout"/>.
    /// </exception>
    /// <exception cref="NearbyTransferCanceledException">
    /// The other device canceled the transfer, or the platform did.
    /// </exception>
    /// <exception cref="NearbyTransferException">
    /// The file could not be sent: for example, the path does not identify a readable file, or the
    /// platform returned an error for the send.
    /// </exception>
    /// <exception cref="NearbyException">
    /// The remote device disconnected before this call, so no active connection remains to send on.
    /// </exception>
    public Task SendFileAsync(
        string path,
        IProgress<NearbyTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _platformConnection.SendFileAsync(path, null, progress, cancellationToken);
    }

    /// <summary>
    /// Sends the specified file to the remote device.
    /// </summary>
    /// <param name="file">The file to send.</param>
    /// <param name="progress">
    /// An optional provider that receives progress updates for the outgoing transfer, or
    /// <see langword="null"/> to ignore progress. <b>Reported on the platform SDK's own callback
    /// thread</b>, so pass a <see cref="Progress{T}"/> rather than a hand-written
    /// <see cref="IProgress{T}"/> that touches the user interface or blocks.
    /// </param>
    /// <param name="cancellationToken">A token to abandon the transfer.</param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous operation. The task completes once the
    /// transfer finishes.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Accepts the same <see cref="FileResult"/> type that <see cref="NearbyFilePayload"/> exposes,
    /// so a received file can be forwarded without conversion.
    /// </para>
    /// <para>
    /// A <see cref="FileResult"/> from a picker does not always carry a readable file-system path:
    /// on iOS <c>FullPath</c> may be a bare file name. When that is the case, the file is copied to
    /// a temporary location through its stream, sent from there, and the temporary copy is deleted
    /// once the transfer ends. Pass a picker result directly; no staging of your own is needed.
    /// </para>
    /// <para>
    /// The receiver sees <see cref="FileBase.FileName"/>, not the name of the file on disk. A
    /// picker's <c>FullPath</c> is often a temporary copy with a generated name.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="file"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled.
    /// </exception>
    /// <exception cref="NearbyTransferTimeoutException">
    /// No transfer progress was reported within
    /// <see cref="NearbyOptions.FileTransferInactivityTimeout"/>.
    /// </exception>
    /// <exception cref="NearbyTransferCanceledException">
    /// The other device canceled the transfer, or the platform did.
    /// </exception>
    /// <exception cref="NearbyTransferException">
    /// The platform returned an error for the send.
    /// </exception>
    /// <exception cref="NearbyException">
    /// The remote device disconnected before this call, so no active connection remains to send on.
    /// </exception>
    public Task SendFileAsync(
        FileResult file,
        IProgress<NearbyTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (File.Exists(file.FullPath))
        {
            return _platformConnection.SendFileAsync(
                file.FullPath,
                file.FileName,
                progress,
                cancellationToken);
        }

        return SendStagedAsync();

        async Task SendStagedAsync()
        {
            var staged = await StageToTempAsync(
                file.OpenReadAsync,
                file.FileName,
                cancellationToken).ConfigureAwait(false);

            try
            {
                await _platformConnection.SendFileAsync(
                    staged,
                    file.FileName,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (Path.GetDirectoryName(staged) is { } directory)
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Never widen this catch — a failure to send must still surface.
                }
            }
        }
    }

    /// <summary>
    /// Opens a named one-way byte stream to the remote device, for unknown-length or live data.
    /// </summary>
    /// <param name="name">
    /// The stream's name, shown to the remote application. At most 1024 UTF-8 bytes.
    /// </param>
    /// <param name="cancellationToken">A token to abandon opening the stream.</param>
    /// <returns>
    /// The writable stream. Dispose it to end the stream. The remote reader then observes the
    /// end of its half. A dropped connection ends the stream from either side.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Experimental.</b> Streams have not been tested between two devices, and their behavior and
    /// API can change in any release. Code that uses this method gets build error <c>NEARBY001</c>.
    /// To opt in, add <c>NEARBY001</c> to <c>NoWarn</c> in the project file, or suppress it with
    /// <c>#pragma warning disable NEARBY001</c>.
    /// </para>
    /// <para>
    /// The remote side receives a <see cref="NearbyStreamPayload"/>, the readable half plus this
    /// name, through <see cref="INearbyConnectivity.PayloadsAsync(CancellationToken)"/>. Both platforms
    /// carry this natively: a stream payload on Android, a named MultipeerConnectivity stream on
    /// iOS. The name arrives with the stream on both.
    /// Writes are not bounded by <see cref="NearbyOptions.FileTransferInactivityTimeout"/>: the
    /// application owns the stream's lifetime and pacing.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> exceeds 1024 UTF-8 bytes, the limit the name travels under.
    /// </exception>
    /// <exception cref="NearbyException">
    /// The remote device disconnected before this call, so no active connection remains.
    /// </exception>
    /// <exception cref="NearbyTransferException">The platform returned an error opening the stream.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled.
    /// </exception>
    [Experimental("NEARBY001", UrlFormat = "https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/payloads.html#open-a-stream")]
    public Task<Stream> OpenStreamAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ControlMessage.ThrowIfStreamNameTooLong(name, nameof(name));

        return _platformConnection.OpenStreamAsync(name, cancellationToken);
    }

    /// <summary>
    /// Cancels one inbound file transfer on this connection. The connection stays open.
    /// </summary>
    /// <param name="payloadId">
    /// The <see cref="NearbyTransferProgress.PayloadId"/> that
    /// <see cref="InboundTransfersAsync(CancellationToken)"/> reported for the transfer.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous operation. The task completes once the
    /// cancel is handed to the platform, not when the transfer stops.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Use it to refuse a file that is larger than your app expects: watch
    /// <see cref="NearbyTransferProgress.TotalBytes"/> and cancel that transfer, instead of closing
    /// the connection.
    /// </para>
    /// <para>
    /// The result arrives on <see cref="InboundTransfersAsync(CancellationToken)"/>: a terminal
    /// <see cref="NearbyTransferStatus.Canceled"/> snapshot when the cancel took effect, or
    /// <see cref="NearbyTransferStatus.Completed"/> when the transfer finished first, in which case
    /// the payload is delivered as usual. The sender's <c>SendFileAsync</c> throws
    /// <see cref="NearbyTransferCanceledException"/>.
    /// </para>
    /// <para>
    /// A transfer can end between a progress snapshot and this call, so an id that is unknown,
    /// belongs to a transfer that already ended, or arrives after the connection closed is ignored.
    /// The call never throws because of the state of the transfer.
    /// </para>
    /// <para>
    /// Takes no <see cref="CancellationToken"/>, like <see cref="CloseAsync"/>: the platform call it
    /// makes cannot be interrupted.
    /// </para>
    /// </remarks>
    public Task CancelInboundTransferAsync(long payloadId)
        => IsOpen && _inboundPositions.ContainsKey(payloadId)
            ? _platformConnection.CancelInboundTransferAsync(payloadId)
            : Task.CompletedTask;

    /// <summary>
    /// Closes this connection.
    /// </summary>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous operation. The task completes once the
    /// disconnect has been signaled to the platform and any in-flight inbound work for this
    /// connection has drained or timed out.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Closes it for every holder</b>, not just the caller. There is one connection per device
    /// and one instance representing it, so closing is a network operation with a remote effect,
    /// not the release of a local handle. This is the same contract as
    /// <see cref="NearbyDevice.DisconnectAsync"/>, which reaches the same
    /// connection by device id.
    /// </para>
    /// <para>
    /// Idempotent: a second call performs no additional work. <see cref="Closed"/> completes with
    /// <see cref="NearbyEndReason.ClosedLocally"/>.
    /// </para>
    /// <para>
    /// Takes no <see cref="CancellationToken"/>, like the stop methods on
    /// <see cref="INearbyConnectivity"/>: teardown is work the caller wants finished, and the drain
    /// it waits for is already bounded internally.
    /// </para>
    /// </remarks>
    public Task CloseAsync() => CloseForAsync(NearbyEndReason.ClosedLocally);

    internal async Task CloseForAsync(NearbyEndReason reason)
    {
        if (Interlocked.Exchange(ref _closeGuard, 1) != 0)
        {
            return;
        }

        CompleteReceive(reason);
        await _platformConnection.DisposeAsync().ConfigureAwait(false);
        DisposeUndeliveredPayloads();
    }

    internal void DisposeUndeliveredPayloads()
    {
        while (_receiveChannel.Reader.TryRead(out var undelivered))
        {
            undelivered.Dispose();
        }
    }

    internal void CompleteReceive() => CompleteReceive(NearbyEndReason.PeerDisconnected);

    internal void CompleteReceive(NearbyEndReason reason)
    {
        _closedTcs.TrySetResult(reason);
        _receiveChannel.Writer.TryComplete();

        // Never disposed, for the reason given on InboundWorkToken.
        Volatile.Read(ref _closedCts)?.Cancel();
    }

    internal CancellationToken InboundWorkToken
    {
        get
        {
            var existing = Volatile.Read(ref _closedCts);

            if (existing is null)
            {
                var created = new CancellationTokenSource();
                existing = Interlocked.CompareExchange(ref _closedCts, created, null) ?? created;

                if (!ReferenceEquals(existing, created))
                {
                    created.Dispose();
                }

                if (!IsOpen)
                {
                    existing.Cancel();
                }
            }

            return existing.Token;
        }
    }

    internal void ReportInboundTransfer(NearbyTransferProgress progress)
    {
        if (progress.Status is NearbyTransferStatus.InProgress)
        {
            _inboundPositions[progress.PayloadId] = (progress.BytesTransferred, progress.TotalBytes);
        }
        else
        {
            _inboundPositions.TryRemove(progress.PayloadId, out _);
        }

        _inboundTransfers.Publish(progress);
    }

    internal void CompleteInboundTransfer(long payloadId, NearbyTransferStatus status)
    {
        var (bytesTransferred, totalBytes) = _inboundPositions.GetValueOrDefault(payloadId);

        ReportInboundTransfer(new NearbyTransferProgress(
            payloadId,
            bytesTransferred: status is NearbyTransferStatus.Completed ? totalBytes ?? bytesTransferred : bytesTransferred,
            totalBytes,
            status));
    }

    internal int PayloadsWritten => Volatile.Read(ref _payloadsWritten);

    internal void WritePayload(NearbyPayload payload)
    {
        if (_receiveChannel.Writer.TryWrite(payload))
        {
            Interlocked.Increment(ref _payloadsWritten);
            return;
        }

        payload.Dispose();
    }

    internal static async Task<string> StageToTempAsync(
        Func<Task<Stream>> openRead,
        string fileName,
        CancellationToken cancellationToken)
    {
        var directory = Directory.CreateTempSubdirectory("nearby-send-").FullName;
        var path = Path.Combine(directory, Path.GetFileName(fileName));
        var source = await openRead().ConfigureAwait(false);

        await using (source.ConfigureAwait(false))
        {
            var destination = File.Create(path);

            await using (destination.ConfigureAwait(false))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
        }

        return path;
    }
}
