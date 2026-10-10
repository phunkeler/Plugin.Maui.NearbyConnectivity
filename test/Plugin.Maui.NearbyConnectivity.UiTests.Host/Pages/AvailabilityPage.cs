namespace Plugin.Maui.NearbyConnectivity.UiTests.Host.Pages;

public sealed class AvailabilityPage : CapabilityPage
{
    readonly INearbyConnectivity _nearby;
    readonly Components.StateBadge _current;
    readonly Components.StateBadge _checked;

    public AvailabilityPage(INearbyConnectivity nearby)
        : base("availability")
    {
        _nearby = nearby;
        Title = "Availability";

        // The cached snapshot, refreshed as the platform reports changes.
        _current = AddBadge("current");
        // The forced read. Kept separate so a test can tell a stale cache from a fresh result.
        _checked = AddBadge("checked");
        Stack.Add(Error);

        AddButton("check", "Check", async () =>
            _checked.Set((await _nearby.CheckAvailabilityAsync(PageToken).ConfigureAwait(true)).ToString()));
    }

    protected override void OnPageActivated(CancellationToken cancellationToken)
    {
        _nearby.AvailabilityChanged += OnAvailabilityChanged;
        cancellationToken.Register(() => _nearby.AvailabilityChanged -= OnAvailabilityChanged);
        _current.Set(_nearby.Availability.ToString());
    }

    void OnAvailabilityChanged(object? sender, EventArgs e) => _current.Set(_nearby.Availability.ToString());
}
