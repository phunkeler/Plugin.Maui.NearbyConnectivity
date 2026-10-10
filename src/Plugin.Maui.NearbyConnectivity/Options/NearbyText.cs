namespace Plugin.Maui.NearbyConnectivity;

// Length only. Nothing here rewrites a value for how it will look: rendering policy belongs to the
// application, and the plugin applies none to payloads either. A locally configured value is
// validated rather than cut.
static class NearbyText
{
    // Apple's MCPeerID cap on iOS, and the connect request's wire budget elsewhere. ONE value, so
    // an inbound name cannot arrive at a length the wire frame and NearbyPresence both reject.
    internal const int MaxDisplayNameBytes = 63;

    // Cuts at a rune boundary. For values the plugin must accept rather than reject: the default
    // local name, Android's EndpointName fallback, an iOS remote peer's name, and an inbound stream
    // name.
    internal static string Truncate(string? value, int maxBytes = MaxDisplayNameBytes)
    {
        if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value ?? string.Empty;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maxBytes));
        var bytes = 0;

        foreach (var rune in value.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > maxBytes)
            {
                break;
            }

            bytes += rune.Utf8SequenceLength;
            builder.Append(rune);
        }

        return builder.ToString();
    }
}
