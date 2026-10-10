namespace Plugin.Maui.NearbyConnectivity;

sealed class ConnectionTable
{
    const int UnreadPayloadWarningThreshold = 64;

    readonly ConcurrentDictionary<string, NearbyConnection> _connections = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, byte> _unobservedWarned = new(StringComparer.Ordinal);
    readonly ILogger _logger;
    readonly TimeSpan _drainTimeout;

    internal bool IsEmpty => _connections.IsEmpty;
    internal KeyedSerialQueue WorkQueue { get; }

    internal ConnectionTable(
        ILogger logger,
        TimeProvider timeProvider,
        TimeSpan drainTimeout)
    {
        _logger = logger;
        _drainTimeout = drainTimeout;
        WorkQueue = new KeyedSerialQueue(
            (key, ex) => NearbyLog.PlatformCallbackFailed(_logger, nameof(KeyedSerialQueue), key, ex),
            timeProvider);
    }

    internal bool IsConnected(string deviceId)
        => _connections.ContainsKey(deviceId);

    internal bool TryGet(string deviceId, [NotNullWhen(true)] out NearbyConnection? connection)
        => _connections.TryGetValue(deviceId, out connection);

    internal NearbyConnection[] Snapshot()
        => [.. _connections.Values];

    internal void Add(string deviceId, NearbyConnection connection)
        => _connections[deviceId] = connection;

    internal void Withdraw(string deviceId)
        => _connections.TryRemove(deviceId, out _);

    internal NearbyConnection[] ClaimAll()
    {
        var live = _connections.Values.ToArray();
        _connections.Clear();
        _unobservedWarned.Clear();

        return live;
    }

    internal void RequireConnected(string deviceId, string operation)
    {
        if (!IsConnected(deviceId))
        {
            throw new NearbyException(
                $"Cannot {operation}: no active connection for device '{deviceId}'.");
        }
    }

    internal void ReportInboundProgress(string deviceId, NearbyTransferProgress progress)
    {
        if (_connections.TryGetValue(deviceId, out var connection))
        {
            connection.ReportInboundTransfer(progress);
        }
    }

    internal void CompleteInboundTransfer(string deviceId, long payloadId, NearbyTransferStatus status)
    {
        if (_connections.TryGetValue(deviceId, out var connection))
        {
            connection.CompleteInboundTransfer(payloadId, status);
        }
    }

    internal CancellationToken InboundWorkToken(string deviceId)
        => _connections.TryGetValue(deviceId, out var connection)
            ? connection.InboundWorkToken
            : new CancellationToken(canceled: true);

    internal void WritePayload(string deviceId, NearbyPayload payload)
    {
        try
        {
            if (!_connections.TryGetValue(deviceId, out var connection))
            {
                NearbyLog.PayloadWithoutConnection(_logger, deviceId);
                payload.Dispose();
                return;
            }

            if (connection.PayloadsWritten >= UnreadPayloadWarningThreshold
                && _unobservedWarned.TryAdd(deviceId, 0))
            {
                NearbyLog.PayloadsUnread(_logger, deviceId, connection.PayloadsWritten);
            }

            connection.WritePayload(payload);
        }
        catch (Exception ex)
        {
            NearbyLog.EventWriteFailed(_logger, nameof(WritePayload), deviceId, ex);
        }
    }

    internal async ValueTask ReleaseAsync(string deviceId, Action<string> releasePlatformHandles)
    {
        if (_connections.TryRemove(deviceId, out var connection))
        {
            connection.CompleteReceive();
        }

        _unobservedWarned.TryRemove(deviceId, out _);

        if (!await WorkQueue.DrainAsync(deviceId, _drainTimeout).ConfigureAwait(false))
        {
            NearbyLog.ConnectionDrainTimedOut(_logger, deviceId, _drainTimeout.TotalSeconds);
        }

        releasePlatformHandles(deviceId);
        connection?.DisposeUndeliveredPayloads();
    }

    internal async Task DrainAllAsync()
    {
        if (await WorkQueue.DrainAllAsync(_drainTimeout).ConfigureAwait(false) is > 0 and var pending)
        {
            NearbyLog.PayloadDrainTimedOut(_logger, pending, _drainTimeout.TotalSeconds);
        }
    }
}
