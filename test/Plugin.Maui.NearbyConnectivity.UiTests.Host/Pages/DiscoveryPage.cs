using System.Globalization;

using Plugin.Maui.NearbyConnectivity.UiTests.Host.Components;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Host.Pages;

public sealed class DiscoveryPage : CapabilityPage
{
    readonly INearbyConnectivity _nearby;
    readonly StateBadge _state;
    readonly StateBadge _lastChange;
    readonly StateBadge _deviceCount;
    readonly VerticalStackLayout _devices;
    readonly Dictionary<string, View> _rows = new(StringComparer.Ordinal);

    public DiscoveryPage(INearbyConnectivity nearby)
        : base("discovery")
    {
        _nearby = nearby;
        Title = "Discovery";

        _state = AddBadge("state");
        _lastChange = AddBadge("last-change");
        _deviceCount = AddBadge("device-count");
        // Zero devices is a real, readable state. See ConnectionsPage for the same seeding.
        _deviceCount.Text = "0";
        Stack.Add(Error);

        AddButton("start", "Start discovering", () => _nearby.Discoverer.StartAsync(PageToken));
        AddButton("stop", "Stop discovering", () => _nearby.Discoverer.StopAsync());
        AddButton("refresh", "Refresh", () => _nearby.Discoverer.RefreshAsync(PageToken));

        _devices = new VerticalStackLayout { AutomationId = "discovery-devices", Spacing = 4 };
        Stack.Add(_devices);
    }

    protected override void OnPageActivated(CancellationToken cancellationToken)
    {
        SetState();

        _nearby.Discoverer.Changed += OnActivityChanged;
        cancellationToken.Register(() => _nearby.Discoverer.Changed -= OnActivityChanged);

        _nearby.DeviceChanged += OnDeviceChanged;
        cancellationToken.Register(() => _nearby.DeviceChanged -= OnDeviceChanged);

        foreach (var device in _nearby.Devices)
        {
            Apply(new NearbyDeviceChange(NearbyDeviceChangeAction.Added, device));
        }
    }

    void OnDeviceChanged(object? sender, NearbyDeviceChange change) => Apply(change);

    void OnActivityChanged(object? sender, EventArgs e)
    {
        SetState();
        _lastChange.Set(_nearby.Discoverer.LastStop is { } stop
            ? $"stopped:{stop.Reason}"
            : "started");
    }

    void SetState() => _state.Set(_nearby.Discoverer.IsActive ? "discovering" : "idle");

    void SetCount() => _deviceCount.Set(_rows.Count.ToString(CultureInfo.InvariantCulture));

    void Apply(NearbyDeviceChange change) => Dispatcher.Dispatch(() =>
    {
        var device = change.Device;

        if (change.Action is NearbyDeviceChangeAction.Removed)
        {
            if (_rows.Remove(device.Id, out var existing))
            {
                _devices.Remove(existing);
                SetCount();
            }

            return;
        }

        if (_rows.TryGetValue(device.Id, out var row))
        {
            _devices.Remove(row);
            _rows.Remove(device.Id);
        }

        var added = new DeviceRow(
            prefix: "discovery-device",
            deviceId: device.Id,
            displayName: device.Presence.DisplayName,
            status: device.Status.ToString(),
            actionText: "Connect",
            action: () => GuardAsync(() => device.ConnectAsync(PageToken)));

        _devices.Add(added);
        _rows[device.Id] = added;
        SetCount();
    });
}
