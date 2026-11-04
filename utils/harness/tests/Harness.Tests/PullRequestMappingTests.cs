using static Harness.Tests.Create;

namespace Harness.Tests;

// What a GraphQL pull request node maps to.
public sealed class PullRequestMappingTests
{
    [Fact]
    public void Map_Node_ReadsEveryField()
    {
        // Arrange
        var element = PullRequestNode.Create(
                author: "Copilot[bot]", number: 21, title: "Fix", body: "AB#1", baseRef: "release", head: Sha('c'))
            .Node();

        // Act
        var pullRequest = PullRequestMapping.Map(element);

        // Assert
        Assert.Equal(
            new PullRequest(21, "Fix", "AB#1", Identity.FromGitHubLogin("copilot"), "release", Sha('c')), pullRequest);
    }

    [Fact]
    public void Map_DeletedAuthor_IsTheGhostUser()
    {
        // Arrange
        var element = PullRequestNode.Create(author: null).Node();

        // Act
        var pullRequest = PullRequestMapping.Map(element);

        // Assert
        Assert.Equal(Identity.FromGitHubLogin("ghost"), pullRequest.Author);
    }

    [Fact]
    public void Map_EmptyBody_IsNull()
    {
        // Arrange
        var element = PullRequestNode.Create(body: null).Node();

        // Act
        var pullRequest = PullRequestMapping.Map(element);

        // Assert
        Assert.Null(pullRequest.Body);
    }
}
