namespace Plugin.Maui.NearbyConnectivity;

sealed class DeviceRegistry : IReadOnlyList<NearbyDevice>
{
    readonly Lock _gate = new();
    readonly Dictionary<string, NearbyDevice> _devices = new(StringComparer.Ordinal);
    readonly HashSet<string> _unconfirmed = new(StringComparer.Ordinal);
    readonly Action<NearbyDeviceChange> _changed;

    volatile NearbyDevice[] _snapshot = [];

    internal DeviceRegistry(Action<NearbyDeviceChange>? changed = null)
        => _changed = changed ?? (static _ => { });

    public int Count => _snapshot.Length;
    public NearbyDevice this[int index] => _snapshot[index];

    public IEnumerator<NearbyDevice> GetEnumerator()
        => ((IEnumerable<NearbyDevice>)_snapshot).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        => _snapshot.GetEnumerator();

    internal bool TryGet(string id, [NotNullWhen(true)] out NearbyDevice? device)
    {
        lock (_gate)
        {
            return _devices.TryGetValue(id, out device);
        }
    }

    internal NearbyDevice AddIfAbsent(NearbyDevice device)
    {
        lock (_gate)
        {
            _unconfirmed.Remove(device.Id);

            if (_devices.TryGetValue(device.Id, out var existing))
            {
                return existing;
            }

            _devices[device.Id] = device;
            Rebuild();
            _changed(new NearbyDeviceChange(NearbyDeviceChangeAction.Added, device));
        }

        return device;
    }

    internal NearbyDevice? Update(
        string id,
        Func<NearbyDevice, NearbyDevice> update,
        NearbyEndReason? reason = null)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(id, out var current))
            {
                return null;
            }

            _unconfirmed.Remove(id);

            var updated = update(current);

            if (ReferenceEquals(updated, current))
            {
                return current;
            }

            _devices[id] = updated;
            Rebuild();
            _changed(new NearbyDeviceChange(NearbyDeviceChangeAction.Updated, updated) { Reason = reason });
            return updated;
        }
    }

    internal bool Remove(string id, NearbyEndReason? reason = null)
    {
        lock (_gate)
        {
            if (!_devices.Remove(id, out var removed))
            {
                return false;
            }

            Rebuild();
            _changed(new NearbyDeviceChange(NearbyDeviceChangeAction.Removed, removed) { Reason = reason });
        }

        return true;
    }

    internal void RemoveWhere(Func<NearbyDevice, bool> predicate, NearbyEndReason? reason = null)
    {
        List<NearbyDeviceChange> changes = [];

        lock (_gate)
        {
            foreach (var device in _devices.Values.ToArray())
            {
                if (predicate(device) && _devices.Remove(device.Id))
                {
                    changes.Add(new NearbyDeviceChange(NearbyDeviceChangeAction.Removed, device) { Reason = reason });
                }
            }

            if (changes.Count == 0)
            {
                return;
            }

            Rebuild();

            foreach (var change in changes)
            {
                _changed(change);
            }
        }
    }

    internal void Clear(NearbyEndReason? reason = null)
        => RemoveWhere(static _ => true, reason);

    internal void BeginGeneration(Func<string, bool> isConnected)
    {
        lock (_gate)
        {
            _unconfirmed.Clear();

            foreach (var device in _devices.Values)
            {
                if (device.Status is NearbyDeviceStatus.Visible)
                {
                    _unconfirmed.Add(device.Id);
                    continue;
                }

                if (device.Status is not NearbyDeviceStatus.Connecting and not NearbyDeviceStatus.RequestReceived
                    && !isConnected(device.Id))
                {
                    _unconfirmed.Add(device.Id);
                }
            }
        }
    }

    internal void EvictUnconfirmed()
        => RemoveWhere(device => _unconfirmed.Contains(device.Id), NearbyEndReason.LostFromDiscovery);

    void Rebuild()
    {
        var snapshot = new NearbyDevice[_devices.Count];
        _devices.Values.CopyTo(snapshot, 0);
        _snapshot = snapshot;
    }
}
