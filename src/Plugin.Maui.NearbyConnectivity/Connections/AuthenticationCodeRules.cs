using System.Globalization;
using System.Security.Cryptography;

namespace Plugin.Maui.NearbyConnectivity;

// One derivation for both platforms, so the six digits mean the same thing on either end of a
// connection. The inputs differ — Android hands over UKEY2's 32-byte raw token, iOS the two DTLS
// certificate fingerprints and two committed nonces — and neither layout is published, which is
// what lets this function change in a later release without breaking a consumer. See
// docs/security.md.
static class AuthenticationCodeRules
{
    const uint Modulus = 1_000_000;

    internal static string FromToken(ReadOnlySpan<byte> token)
    {
        if (token.IsEmpty)
        {
            throw new ArgumentException("An authentication code cannot be derived from an empty token.", nameof(token));
        }

        return (BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(token)) % Modulus)
            .ToString("D6", CultureInfo.InvariantCulture);
    }

    internal static string FromBindingAndNonces(
        ReadOnlySpan<byte> binding,
        ReadOnlySpan<byte> localNonce,
        ReadOnlySpan<byte> remoteNonce)
        => FromToken([.. s_linkCodeLabel, .. binding, .. Unordered(localNonce, remoteNonce)]);

    static readonly byte[] s_linkCodeLabel = "Plugin.Maui.NearbyConnectivity/link-code/v1"u8.ToArray();

    internal static byte[] Unordered(ReadOnlySpan<byte> local, ReadOnlySpan<byte> remote)
        => local.SequenceCompareTo(remote) <= 0
            ? [.. local, .. remote]
            : [.. remote, .. local];
}
