using System.Text;
using Plugin.Maui.NearbyConnectivity;

namespace Minimal;

public class MainPage : ContentPage, IDisposable
{
    readonly INearbyConnectivity _nearby;
    readonly NearbyDeviceCollection _devices;
    readonly CollectionView _deviceList = new() { SelectionMode = SelectionMode.Single };
    readonly Entry _message = new() { Placeholder = "Message", Text = "Hello" };
    readonly Label _log = new();
    CancellationTokenSource? _receiving;

    public MainPage(INearbyConnectivity nearby)
    {
        _nearby = nearby;
        _devices = new NearbyDeviceCollection(nearby);

        _deviceList.ItemsSource = _devices;
        _deviceList.ItemTemplate = new DataTemplate(() =>
        {
            var label = new Label { Padding = 12 };
            label.SetBinding(Label.TextProperty, static (NearbyDevice d) => d.Presence.DisplayName);
            return label;
        });
        _deviceList.SelectionChanged += OnDeviceSelected;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 12,
                Children = { new Label { Text = "Tap a device to send the message." }, _message, _deviceList, _log },
            },
        };
    }

    #region start
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var status = await Permissions.RequestAsync<NearbyPermission>();
        if (status != PermissionStatus.Granted)
        {
            _log.Text = "Permission denied.";
            return;
        }

        // Accept every incoming request. A real app prompts the user instead.
        _nearby.Advertiser.SetConsent(NearbyConsentPolicy.AcceptAll);

        try
        {
            await _nearby.Advertiser.StartAsync();
            await _nearby.Discoverer.StartAsync();
        }
        catch (NearbyException ex)
        {
            _log.Text = $"Could not start: {ex.Message}";
            return;
        }

        _receiving = new CancellationTokenSource();
        _ = ReceiveAsync(_receiving.Token);
    }
    #endregion

    #region send
    async void OnDeviceSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection is not [NearbyDevice device])
        {
            return;
        }

        _deviceList.SelectedItem = null;

        try
        {
            var connection = await device.ConnectAsync();
            await connection.SendAsync(Encoding.UTF8.GetBytes(_message.Text));
            Write($"Sent to {device.Presence.DisplayName}");
        }
        catch (NearbyConnectException ex)
        {
            Write($"Could not connect: {ex.Reason}");
        }
        catch (NearbyException ex)
        {
            Write($"Could not send: {ex.Message}");
        }
    }
    #endregion

    #region receive
    async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        await foreach (var (from, payload) in _nearby.PayloadsAsync(cancellationToken))
        {
            using (payload)
            {
                if (payload is NearbyBytesPayload bytes)
                {
                    var text = Encoding.UTF8.GetString(bytes.Data.Span);
                    Write($"{from.Presence.DisplayName}: {text}");
                }
            }
        }
    }
    #endregion

    #region stop
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        _receiving?.Cancel();
        await _nearby.StopAllAsync();
    }
    #endregion

    public void Dispose()
    {
        _receiving?.Dispose();
        _devices.Dispose();
        GC.SuppressFinalize(this);
    }

    // PayloadsAsync delivers on the thread pool, so every UI write hops to the dispatcher.
    void Write(string line) => Dispatcher.Dispatch(() => _log.Text += line + Environment.NewLine);
}
