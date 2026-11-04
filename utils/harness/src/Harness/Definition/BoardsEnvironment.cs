namespace Harness;

/// <summary>The Azure Boards settings in the process environment, which win over <c>harness.json</c>.</summary>
/// <param name="Organization">The <c>ADO_ORG</c> variable; <see langword="null"/> when it is not set.</param>
/// <param name="Project">The <c>ADO_PROJECT</c> variable; <see langword="null"/> when it is not set.</param>
sealed record BoardsEnvironment(string? Organization, string? Project)
{
    /// <summary>The environment with neither variable set.</summary>
    public static BoardsEnvironment None { get; } = new(null, null);
}
