using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Harness;

/// <summary>
/// What a pull request touches and what it is linked to: the work items its description names, the platforms it
/// reaches, the Test Cases its tests carry and the chain from each story to its tests. A report, never a verdict.
/// </summary>
/// <param name="Number">The pull request number.</param>
/// <param name="Title">The pull request title.</param>
/// <param name="Author">The author's login, in its canonical spelling.</param>
/// <param name="BaseRef">The base branch, without <c>origin/</c>.</param>
/// <param name="HeadSha">The head commit.</param>
/// <param name="WorkItems">
/// The Azure Boards work items the description names as <c>AB#&lt;id&gt;</c>, distinct and ascending. The title does
/// not count: Azure Boards links a work item from the description or a commit only.
/// </param>
/// <param name="TestImpact">The changed tests and the Test Cases they carry.</param>
/// <param name="PlatformReach">The platforms the change reaches.</param>
/// <param name="Chain">What Azure Boards says about each work item in <paramref name="WorkItems"/>.</param>
sealed partial record ScopeReport(
    int Number,
    string Title,
    string Author,
    string BaseRef,
    string HeadSha,
    ImmutableArray<int> WorkItems,
    TestImpact TestImpact,
    PlatformReach PlatformReach,
    Chain Chain)
{
    /// <summary>What the run that built the report cost. Not part of the report's facts: it differs on every run.</summary>
    public RunStats Run { get; init; } = RunStats.None;

    /// <summary>Builds the report.</summary>
    /// <param name="pullRequest">The pull request.</param>
    /// <param name="testImpact">The tests the change edits, read by <see cref="TestImpactReader"/>.</param>
    /// <param name="platformReach">The platforms the change reaches, read by <see cref="TestImpactReader"/>.</param>
    /// <param name="chain">The chain, built by <see cref="ChainBuilder"/> from the same work items.</param>
    /// <returns>The report.</returns>
    public static ScopeReport Build(
        PullRequest pullRequest, TestImpact testImpact, PlatformReach platformReach, Chain chain)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        return new(
            Number: pullRequest.Number,
            Title: pullRequest.Title,
            Author: pullRequest.Author.Id,
            BaseRef: pullRequest.BaseRef,
            HeadSha: pullRequest.HeadSha,
            WorkItems: WorkItemsIn(pullRequest.Body),
            TestImpact: testImpact,
            PlatformReach: platformReach,
            Chain: chain);
    }

    /// <summary>Finds the work items a pull request description names.</summary>
    /// <param name="description">The description; <see langword="null"/> when it is empty.</param>
    /// <returns>The ids of every <c>AB#&lt;id&gt;</c>, distinct and ascending.</returns>
    public static ImmutableArray<int> WorkItemsIn(string? description) =>
    [
        .. WorkItemReference().Matches(description ?? "")
            .Select(match => int.TryParse(
                match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : (int?)null)
            .OfType<int>()
            .Distinct()
            .Order(),
    ];

    [GeneratedRegex(@"\bAB#(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex WorkItemReference();
}
