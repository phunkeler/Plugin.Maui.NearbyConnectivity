namespace Plugin.Maui.NearbyConnectivity.UiTests.Host.Components;

/// <summary>A single labelled state value the tests read by AutomationId.</summary>
public sealed class StateBadge : Label
{
    public StateBadge(string automationId)
    {
        AutomationId = automationId;
        FontSize = 16;
        Text = "-";
    }

    public void Set(string value) => Dispatcher.Dispatch(() => Text = value);
}
