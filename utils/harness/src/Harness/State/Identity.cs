using System.Globalization;

namespace Harness;

/// <summary>
/// A platform identity in the one spelling every comparison uses. Each record system builds its identities through its
/// own factory, so two spellings of one account are always equal.
/// </summary>
readonly record struct Identity
{
    Identity(string id) => Id = id;

    /// <summary>Gets the identity in its canonical spelling.</summary>
    public string Id { get; }

    /// <summary>Creates the identity of a GitHub login.</summary>
    /// <param name="login">The login as a person wrote it or as the API reports it.</param>
    /// <returns>The identity: the login in lower case, without a trailing <c>[bot]</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="login"/> is <see langword="null"/>.</exception>
    public static Identity FromGitHubLogin(string login)
    {
        ArgumentNullException.ThrowIfNull(login);

        // TextInfo, not ToLowerInvariant: CA1308 flags that call, and logins are canonically lower case.
        var id = CultureInfo.InvariantCulture.TextInfo.ToLower(login);
        return new(id.EndsWith("[bot]", StringComparison.Ordinal) ? id[..^"[bot]".Length] : id);
    }
}
