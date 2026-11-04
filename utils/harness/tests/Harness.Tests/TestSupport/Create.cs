using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core;

namespace Harness.Tests;

// The subjects and services the tests build, so a constructor change touches one file.
static class Create
{
    public const string Repository = "owner/repo";

    // Where the Boards fixtures live.
    public static BoardsDefinition Boards { get; } = new("https://dev.azure.com/fabrikam", "Mobile App");

    // A change on #57 with every row filled: one story with one Test Case, carried by one test method. Each test
    // changes what it looks at.
    public static ScopeReport FullScope { get; } = new(
        Number: 57,
        Title: "Fix the login timeout",
        Author: "copilot",
        BaseRef: "main",
        HeadSha: Sha('a'),
        WorkItems: [1234],
        TestImpact: TestImpact.Of(
        [
            new("uitest/Login/TimeoutTests.cs", [1240], [new("TimeoutTests.Times_out_TC1240", [1240])], []),
        ]),
        PlatformReach: new(["android"], Shared: true),
        Chain: new(
            null,
            [
                new(
                    1234,
                    WorkItem(1234, "User Story", "Time out an idle login", "Active", "needs-qa"),
                    [
                        new(
                            WorkItem(1240, "Test Case", "Idle login times out", "Ready", "AI-Drafted"),
                            ["TimeoutTests.Times_out_TC1240"],
                            []),
                    ]),
            ]));

    // A 40-hex SHA or tree made from one character, such as Sha('a').
    public static string Sha(char character) => new(character, 40);

    // A work item with no relations.
    public static WorkItem WorkItem(int id, string type, string title, string state, params string[] tags) =>
        new(id, type, title, state, [.. tags], []);

    // A Change over the repository at root with real git. Boards answers nothing unless a test passes its own.
    public static Change Change(
        string root,
        IGitHubRecords gitHubRecords,
        IWorkItems? workItems = null,
        BoardsEnvironment? boardsEnvironment = null)
    {
        var git = new Git(new ProcessRunner(), root);
        return new(
            gitHubRecords,
            git,
            new ProcessDefinitionReader(git),
            new TestImpactReader(git),
            new ChainBuilder(WorkItemsFactory(workItems ?? WorkItems()), new TestImpactReader(git)),
            boardsEnvironment ?? BoardsEnvironment.None,
            new ToolCalls(),
            TimeProvider.System,
            NullLogger<Change>.Instance);
    }

    // GitHub records that answer nothing: every call a test did not answer fails with the member's name, so an
    // unexpected GitHub call fails the test. The answer is a faulted task, not a throw, so a test can still configure
    // the call afterwards. Each answer is made when the call is made: a faulted task made in advance and never awaited
    // is reported as an unobserved task exception.
    public static IGitHubRecords GitHubRecords()
    {
        var gitHubRecords = Substitute.For<IGitHubRecords>();
        gitHubRecords.ReadRepositoryNameAsync(Arg.Any<CancellationToken>())
            .Returns(NoAnswer<string>(nameof(IGitHubRecords.ReadRepositoryNameAsync)));
        gitHubRecords.ReadPullRequestAsync(Arg.Any<RecordRef>(), Arg.Any<CancellationToken>())
            .Returns(NoAnswer<PullRequest>(nameof(IGitHubRecords.ReadPullRequestAsync)));
        return gitHubRecords;
    }

    // Azure Boards that answers nothing, the same way.
    public static IWorkItems WorkItems()
    {
        var workItems = Substitute.For<IWorkItems>();
        workItems.ReadWorkItemAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(NoAnswer<WorkItem?>(nameof(IWorkItems.ReadWorkItemAsync)));
        workItems.ReadLinkedTestCasesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(NoAnswer<ImmutableArray<WorkItem>>(nameof(IWorkItems.ReadLinkedTestCasesAsync)));
        return workItems;
    }

    // A factory that opens the given work items for Boards only, so a chain read for another project fails.
    public static IWorkItemsFactory WorkItemsFactory(IWorkItems workItems)
    {
        var factory = Substitute.For<IWorkItemsFactory>();
        factory.Create(Arg.Any<BoardsDefinition>())
            .Returns(call => call.Arg<BoardsDefinition>() == Boards
                ? workItems
                : throw new InvalidOperationException($"no Boards at {call.Arg<BoardsDefinition>()}"));
        return factory;
    }

    // A process runner that answers `gh repo view` with the repository. A call no test answered fails with its
    // command line, where a bare substitute would return null and fail later with a NullReferenceException.
    public static IProcessRunner GhRunner(string repository = Repository)
    {
        var runner = RunnerAnsweringNothing();
        runner.Gh("repo", "view").Returns(new ProcessResult(0, repository + "\n", ""));
        return runner;
    }

    // A process runner that answers nothing: a call no test answered fails with its command line.
    public static IProcessRunner RunnerAnsweringNothing()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromException<ProcessResult>(new InvalidOperationException(
                $"no answer for: {call.ArgAt<string>(0)} {string.Join(' ', call.ArgAt<IReadOnlyList<string>>(1))}")));
        return runner;
    }

    // The CLI's services over a working directory, as Program registers them, with the GitHub records replaced, no
    // Boards settings from the environment, and, when given, git replaced.
    public static ServiceProvider Services(
        string workingDirectory, IGitHubRecords gitHubRecords, IGit? git = null, TimeProvider? clock = null)
    {
        var services = new ServiceCollection()
            .AddHarness(options => options.WorkingDirectory = workingDirectory)
            .AddSingleton(gitHubRecords)
            .AddSingleton(BoardsEnvironment.None);
        if (git is not null)
        {
            services.AddSingleton(git);
        }

        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    // A definition with one text replaced. Throws when the text is not there, so a stale edit cannot leave the
    // definition unchanged without notice.
    public static string Definition(string definition, string old, string replacement) =>
        definition.Contains(old, StringComparison.Ordinal)
            ? definition.Replace(old, replacement, StringComparison.Ordinal)
            : throw new InvalidOperationException($"the definition has no {old}");

    static Func<CallInfo, Task<T>> NoAnswer<T>(string member) => _ => Task.FromException<T>(NoAnswerFor(member));

    static InvalidOperationException NoAnswerFor(string member) => new($"no answer for {member}");
}
