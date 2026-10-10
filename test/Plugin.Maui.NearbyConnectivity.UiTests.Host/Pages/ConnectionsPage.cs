using System.Globalization;

using Plugin.Maui.NearbyConnectivity.UiTests.Host.Components;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Host.Pages;

public sealed class ConnectionsPage : CapabilityPage
{
    readonly INearbyConnectivity _nearby;
    readonly StateBadge _count;
    readonly StateBadge _lastClosed;
    readonly VerticalStackLayout _connections;
    readonly Dictionary<string, View> _rows = new(StringComparer.Ordinal);

    public ConnectionsPage(INearbyConnectivity nearby)
        : base("connections")
    {
        _nearby = nearby;
        Title = "Connections";
        _count = AddBadge("count");
        _count.Text = "0";
        _lastClosed = AddBadge("last-closed");
        Stack.Add(Error);

        AddButton("stop-all", "Stop all", () => _nearby.StopAllAsync());

        _connections = new VerticalStackLayout { AutomationId = "connections-list", Spacing = 4 };
        Stack.Add(_connections);
    }

    protected override void OnPageActivated(CancellationToken cancellationToken)
    {
        _nearby.DeviceChanged += OnDeviceChanged;
        cancellationToken.Register(() => _nearby.DeviceChanged -= OnDeviceChanged);

        foreach (var device in _nearby.Devices)
        {
            Show(device);
        }
    }

    void OnDeviceChanged(object? sender, NearbyDeviceChange change) => Show(change.Device);

    void Show(NearbyDevice device)
    {
        if (device.Connection is { } connection)
        {
            Add(connection);
        }
    }

    void SetCount() => _count.Set(_rows.Count.ToString(CultureInfo.InvariantCulture));

    void Add(NearbyConnection connection) => Dispatcher.Dispatch(() =>
    {
        var device = connection.RemoteDevice;

        if (_rows.ContainsKey(device.Id))
        {
            return;
        }

        var row = new DeviceRow(
            prefix: "connections-device",
            deviceId: device.Id,
            displayName: device.Presence.DisplayName,
            status: "open",
            actionText: "Disconnect",
            action: () => GuardAsync(() => connection.CloseAsync()));

        _connections.Add(row);
        _rows[device.Id] = row;
        SetCount();

        TrackClose(connection, device.Id);
    });

    // Connection.Closed carries the reason; the row is removed when it completes rather than being
    // polled, so "disconnected" is observed the same way whichever side ended it.
    async void TrackClose(NearbyConnection connection, string deviceId)
    {
        NearbyEndReason reason;

        try
        {
            reason = await connection.Closed.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Error.Set($"{ex.GetType().Name}: {ex.Message}");
            return;
        }

        Dispatcher.Dispatch(() =>
        {
            if (_rows.Remove(deviceId, out var row))
            {
                _connections.Remove(row);
                SetCount();
            }

            _lastClosed.Set(reason.ToString());
        });
    }
}
