using System.Collections.Specialized;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.Services;

/// <summary>
/// Stamps when each connected device was first seen connected, and exposes the connected set for
/// the header chip.
/// </summary>
/// <remarks>
/// A singleton that lives as long as the session, so it never unsubscribes from
/// <see cref="Connections"/>.
/// </remarks>
public sealed class ConnectionTracker
{
    readonly Dictionary<string, DateTimeOffset> _connectedSince = new(StringComparer.Ordinal);
    readonly TimeProvider _timeProvider;
    int _notifiedCount;

    public ConnectionTracker(NearbyDeviceCollection connections, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
        Connections = connections;
        Connections.CollectionChanged += OnConnectionsChanged;
        Restamp();
    }

    /// <summary>
    /// The devices currently connected.
    /// </summary>
    public NearbyDeviceCollection Connections { get; }

    /// <summary>
    /// When this device was first seen connected, or now for one this tracker has not seen.
    /// </summary>
    /// <remarks>
    /// The plugin reports that a device is connected, not since when, so the sample stamps the
    /// moment it noticed. A device that leaves <see cref="NearbyDeviceStatus.Connected"/> loses its
    /// stamp, so a reconnect starts counting again. Read from the UI thread, like
    /// <see cref="Connections"/>.
    /// </remarks>
    public DateTimeOffset ConnectedSince(string deviceId)
        => _connectedSince.TryGetValue(deviceId, out var since) ? since : _timeProvider.GetUtcNow();

    void OnConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Restamp();

        if (Connections.Count != _notifiedCount)
        {
            _notifiedCount = Connections.Count;
            ConnectionKeepAlive.Update(_notifiedCount);
        }
    }

    void Restamp()
    {
        var connected = Connections.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var id in _connectedSince.Keys.Except(connected).ToList())
        {
            _connectedSince.Remove(id);
        }

        foreach (var id in connected)
        {
            _connectedSince.TryAdd(id, _timeProvider.GetUtcNow());
        }
    }
}
