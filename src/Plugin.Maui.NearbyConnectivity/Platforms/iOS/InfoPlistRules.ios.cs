namespace Plugin.Maui.NearbyConnectivity;

// The Info.plist keys MPC needs, checked at run time so a missing one is reported rather than
// discovered as a session that finds nothing. These are build-time configuration, not a runtime
// permission, so there is nothing to request and NearbyPermission reports Granted on iOS. Without
// them the failure is silent in the worst way: advertising and discovery start, the local-network
// prompt never appears, and no peer is ever found. Nothing in the SDK reports it.
//
// Read from the bundle rather than validated at startup, because the values live in the
// application's own Info.plist and the library cannot see them until it runs on the device.
//
// What is an error here and what is only a warning: a missing usage description, and an
// NSBonjourServices that names this service identifier under no transport at all, are both
// unambiguously broken, so they block a start. Declaring only ONE transport is not. Apple documents
// neither a requirement for both nor a rule for choosing, and asked directly whether _service._tcp
// alone suffices, a DTS engineer answered "I think it is but I don't know MultipeerConnectivity
// well enough to say that with 100% certainty"
// (https://developer.apple.com/forums/thread/651842). Developers in that thread report both
// outcomes. Refusing to start on a configuration Apple has not ruled out would turn a working app
// into a hard failure on this library's guess, so a partial declaration is logged and allowed.
static class InfoPlistRules
{
    internal const string BonjourServicesKey = "NSBonjourServices";
    internal const string LocalNetworkUsageKey = "NSLocalNetworkUsageDescription";

    const string Reference =
        "See https://developer.apple.com/documentation/bundleresources/information-property-list/nsbonjourservices.";

    static readonly string[] s_transports = ["_tcp", "_udp"];

    internal static void Validate(
        string serviceId,
        List<string> failures,
        List<string> warnings)
        => Validate(
            NSBundle.MainBundle.InfoDictionary,
            serviceId,
            failures,
            warnings);

    internal static void Validate(
        NSDictionary? info,
        string serviceId,
        List<string> failures,
        List<string> warnings)
    {
        if (info is null)
        {
            return;
        }

        ValidateLocalNetworkUsage(info, failures);
        ValidateBonjourServices(info, serviceId, failures, warnings);
    }

    static void ValidateLocalNetworkUsage(NSDictionary info, List<string> failures)
    {
        if (info[LocalNetworkUsageKey] is NSString { Length: > 0 })
        {
            return;
        }

        failures.Add(
            $"{LocalNetworkUsageKey} is missing from Info.plist. iOS shows it to the user when the "
            + "app first uses the local network. Add a string explaining why this app connects to nearby devices.");
    }

    static void ValidateBonjourServices(
        NSDictionary info,
        string serviceId,
        List<string> failures,
        List<string> warnings)
    {
        if (info[BonjourServicesKey] is not NSArray { Count: > 0 } declared)
        {
            failures.Add(
                $"{BonjourServicesKey} is missing from Info.plist. Add '_{serviceId}._tcp', and "
                + $"'_{serviceId}._udp' alongside it unless you have confirmed TCP alone is enough "
                + $"for your app, or MultipeerConnectivity cannot browse. {Reference}");

            return;
        }

        var entries = new HashSet<string>(StringComparer.Ordinal);

        for (nuint i = 0; i < declared.Count; i++)
        {
            if (declared.GetItem<NSString>(i) is { } entry)
            {
                entries.Add(entry.ToString());
            }
        }

        List<string> missing = [];

        foreach (var transport in s_transports)
        {
            if (!entries.Contains($"_{serviceId}.{transport}"))
            {
                missing.Add($"_{serviceId}.{transport}");
            }
        }

        if (missing.Count == s_transports.Length)
        {
            failures.Add(
                $"{BonjourServicesKey} declares no entry for ServiceId '{serviceId}'. Add "
                + $"'_{serviceId}._tcp', and '_{serviceId}._udp' alongside it unless you have "
                + "confirmed TCP alone is enough for your app. Note the Bonjour form takes the "
                + $"'_name._tcp' shape that ServiceId itself must not. {Reference}");

            return;
        }

        if (missing.Count > 0)
        {
            warnings.Add(
                $"{BonjourServicesKey} does not declare {string.Join(" and ", missing)}. Apple "
                + "does not document whether MultipeerConnectivity needs both transports, and "
                + "developers report both outcomes, so this is allowed to run. Declare the missing "
                + $"entry if discovery finds no peer. {Reference}");
        }
    }
}
