using System.Text.Json;

namespace Harness.Cli;

/// <summary>
/// The <c>--json</c> output: the scope report as one line of compact JSON. The keys are the contract with the
/// workflow and the measures, so they are pinned here, not taken from the members of the records.
/// </summary>
static class JsonOutput
{
    static readonly JsonSerializerOptions s_options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Formats the scope report as one line of compact JSON.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The JSON, without a newline.</returns>
    public static string Format(ScopeReport report) => JsonSerializer.Serialize(
        new
        {
            report.Number,
            report.Title,
            report.Author,
            report.BaseRef,
            report.HeadSha,
            Run = new
            {
                DurationMs = report.Run.DurationMilliseconds,
                report.Run.GitCalls,
                report.Run.GhCalls,
                report.Run.AzCalls,
            },
            report.WorkItems,
            PlatformReach = new { report.PlatformReach.Platforms, report.PlatformReach.Shared },
            TestCases = report.TestImpact.TestCaseIds.Select(id => new
            {
                Id = id,
                TestMethods = report.TestImpact.TestsFor(id),
            }),
            BoardsNotRead = report.Chain.NotReadReason,
            CappedSearch = report.Chain.CappedSearch,
            Chain = report.Chain.Stories.Select(story => new
            {
                story.Id,
                Found = story.Story is not null,
                story.Story?.Type,
                story.Story?.Title,
                story.Story?.State,
                Tags = story.Story?.Tags,
                TestCases = story.TestCases.Select(link => new
                {
                    link.TestCase.Id,
                    link.TestCase.Title,
                    link.TestCase.State,
                    link.TestCase.Tags,
                    TestMethods = link.ChangedTests.Select(test => new { Name = test, Changed = true })
                        .Concat(link.ExistingTests.Select(test => new { Name = test, Changed = false })),
                }),
            }),
        },
        s_options);
}
