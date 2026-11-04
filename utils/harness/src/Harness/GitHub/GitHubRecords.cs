using System.Globalization;
using System.Text.Json;

namespace Harness;

/// <summary>The GitHub records of the current repository, through the gh command line.</summary>
/// <param name="processRunner">Runs gh.</param>
sealed class GitHubRecords(IProcessRunner processRunner) : IGitHubRecords
{
    const string Query = $$"""
        query($owner: String!, $name: String!, $number: Int!) {
          repository(owner: $owner, name: $name) { pullRequest(number: $number) { {{PullRequestMapping.Fields}} } }
        }
        """;

    string? _repository;

    public async Task<string> ReadRepositoryNameAsync(CancellationToken cancellationToken) => _repository ??=
        (await RunAsync(
                ["repo", "view", "--json", "nameWithOwner", "-q", ".nameWithOwner"],
                cancellationToken)).Trim();

    public async Task<PullRequest> ReadPullRequestAsync(RecordRef record, CancellationToken cancellationToken)
    {
        var repository = await ReadRepositoryNameAsync(cancellationToken);
        if (record.Repository is { } named && !string.Equals(named, repository, StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException($"{record}: this clone is {repository}; run harness scope in a clone of {named}");
        }

        var slash = repository.IndexOf('/', StringComparison.Ordinal);
        var stdout = await RunAsync(
            [
                "api", "graphql", "-f", "query=" + Query,
                "-f", "owner=" + repository[..slash],
                "-f", "name=" + repository[(slash + 1)..],
                "-F", "number=" + record.Number.ToString(CultureInfo.InvariantCulture),
            ],
            cancellationToken);
        return Read(() =>
        {
            using var document = JsonDocument.Parse(stdout);
            var pullRequest = document.RootElement.GetProperty("data").GetProperty("repository")
                .GetProperty("pullRequest");
            return pullRequest.ValueKind == JsonValueKind.Null
                ? throw new RecordNotFoundException($"{record} not found")
                : PullRequestMapping.Map(pullRequest);
        });
    }

    // A read that finds no pull request, or gets a 404, is RecordNotFoundException; any other failure is unavailable.
    async Task<string> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var (exitCode, stdout, stderr) = await processRunner
            .RunAsync("gh", arguments, workingDirectory: null, standardInput: null, cancellationToken);
        if (exitCode == 0)
        {
            return stdout;
        }

        var firstLine = stderr.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0)
            ?? $"gh {arguments[0]} {arguments[1]} exited with code {exitCode.ToString(CultureInfo.InvariantCulture)}";
        throw (stderr.Contains("Could not resolve to a PullRequest", StringComparison.Ordinal)
                || stderr.Contains("HTTP 404", StringComparison.Ordinal)
                || stderr.Contains("no pull requests found", StringComparison.Ordinal))
            ? new RecordNotFoundException(firstLine)
            : new RecordUnavailableException(firstLine);
    }

    // The boundary between a gh response and the harness. A response whose shape the harness does not know, after a gh
    // or GitHub change, is a record system the harness cannot read: exit 5, not a harness bug. Only reads of a
    // response go in here, so an exception from the harness's own logic still surfaces as a bug.
    static T Read<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception)
            when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new RecordUnavailableException(
                "GitHub sent a response the harness cannot read: " + exception.Message, exception);
        }
    }
}
