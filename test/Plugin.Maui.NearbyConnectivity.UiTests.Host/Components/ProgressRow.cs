namespace Plugin.Maui.NearbyConnectivity.UiTests.Host.Components;

/// <summary>A transfer's progress, read as text so a test can assert on it without pixel maths.</summary>
public sealed class ProgressRow : Label
{
    public ProgressRow(string automationId)
    {
        AutomationId = automationId;
        Text = "idle";
    }

    public void Set(long transferred, long? total)
        => Dispatcher.Dispatch(() => Text = total is { } size ? $"{transferred}/{size}" : $"{transferred}");
}
