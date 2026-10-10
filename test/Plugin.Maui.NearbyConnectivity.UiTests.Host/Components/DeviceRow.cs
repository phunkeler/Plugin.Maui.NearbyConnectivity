namespace Plugin.Maui.NearbyConnectivity.UiTests.Host.Components;

/// <summary>
/// One device in a list, with an action button. The same row shape appears on the discovery and
/// connections pages, so its ids are built in one place:
/// "<c>prefix</c>-<c>name</c>|<c>status</c>|<c>action</c>-<c>deviceId</c>".
/// </summary>
public sealed class DeviceRow : HorizontalStackLayout
{
    public DeviceRow(string prefix, string deviceId, string displayName, string status, string actionText, Func<Task> action)
    {
        Spacing = 8;
        Padding = new Thickness(0, 4);
        AutomationId = $"{prefix}-row-{deviceId}";

        var button = new Button { AutomationId = $"{prefix}-action-{deviceId}", Text = actionText };
        button.Clicked += async (_, _) => await action().ConfigureAwait(true);

        Add(new Label
        {
            AutomationId = $"{prefix}-name-{deviceId}",
            Text = displayName,
            VerticalOptions = LayoutOptions.Center,
        });
        Add(new Label
        {
            AutomationId = $"{prefix}-status-{deviceId}",
            Text = status,
            VerticalOptions = LayoutOptions.Center,
        });
        Add(button);
    }
}
