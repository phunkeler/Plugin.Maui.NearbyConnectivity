using System.Text.Json;

namespace Harness;

/// <summary>Maps a GraphQL pull request node to a <see cref="PullRequest"/>. Pure.</summary>
static class PullRequestMapping
{
    /// <summary>The GraphQL fields <see cref="Map"/> reads.</summary>
    public const string Fields = "number title body baseRefName headRefOid author { login }";

    /// <summary>Maps a pull request node.</summary>
    /// <param name="pullRequest">The node, with the <see cref="Fields"/>.</param>
    /// <returns>The pull request. A deleted author is the <c>ghost</c> user.</returns>
    /// <exception cref="RecordUnavailableException">A field GitHub declares non-null is null.</exception>
    public static PullRequest Map(JsonElement pullRequest) => new(
        pullRequest.GetProperty("number").GetInt32(),
        Required(pullRequest, "title"),
        pullRequest.GetProperty("body").GetString(),
        Login(pullRequest.GetProperty("author")) ?? Identity.FromGitHubLogin(GitHubApi.GhostLogin),
        Required(pullRequest, "baseRefName"),
        Required(pullRequest, "headRefOid"));

    // A string GitHub's schema declares non-null; a null means the response is not one this code understands.
    static string Required(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new RecordUnavailableException($"GitHub returned null {name}");

    // A deleted user has no actor or no login: null.
    static Identity? Login(JsonElement actor) =>
        actor.ValueKind == JsonValueKind.Object && actor.GetProperty("login").GetString() is { } login
            ? Identity.FromGitHubLogin(login)
            : null;
}
