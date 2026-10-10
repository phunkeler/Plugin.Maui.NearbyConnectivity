using System.Globalization;

using Plugin.Maui.NearbyConnectivity.UiTests.Host.Components;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Host.Pages;

public sealed class AdvertisingPage : CapabilityPage
{
    readonly INearbyConnectivity _nearby;
    readonly StateBadge _state;
    readonly StateBadge _lastChange;
    readonly StateBadge _requestCount;
    readonly VerticalStackLayout _requests;
    readonly HashSet<string> _shown = new(StringComparer.Ordinal);

    public AdvertisingPage(INearbyConnectivity nearby)
        : base("advertising")
    {
        _nearby = nearby;
        Title = "Advertising";

        _state = AddBadge("state");
        // Carries the reason, which a start/stop boolean pair cannot: an application-requested stop
        // and a platform-forced one are indistinguishable otherwise.
        _lastChange = AddBadge("last-change");
        _requestCount = AddBadge("request-count");
        Stack.Add(Error);

        AddButton("start", "Start advertising", () => _nearby.Advertiser.StartAsync(PageToken));
        AddButton("stop", "Stop advertising", () => _nearby.Advertiser.StopAsync());

        _requests = new VerticalStackLayout { AutomationId = "advertising-requests", Spacing = 4 };
        Stack.Add(_requests);
    }

    protected override void OnPageActivated(CancellationToken cancellationToken)
    {
        SetState();
        SetCount();

        _nearby.Advertiser.Changed += OnActivityChanged;
        cancellationToken.Register(() => _nearby.Advertiser.Changed -= OnActivityChanged);

        _nearby.DeviceChanged += OnDeviceChanged;
        cancellationToken.Register(() => _nearby.DeviceChanged -= OnDeviceChanged);

        foreach (var device in _nearby.Devices)
        {
            ShowRequest(device);
        }
    }

    void OnDeviceChanged(object? sender, NearbyDeviceChange change) => ShowRequest(change.Device);

    void ShowRequest(NearbyDevice device)
    {
        if (device.Request is { } request)
        {
            AddRequestRow(request);
        }
    }

    void OnActivityChanged(object? sender, EventArgs e)
    {
        SetState();
        _lastChange.Set(_nearby.Advertiser.LastStop is { } stop
            ? $"stopped:{stop.Reason}"
            : "started");
    }

    void SetState() => _state.Set(_nearby.Advertiser.IsActive ? "advertising" : "idle");

    void SetCount() => _requestCount.Set(_shown.Count.ToString(CultureInfo.InvariantCulture));

    void AddRequestRow(NearbyConnectionRequest request)
    {
        var device = request.RemoteDevice;

        Dispatcher.Dispatch(() =>
        {
            if (!_shown.Add(device.Id))
            {
                return;
            }

            var row = new HorizontalStackLayout
            {
                AutomationId = $"advertising-request-row-{device.Id}",
                Spacing = 8,
            };

            row.Add(new Label
            {
                AutomationId = $"advertising-request-name-{device.Id}",
                Text = device.Presence.DisplayName,
                VerticalOptions = LayoutOptions.Center,
            });
            row.Add(AnswerButton($"advertising-request-accept-{device.Id}", "Accept", device.Id, row,
                () => request.AcceptAsync(PageToken)));
            row.Add(AnswerButton($"advertising-request-decline-{device.Id}", "Decline", device.Id, row,
                () => request.DeclineAsync(PageToken)));

            _requests.Add(row);
            SetCount();
        });
    }

    Button AnswerButton(string automationId, string text, string deviceId, View row, Func<Task> answer)
    {
        var button = new Button { AutomationId = automationId, Text = text };

        button.Clicked += async (_, _) => await GuardAsync(async () =>
        {
            await answer().ConfigureAwait(true);
            _requests.Remove(row);
            _shown.Remove(deviceId);
            SetCount();
        }).ConfigureAwait(true);

        return button;
    }
}
