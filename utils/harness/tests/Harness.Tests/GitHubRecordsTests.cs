using NSubstitute;
using static Harness.Tests.Create;

namespace Harness.Tests;

// The gh command lines GitHubRecords sends and how it reads their output. Each gh call has one test that pins its
// exact arguments.
public sealed class GitHubRecordsTests
{
    [Theory]
    [InlineData(
        "GraphQL: Could not resolve to a PullRequest with the number of 9.", typeof(RecordNotFoundException))]
    [InlineData("HTTP 401: Bad credentials", typeof(RecordUnavailableException))]
    [InlineData("error connecting to api.github.com", typeof(RecordUnavailableException))]
    public async Task ReadPullRequestAsync_GhFails_ThrowsMatchingRecordException(string standardError, Type expected)
    {
        // Arrange
        var runner = GhRunner();
        runner.Gh("api", "graphql").Returns(Failed(standardError + "\nmore detail\n"));

        // Act
        var exception = await Record.ExceptionAsync(() => new GitHubRecords(runner)
            .ReadPullRequestAsync(new(9), TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType(expected, exception);
        Assert.Equal(standardError, exception?.Message);
    }

    // A gh or GitHub change can alter a response's shape. That is a record system the harness cannot read, exit 5,
    // not a harness bug.
    [Theory]
    [InlineData("A new release of gh is available\n{\"data\":{}}", Label = "not-json")]
    [InlineData("{\"data\":{}}", Label = "missing-field")]
    [InlineData("{\"data\":{\"repository\":7}}", Label = "wrong-type")]
    [InlineData("{\"data\":{\"repository\":{\"pullRequest\":{\"number\":9}}}}", Label = "pull-request-missing-fields")]
    public async Task ReadPullRequestAsync_UnreadableResponse_ThrowsRecordUnavailable(string standardOutput)
    {
        // Arrange
        var runner = GhRunner();
        runner.Gh("api", "graphql").Returns(Ok(standardOutput));

        // Act
        var exception = await Record.ExceptionAsync(() => new GitHubRecords(runner)
            .ReadPullRequestAsync(new(9), TestContext.Current.CancellationToken));

        // Assert
        var unavailable = Assert.IsType<RecordUnavailableException>(exception);
        Assert.NotNull(unavailable.InnerException);
    }

    [Fact]
    public async Task ReadPullRequestAsync_Record_SendsOwnerNameAndNumber()
    {
        // Arrange
        var runner = GhRunner("octo/widgets");
        runner.Gh("api", "graphql").Returns(Ok(PullRequestNode.Create().Response()));

        // Act
        var pullRequest = await new GitHubRecords(runner)
            .ReadPullRequestAsync(new(9), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Sha('a'), pullRequest.HeadSha);
        var query = Assert.Single(runner.RunnerCalls(), call => call.Arguments[0] == "api").Arguments;
        Assert.Equal(["api", "graphql", "-f"], query[..3]);
        Assert.StartsWith("query=", query[3], StringComparison.Ordinal);
        Assert.Equal(["-f", "owner=octo", "-f", "name=widgets", "-F", "number=9"], query[4..]);
    }

    [Fact]
    public async Task ReadPullRequestAsync_CalledTwice_QueriesTheFirstRepositoryName()
    {
        // Arrange
        var runner = GhRunner();
        runner.Gh("repo", "view").Returns(Ok("octo/widgets\n"), Ok("other/repo\n"));
        runner.Gh("api", "graphql").Returns(Ok(PullRequestNode.Create().Response()));
        var gitHubRecords = new GitHubRecords(runner);
        await gitHubRecords.ReadPullRequestAsync(new(9), TestContext.Current.CancellationToken);

        // Act
        await gitHubRecords.ReadPullRequestAsync(new(9), TestContext.Current.CancellationToken);

        // Assert
        var query = runner.RunnerCalls().Last(call => call.Arguments[0] == "api").Arguments;
        Assert.Equal(["-f", "owner=octo", "-f", "name=widgets", "-F", "number=9"], query[4..]);
    }

    [Fact]
    public async Task ReadPullRequestAsync_NullPullRequest_ThrowsRecordNotFound()
    {
        // Arrange
        var runner = GhRunner();
        runner.Gh("api", "graphql").Returns(Ok("""{"data":{"repository":{"pullRequest":null}}}"""));

        // Act
        var exception = await Record.ExceptionAsync(() => new GitHubRecords(runner)
            .ReadPullRequestAsync(new(9), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("#9 not found", Assert.IsType<RecordNotFoundException>(exception).Message);
    }

    [Fact]
    public async Task ReadPullRequestAsync_AddressOfThisRepositoryInOtherCase_ReadsIt()
    {
        // Arrange
        var runner = GhRunner("Dotnet/MAUI");
        runner.Gh("api", "graphql").Returns(Ok(PullRequestNode.Create(number: 21).Response()));
        var record = RecordRef.Parse("https://github.com/dotnet/maui/pull/21/files");

        // Act
        var pullRequest = await new GitHubRecords(runner)
            .ReadPullRequestAsync(record, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(21, pullRequest.Number);
    }

    [Fact]
    public async Task ReadPullRequestAsync_AddressOfAnotherRepository_ThrowsUsageException()
    {
        // Arrange
        var runner = GhRunner("owner/repo");
        var record = RecordRef.Parse("https://github.com/dotnet/maui/pull/21");

        // Act
        var exception = await Record.ExceptionAsync(() => new GitHubRecords(runner)
            .ReadPullRequestAsync(record, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "dotnet/maui#21: this clone is owner/repo; run harness scope in a clone of dotnet/maui",
            Assert.IsType<UsageException>(exception).Message);
    }

    static ProcessResult Ok(string standardOutput) => new(0, standardOutput, "");

    static ProcessResult Failed(string standardError) => new(1, "", standardError);
}
