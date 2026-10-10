namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Reads items out of the delivery streams. Replay makes this reliable:
/// a request or connection still outstanding is yielded to a late enumerator too, so a test can
/// emit first and enumerate second without a race.
/// </summary>
static class Take
{
    /// <summary>The first item the stream yields.</summary>
    /// <param name="stream">The delivery stream to read.</param>
    /// <param name="cancellationToken">Bounds the wait.</param>
    public static async Task<T> FirstAsync<T>(
        IAsyncEnumerable<T> stream,
        CancellationToken cancellationToken)
    {
        await foreach (var item in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            return item;
        }

        throw new InvalidOperationException("The stream ended without yielding an item.");
    }

    /// <summary>
    /// The first request a device carries: one already held, or the next one
    /// <see cref="INearbyConnectivity.DeviceChanged"/> reports.
    /// </summary>
    /// <param name="session">The session whose devices to watch.</param>
    /// <param name="cancellationToken">Bounds the wait.</param>
    public static async Task<NearbyConnectionRequest> RequestAsync(
        INearbyConnectivity session,
        CancellationToken cancellationToken)
    {
        var raised = new TaskCompletionSource<NearbyConnectionRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChanged(object? sender, NearbyDeviceChange change)
        {
            if (change.Device.Request is { } request)
            {
                raised.TrySetResult(request);
            }
        }

        session.DeviceChanged += OnChanged;

        try
        {
            if (session.Devices.Select(d => d.Request).FirstOrDefault(r => r is not null) is { } held)
            {
                return held;
            }

            return await raised.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            session.DeviceChanged -= OnChanged;
        }
    }
}
