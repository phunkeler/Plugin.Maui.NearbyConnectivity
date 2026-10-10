using System.Text.RegularExpressions;

namespace Plugin.Maui.NearbyConnectivity;

// The rules are Apple's, because they are the stricter of the two — Nearby Connections accepts any
// string. They are applied on EVERY platform anyway: a value validated only where the SDK rejects
// it lets a developer building on Android ship an identifier that throws at startup on iOS, with
// nothing in their own build able to surface it. Validating the intersection means the mistake
// fails at the call site on whichever platform they happen to build first.
static partial class ServiceIdRules
{
    internal const string Unset = "_UNSET";

    const string Reference =
        "See https://developer.apple.com/documentation/multipeerconnectivity/mcnearbyserviceadvertiser " +
        "and RFC 6335 section 5.1 (Service Name Syntax).";

    internal static void Validate(
        string serviceId,
        string? suggestion,
        List<string> failures)
    {
        if (serviceId == Unset)
        {
            var setTo = suggestion is null
                ? "Set it to a short protocol name such as 'abc-txtchat'."
                : $"Set it to a short protocol name, for example: options.ServiceId = \"{suggestion}\";";

            failures.Add(
                "ServiceId has not been set. It identifies your application to nearby peers " +
                "and has no meaningful default. " + setTo + " " +
                "Note this is NOT a Bonjour '_name._tcp' service type; that longer form belongs " +
                "only in the app's Info.plist NSBonjourServices entries. " + Reference);
            return;
        }

        if (string.IsNullOrWhiteSpace(serviceId))
        {
            failures.Add(
                $"{nameof(NearbyOptions.ServiceId)} must not be null, empty, or whitespace. On iOS it is passed " +
                "directly as MCNearbyServiceAdvertiser's serviceType, which rejects it and raises " +
                "a native exception that cannot be caught. " + Reference);
            return;
        }

        if (serviceId.Length > 15)
        {
            failures.Add(
                $"{nameof(NearbyOptions.ServiceId)} '{serviceId}' is {serviceId.Length} characters long; it must be " +
                $"1-15 characters. {Reference}");
        }

        if (!serviceId.All(IsAllowedCharacter))
        {
            var offending = new string([.. serviceId.Where(c => !IsAllowedCharacter(c)).Distinct()]);

            failures.Add(
                $"{nameof(NearbyOptions.ServiceId)} '{serviceId}' contains characters that are not permitted: '{offending}'. " +
                $"Only ASCII lowercase letters, digits, and hyphens are permitted — uppercase " +
                $"letters, underscores, dots, and spaces are all invalid. {Reference}");
        }

        if (!serviceId.Any(char.IsAsciiLetterLower))
        {
            failures.Add(
                $"{nameof(NearbyOptions.ServiceId)} '{serviceId}' contains no ASCII letter; it must contain at " +
                $"least one. {Reference}");
        }

        if (serviceId.StartsWith('-') || serviceId.EndsWith('-'))
        {
            failures.Add(
                $"{nameof(NearbyOptions.ServiceId)} '{serviceId}' begins or ends with a hyphen, which is not permitted. {Reference}");
        }

        if (serviceId.Contains("--", StringComparison.Ordinal))
        {
            failures.Add(
                $"{nameof(NearbyOptions.ServiceId)} '{serviceId}' contains adjacent hyphens, which are not permitted. {Reference}");
        }
    }

    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Service ids are lowercase on both platforms.")]
    internal static string? Suggest(string? applicationName)
    {
        var candidate = Unsupported().Replace(applicationName?.ToLowerInvariant() ?? "", "-").Trim('-');

        if (candidate.Length > 15)
        {
            candidate = candidate[..15].TrimEnd('-');
        }

        return candidate.Any(char.IsAsciiLetterLower) ? candidate : null;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex Unsupported();

    static bool IsAllowedCharacter(char c)
        => char.IsAsciiLetterLower(c)
            || char.IsAsciiDigit(c)
            || c == '-';
}
