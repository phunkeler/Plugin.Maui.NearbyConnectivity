using NSubstitute;

namespace Harness.Tests;

static class SubstituteExtensions
{
    // ReadPullRequestAsync of the node's record returns the node, mapped as GitHubRecords maps it.
    public static void ReadReturns(this IGitHubRecords gitHubRecords, PullRequestNode node) =>
        gitHubRecords
            .ReadPullRequestAsync(new RecordRef(node.Number), Arg.Any<CancellationToken>())
            .Returns(_ => node.ToPullRequest());

    // A `gh` run whose first two arguments match, for Returns.
    public static Task<ProcessResult> Gh(this IProcessRunner runner, string first, string second) =>
        runner.RunAsync(
            "gh",
            Arg.Is<IReadOnlyList<string>>(arguments =>
                arguments.Count >= 2 && arguments[0] == first && arguments[1] == second),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

    // An `az rest` run for one work item id, for Returns.
    public static Task<ProcessResult> AzWorkItem(this IProcessRunner runner, int id) =>
        runner.RunAsync(
            Arg.Is<string>(fileName => fileName == "az" || fileName == "az.cmd"),
            Arg.Is<IReadOnlyList<string>>(arguments =>
                arguments.Count > 0 && arguments[0] == "rest"
                && arguments.Any(argument => argument.EndsWith($"/_apis/wit/workitems/{id}", StringComparison.Ordinal))),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

    // Every `gh` run the runner received, in order.
    public static List<RunnerCall> RunnerCalls(this IProcessRunner runner) => Calls(runner, "gh");

    // Every `az` run the runner received, in order.
    public static List<RunnerCall> AzCalls(this IProcessRunner runner) => Calls(runner, "az");

    static List<RunnerCall> Calls(IProcessRunner runner, string fileName) =>
    [
        .. runner.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IProcessRunner.RunAsync))
            .Select(call => call.GetArguments())
            .Where(arguments => ((string?)arguments[0])?.StartsWith(fileName, StringComparison.Ordinal) == true)
            .Select(arguments => new RunnerCall([.. (IReadOnlyList<string>)arguments[1]!], (string?)arguments[3])),
    ];
}
