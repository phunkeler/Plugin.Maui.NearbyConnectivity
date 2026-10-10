namespace Plugin.Maui.NearbyConnectivity;

static class OfferWindowPolicy
{
    internal static readonly TimeSpan s_default = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan s_max = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan s_autoAcceptHandshake = TimeSpan.FromSeconds(30);

    internal static TimeSpan Clamp(TimeSpan declared)
        => declared == Timeout.InfiniteTimeSpan || declared > s_max
            ? s_max
            : declared < TimeSpan.Zero
                ? TimeSpan.Zero
                : declared;
}
