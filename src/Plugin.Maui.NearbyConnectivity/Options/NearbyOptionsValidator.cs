namespace Plugin.Maui.NearbyConnectivity;

static class NearbyOptionsValidator
{
    internal static void Validate(NearbyOptions options)
    {
        var failures = new List<string>();

        ServiceIdRules.Validate(
            options.ServiceId,
            options.ServiceId == ServiceIdRules.Unset
                ? SuggestedServiceId()
                : null,
            failures);

        if (options.Presence is null)
        {
            failures.Add($"{nameof(options.Presence)} must not be null.");
        }

        if (options.DiscoveryRefreshInterval is { } refresh && refresh <= TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(options.DiscoveryRefreshInterval)} must be greater than zero, or null to never refresh.");
        }

        if (options.OfferWindow <= TimeSpan.Zero
            || options.OfferWindow > OfferWindowPolicy.s_max)
        {
            failures.Add(
                $"{nameof(options.OfferWindow)} must be greater than zero and no more than "
                + $"{OfferWindowPolicy.s_max.TotalMinutes:0.#} minutes, "
                + "the longest window a remote device honors.");
        }

        if (options.FileTransferInactivityTimeout is { } transferTimeout
            && transferTimeout <= TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(options.FileTransferInactivityTimeout)} must be greater than zero, "
                + "or null to disable the check.");
        }

        if (failures.Count > 0)
        {
            throw new ArgumentException($"{nameof(NearbyOptions)} is invalid. {string.Join(" ", failures)}");
        }
    }

    static string? SuggestedServiceId()
    {
        try
        {
            return ServiceIdRules.Suggest(AppInfo.Name);
        }
        catch (NotImplementedException)
        {
            return null;
        }
    }
}
