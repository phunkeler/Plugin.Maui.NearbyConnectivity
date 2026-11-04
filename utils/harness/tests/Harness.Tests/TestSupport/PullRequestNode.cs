using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harness.Tests;

// Builds the GraphQL pullRequest node that GitHubRecords reads. Change tests map it to a PullRequest, so every test
// reads the same record GitHubRecords would return.
public sealed class PullRequestNode
{
    readonly JsonObject _pullRequest;

    PullRequestNode(JsonObject pullRequest, int number) => (_pullRequest, Number) = (pullRequest, number);

    public int Number { get; }

    // An open pull request; the head defaults to Sha('a').
    public static PullRequestNode Create(
        string? author = "Copilot",
        int number = 1,
        string title = "Title",
        string? body = null,
        string baseRef = "main",
        string? head = null) =>
        new(
            new JsonObject
            {
                ["number"] = number,
                ["title"] = title,
                ["body"] = body,
                ["baseRefName"] = baseRef,
                ["headRefOid"] = head ?? Harness.Tests.Create.Sha('a'),
                ["author"] = author is null ? null : new JsonObject { ["login"] = author },
            },
            number);

    // The pullRequest node, as PullRequestMapping.Map takes it.
    public JsonElement Node() => JsonSerializer.SerializeToElement(_pullRequest);

    // The stdout of `gh api graphql` for the single pull request query.
    public string Response() =>
        """{"data":{"repository":{"pullRequest":""" + _pullRequest.ToJsonString() + "}}}";

    // The pull request GitHubRecords maps from this node.
    internal PullRequest ToPullRequest() => PullRequestMapping.Map(Node());
}
