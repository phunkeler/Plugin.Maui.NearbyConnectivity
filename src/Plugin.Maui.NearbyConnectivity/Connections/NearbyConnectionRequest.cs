namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// An inbound connection request from a nearby device, held on <see cref="NearbyDevice.Request"/>
/// while the device reports <see cref="NearbyDeviceStatus.RequestReceived"/>.
/// </summary>
/// <remarks>
/// <para>
/// Answer it with <see cref="AcceptAsync(CancellationToken)"/> or
/// <see cref="DeclineAsync(CancellationToken)"/> before <see cref="ExpiresAt"/> elapses. A request
/// can be answered once, and only while it is outstanding.
/// </para>
/// <para>
/// Under a consent policy that answers for you, the session answers on the application's behalf,
/// the device never reports <see cref="NearbyDeviceStatus.RequestReceived"/>, and it moves
/// straight to <see cref="NearbyDeviceStatus.Connected"/> on
/// <see cref="INearbyConnectivity.DeviceChanged"/> instead.
/// </para>
/// </remarks>
/// <seealso href="https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/connect.html#answer-an-inbound-request">Answer an inbound request</seealso>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The session owns a request's lifetime and disarms its countdown at the single " +
        "claim that settles accept, decline, and expiry. Making the type IDisposable would put " +
        "that release on the public surface, where a consumer disposing a request the session " +
        "still holds would silence an expiry nobody asked to silence — the same reason " +
        "NearbyConnection exposes CloseAsync rather than IDisposable.")]
public sealed class NearbyConnectionRequest
{
    readonly TimeProvider _timeProvider;
    readonly TaskCompletionSource _expired = new(TaskCreationOptions.RunContinuationsAsynchronously);

    CancellationTokenSource? _countdown;
    int _settled;

    INearbyDeviceGateway Gateway => RemoteDevice.Gateway ?? throw new InvalidOperationException(
        $"The request from device '{RemoteDevice.Id}' is not attached to a session.");

    internal Func<CancellationToken, Task<NearbyConnection>> AcceptCore { get; }
    internal Func<CancellationToken, Task> RejectCore { get; }

    /// <summary>
    /// Gets the device asking to connect.
    /// </summary>
    /// <value>The remote device. Its display name is remote-chosen, sanitized input.</value>
    public NearbyDevice RemoteDevice { get; }

    /// <summary>
    /// Gets when this offer expires.
    /// </summary>
    /// <value>
    /// The one deadline both sides share: declared by the initiating device as its
    /// <see cref="NearbyOptions.OfferWindow"/>, sent on the wire with the request, and clamped
    /// locally. After it passes, the request is no longer answerable.
    /// </value>
    /// <remarks>
    /// A fixed point in time, computed once at receipt. Durations travel on the wire rather than
    /// timestamps, because the two devices share no clock. This value is in local time, read from
    /// the registered <see cref="TimeProvider"/>: compare it against that provider's
    /// <see cref="TimeProvider.GetUtcNow"/>, not <see cref="DateTimeOffset.UtcNow"/>.
    /// </remarks>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Gets whether this request is still answerable.
    /// </summary>
    /// <value>
    /// <see langword="true"/> until the request is accepted, declined, expires, or the session
    /// stops.
    /// </value>
    /// <remarks>
    /// A snapshot; the offer can expire between reading this and acting on it, which is why
    /// <see cref="AcceptAsync(CancellationToken)"/> reports the authoritative answer by throwing
    /// rather than expecting the caller to have checked.
    /// </remarks>
    public bool IsPending => Volatile.Read(ref _settled) == 0
        && !_expired.Task.IsCompleted;

    /// <summary>
    /// Gets how long is left to answer, or <see cref="TimeSpan.Zero"/> once the window has closed.
    /// </summary>
    /// <value>
    /// The remaining window, never negative. Recomputed on every read from
    /// <see cref="ExpiresAt"/>, so it is a live value rather than a stale snapshot.
    /// </value>
    /// <remarks>
    /// A convenience over <see cref="ExpiresAt"/> for a countdown that ticks on the application's
    /// own timer. The deadline stays the value to store, because this one is only true at the
    /// instant it is read. Read from the session's injected clock, like every other deadline this
    /// library owns, so a test can advance past one virtually.
    /// </remarks>
    public TimeSpan ExpiresIn
    {
        get
        {
            var remaining = ExpiresAt - _timeProvider.GetUtcNow();
            return remaining > TimeSpan.Zero
                ? remaining
                : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Gets a task that completes when this request stops being answerable: it expired, a
    /// connection to the device opened another way, or the session stopped.
    /// </summary>
    /// <value>
    /// A task that completes when the request is withdrawn. It never faults and never completes
    /// for a request that was answered in time. A view races its prompt against it with
    /// <see cref="Task.WhenAny(Task, Task)"/> to dismiss the prompt when the offer lapses; MAUI's
    /// own prompts take no <see cref="CancellationToken"/>, so a task is the shape they can race.
    /// </value>
    /// <example>
    /// <code language="csharp">
    /// var prompt = page.DisplayAlertAsync(
    ///     "Connect?", request.RemoteDevice.Presence.DisplayName, "Accept", "Decline");
    ///
    /// if (await Task.WhenAny(prompt, request.Expired) != prompt)
    /// {
    ///     return;   // The window closed; the plugin already declined it.
    /// }
    ///
    /// await (await prompt ? request.AcceptAsync(ct) : request.DeclineAsync(ct));
    /// </code>
    /// </example>
    public Task Expired => _expired.Task;

    internal NearbyConnectionRequest(
        NearbyDevice remoteDevice,
        DateTimeOffset deadline,
        Func<CancellationToken, Task<NearbyConnection>> accept,
        Func<CancellationToken, Task> reject,
        TimeProvider timeProvider)
    {
        RemoteDevice = remoteDevice;
        ExpiresAt = deadline;
        AcceptCore = accept;
        RejectCore = reject;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Accepts the request and completes the handshake.
    /// </summary>
    /// <param name="cancellationToken">A token to stop waiting for the handshake.</param>
    /// <returns>
    /// The established connection, the same instance <see cref="NearbyDevice.Connection"/>
    /// returns.
    /// </returns>
    /// <exception cref="NearbyConnectException">
    /// The handshake did not produce a connection. <see cref="NearbyConnectException.Reason"/> is
    /// <see cref="NearbyConnectFailure.TimedOut"/> when the offer's deadline passed, whether it
    /// expired before this call, or during the handshake, which is the same moment the initiating
    /// device gives up on its own <see cref="NearbyDevice.ConnectAsync(CancellationToken)"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled.
    /// </exception>
    public Task<NearbyConnection> AcceptAsync(CancellationToken cancellationToken = default)
        => Gateway.AcceptRequestAsync(this, cancellationToken);

    /// <summary>
    /// Declines the request. The remote device observes a declined handshake.
    /// </summary>
    /// <param name="cancellationToken">A token to stop waiting for the decline to be sent.</param>
    /// <returns>A task that completes once the decline is signaled to the platform.</returns>
    /// <remarks>
    /// No-op once the request is no longer pending: an offer that already expired needs no answer,
    /// and declining it changes nothing the remote device can still observe.
    /// </remarks>
    public Task DeclineAsync(CancellationToken cancellationToken = default)
        => Gateway.DeclineRequestAsync(this, cancellationToken);

    internal CancellationToken ArmCountdown()
    {
        var cts = new CancellationTokenSource();

        if (Interlocked.Exchange(ref _countdown, cts) is { } previous)
        {
            previous.Cancel();
            previous.Dispose();
        }

        return cts.Token;
    }

    internal void DisarmCountdown()
    {
        if (Interlocked.Exchange(ref _countdown, null) is { } cts)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    internal void MarkExpired()
    {
        if (Interlocked.Exchange(ref _settled, 1) == 0)
        {
            _expired.TrySetResult();
        }
    }

    internal void MarkAnswered()
    {
        Interlocked.Exchange(ref _settled, 1);
    }

    internal async Task TryRejectAsync(Action<Exception> onFailure, CancellationToken cancellationToken)
    {
        try
        {
            await RejectCore(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            onFailure(ex);
        }
    }
}
