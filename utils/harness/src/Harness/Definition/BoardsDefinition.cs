using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Harness;

/// <summary>
/// Where the Azure Boards work items live: the <c>boards</c> block of <c>harness.json</c>, or the <c>ADO_ORG</c> and
/// <c>ADO_PROJECT</c> environment variables, which win.
/// </summary>
/// <param name="Organization">The organization URL, as <c>az devops</c> takes it for <c>--org</c>.</param>
/// <param name="Project">The project name.</param>
sealed record BoardsDefinition(string? Organization, string? Project)
{
    /// <summary>The hosts an organization URL may name: Azure DevOps Services, and the older per-organization domain.</summary>
    public static ImmutableArray<string> AllowedHosts { get; } = ["dev.azure.com", "visualstudio.com"];

    /// <summary>Tells whether the definition names both the organization and the project.</summary>
    [MemberNotNullWhen(true, nameof(Organization), nameof(Project))]
    public bool IsComplete => Organization is not null && Project is not null;

    /// <summary>Combines the <c>boards</c> block with the environment, key by key.</summary>
    /// <param name="configured">The <c>boards</c> block; <see langword="null"/> when it is absent.</param>
    /// <param name="environment">The environment; an empty or white-space value counts as not set.</param>
    /// <returns>
    /// Each key from the environment when it is set, otherwise from the block; <see langword="null"/> keys stay
    /// <see langword="null"/>.
    /// </returns>
    public static BoardsDefinition Resolve(BoardsDefinition? configured, BoardsEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return new(
            Organization: Set(environment.Organization) ?? configured?.Organization,
            Project: Set(environment.Project) ?? configured?.Project);
    }

    /// <summary>
    /// Tells whether a value can be an organization URL. The token <c>az rest</c> acquires goes to whatever host the
    /// URL names, so only Azure DevOps hosts are accepted.
    /// </summary>
    /// <param name="organization">The value.</param>
    /// <returns>
    /// <see langword="true"/> for an absolute <c>https</c> URL on one of <see cref="AllowedHosts"/> or a subdomain of
    /// one, without <c>&lt;</c> or <c>&gt;</c>, which only a placeholder holds; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool IsOrganizationUrl(string organization) =>
        organization.IndexOfAny(['<', '>']) < 0
        && Uri.TryCreate(organization, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && AllowedHosts.Any(host => ("." + uri.Host).EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    static string? Set(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
