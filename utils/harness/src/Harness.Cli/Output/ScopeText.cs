using System.Collections.Immutable;
using System.Globalization;

namespace Harness.Cli;

/// <summary>The wording of the scope report's rows.</summary>
static class ScopeText
{
    /// <summary>The last line of the report, so a sticky comment can be found.</summary>
    public const string Marker = "<!-- harness-scope -->";

    // The Work item link and Chain rows of a description that names no work item.
    const string NoWorkItem = "none: no AB# in the description";

    /// <summary>The report's heading.</summary>
    public const string Heading = "Change scope";

    /// <summary>The pull request row.</summary>
    /// <param name="report">The report.</param>
    /// <returns>Such as <c>#57 `abc1234` on `main` by @login</c>.</returns>
    public static string PullRequest(ScopeReport report) =>
        $"#{Number(report.Number)} `{Sha.Short(report.HeadSha)}` on `{report.BaseRef}` by @{report.Author}";

    /// <summary>The work item link row: whether the description names a work item, which links it in Boards.</summary>
    /// <param name="report">The report.</param>
    /// <returns>
    /// Such as <c>AB#1234 in the description</c>; <c>none: no AB# in the description</c> without one.
    /// </returns>
    public static string WorkItemLink(ScopeReport report) => report.WorkItems.IsEmpty
        ? NoWorkItem
        : string.Join(", ", report.WorkItems.Select(id => "AB#" + Number(id))) + " in the description";

    /// <summary>The platform reach row.</summary>
    /// <param name="report">The report.</param>
    /// <returns>
    /// Such as <c>android, shared → needs an Android device run</c>; <c>shared → shared code only</c> when no
    /// platform is touched; <c>none</c> when no source is touched.
    /// </returns>
    public static string PlatformReach(ScopeReport report)
    {
        var reach = report.PlatformReach;
        var touched = string.Join(", ", reach.Shared ? [.. reach.Platforms, "shared"] : reach.Platforms);
        return reach.Platforms.IsEmpty
            ? reach.Shared ? $"{touched} → shared code only" : "none"
            : $"{touched} → {DeviceRuns(reach.Platforms)}";
    }

    /// <summary>The Test Cases row: each Test Case id the changed tests carry, and the tests that carry it.</summary>
    /// <param name="report">The report.</param>
    /// <returns>
    /// Such as <c>TC 1201: SignOffTests.TC1201_SignsOff; TC 1240: tests/TimeoutTests.cs</c>; <c>none</c> when no
    /// changed test carries an id.
    /// </returns>
    public static string TestCases(ScopeReport report)
    {
        var impact = report.TestImpact;
        return impact.TestCaseIds.IsEmpty
            ? "none"
            : string.Join("; ", impact.TestCaseIds.Select(id => $"TC {Number(id)}: {Tests(impact.TestsFor(id))}"));
    }

    /// <summary>The Chain row: one line per work item the description names.</summary>
    /// <param name="report">The report.</param>
    /// <returns>
    /// Such as <c>AB#4711 User Story "Remember the user" Active [needs-qa] -> TC 4720 "Shows the last user" Ready
    /// [AI-Drafted] -> PR #812 -> tests: TC 4720: RememberedUserTests.TC4720_ShowsLastUserName (changed)</c>, then
    /// <c>searched first 2000 test files</c> when the search of the head stopped at its cap; one line
    /// <c>none: no AB# in the description</c>, or <c>Boards not read: </c> and the reason.
    /// </returns>
    public static IEnumerable<string> Chain(ScopeReport report)
    {
        var chain = report.Chain;
        if (chain.NotReadReason is { } reason)
        {
            return ["Boards not read: " + reason];
        }

        if (chain.Stories.IsEmpty)
        {
            return [NoWorkItem];
        }

        var lines = chain.Stories.Select(story => StoryLine(story, report.Number));
        return chain.CappedSearch is { } searched
            ? [.. lines, $"searched first {Number(searched)} test files"]
            : lines;
    }

    static string StoryLine(ChainStory story, int pullRequest)
    {
        var pr = $"PR #{Number(pullRequest)}";
        if (story.Story is not { } workItem)
        {
            return $"AB#{Number(story.Id)} not found in Boards -> {pr}";
        }

        var head = $"AB#{Number(story.Id)} {workItem.Type} {Describe(workItem)}";
        if (story.TestCases.IsEmpty)
        {
            return $"{head} -> no Test Case -> {pr}";
        }

        var testCases = string.Join(
            ", ", story.TestCases.Select(link => $"TC {Number(link.TestCase.Id)} {Describe(link.TestCase)}"));
        var tests = string.Join(
            "; ", story.TestCases.Select(link => $"TC {Number(link.TestCase.Id)}: {ChainTests(link)}"));
        return $"{head} -> {testCases} -> {pr} -> tests: {tests}";
    }

    // "\"Title\" State [tag, tag]", the tags left out when there are none.
    static string Describe(WorkItem workItem) =>
        $"\"{workItem.Title}\" {workItem.State}"
        + (workItem.Tags.IsEmpty ? "" : $" [{string.Join(", ", workItem.Tags)}]");

    static string Tests(ImmutableArray<string> tests) => tests.IsEmpty ? "none" : string.Join(", ", tests);

    // "A.M (changed), B.N (existing)", or "none".
    static string ChainTests(ChainTestCase link) => Tests(
    [
        .. link.ChangedTests.Select(test => test + " (changed)"),
        .. link.ExistingTests.Select(test => test + " (existing)"),
    ]);

    // "needs an Android device run", "needs Android and iOS device runs", "needs a tizen device run".
    static string DeviceRuns(ImmutableArray<string> platforms)
    {
        var names = platforms.Select(DisplayName).ToList();
        if (names.Count == 1)
        {
            return $"needs {Article(platforms[0])} {names[0]} device run";
        }

        var joined = string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
        return $"needs {joined} device runs";
    }

    static string DisplayName(string platform) => platform switch
    {
        "android" => "Android",
        "ios" => "iOS",
        _ => platform,
    };

    static string Article(string platform) => platform is "android" or "ios" ? "an" : "a";

    static string Number(int number) => number.ToString(CultureInfo.InvariantCulture);
}
