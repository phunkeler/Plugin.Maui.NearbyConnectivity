using Microsoft.Extensions.Logging;

namespace Harness;

/// <summary>The harness verb over one repository and its pull requests. The CLI is the only caller.</summary>
/// <param name="gitHubRecords">The repository's pull requests.</param>
/// <param name="git">The repository.</param>
/// <param name="processDefinitionReader">Reads <c>harness.json</c> on the base branch.</param>
/// <param name="testImpactReader">Reads a change's test impact and platform reach.</param>
/// <param name="chainBuilder">Reads the work items the pull request names, for the Chain row.</param>
/// <param name="boardsEnvironment">The Azure Boards settings in the environment, which win over the definition.</param>
/// <param name="toolCalls">The calls made to external tools, reported with the verb.</param>
/// <param name="timeProvider">Times the verb.</param>
/// <param name="logger">Receives the verb's events.</param>
sealed class Change(
    IGitHubRecords gitHubRecords,
    IGit git,
    ProcessDefinitionReader processDefinitionReader,
    TestImpactReader testImpactReader,
    ChainBuilder chainBuilder,
    BoardsEnvironment boardsEnvironment,
    ToolCalls toolCalls,
    TimeProvider timeProvider,
    ILogger<Change> logger)
{
    /// <summary>Reports what a pull request touches and what it is linked to. Reads only.</summary>
    /// <param name="record">The pull request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// The report over the diff from the merge base of the base branch, as fetched from its remote, to the head. A base
    /// without <c>harness.json</c> uses the defaults. The Chain row never fails the report: a Boards that cannot be
    /// read is a reason on the row.
    /// </returns>
    /// <exception cref="UsageException">
    /// The base branch is on no remote or on several, the head commit is not fetched, or the base's
    /// <c>harness.json</c> is not valid.
    /// </exception>
    /// <exception cref="RecordNotFoundException">The pull request does not exist.</exception>
    /// <exception cref="RecordUnavailableException">GitHub is unreachable or refused the caller.</exception>
    public async Task<ScopeReport> ScopeAsync(RecordRef record, CancellationToken cancellationToken)
    {
        logger.VerbStarted("scope", record.ToString());
        var started = timeProvider.GetTimestamp();
        try
        {
            return await ReadScopeAsync(record, cancellationToken) with { Run = Measure(started) };
        }
        finally
        {
            var run = Measure(started);
            logger.VerbFinished("scope", run.DurationMilliseconds, run.GitCalls, run.GhCalls, run.AzCalls);
        }
    }

    RunStats Measure(long started) => new(
        DurationMilliseconds: (long)timeProvider.GetElapsedTime(started).TotalMilliseconds,
        GitCalls: toolCalls.CountOf("git"),
        GhCalls: toolCalls.CountOf("gh"),
        AzCalls: toolCalls.CountOf("az"));

    async Task<ScopeReport> ReadScopeAsync(RecordRef record, CancellationToken cancellationToken)
    {
        var (pullRequest, baseRef, definition) = await ReadAsync(record, cancellationToken);
        var mergeBase = await git.FindMergeBaseAsync(baseRef, pullRequest.HeadSha, cancellationToken);
        var changed = await git.ListChangedPathsAsync(mergeBase, pullRequest.HeadSha, cancellationToken);
        var testImpact = definition?.TestImpact ?? TestImpactDefinition.Default;
        var impact = await testImpactReader
            .ReadAsync(
                testImpact,
                changed,
                mergeBase,
                pullRequest.HeadSha,
                cancellationToken);
        var chain = await chainBuilder
            .BuildAsync(
                ScopeReport.WorkItemsIn(pullRequest.Body),
                BoardsDefinition.Resolve(definition?.Boards, boardsEnvironment),
                impact.TestImpact,
                testImpact,
                pullRequest.HeadSha,
                cancellationToken);
        if (chain.NotReadReason is { } reason)
        {
            logger.BoardsNotRead(reason);
        }

        return ScopeReport.Build(pullRequest, impact.TestImpact, impact.PlatformReach, chain);
    }

    // The pull request, its base as fetched from a remote, and the definition on that base; null when the base has
    // none. A pull request's base is a branch name; git has it only as a remote-tracking ref.
    async Task<(PullRequest PullRequest, string BaseRef, ProcessDefinition? Definition)> ReadAsync(
        RecordRef record, CancellationToken cancellationToken)
    {
        var pullRequest = await gitHubRecords.ReadPullRequestAsync(record, cancellationToken);
        var baseRef = await git.FindRemoteBranchAsync(pullRequest.BaseRef, cancellationToken);
        var definition = await processDefinitionReader.FindAsync(baseRef, cancellationToken);
        return (pullRequest, baseRef, definition);
    }
}
