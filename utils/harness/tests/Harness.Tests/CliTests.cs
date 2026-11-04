using System.Text.Json;
using Harness.Cli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static Harness.Tests.Create;

namespace Harness.Tests;

// The command line: option rules, output and exit codes. The command runs against the services AddHarness registers,
// with GitHub records, and where a test needs no repository git, replaced by substitutes. One test runs the CLI as a
// real process, because the console logger and stderr are process-wide.
public sealed class CliTests : IDisposable
{
    readonly TempRepo _repo = TempRepo.Create();
    readonly DirectoryInfo _outside = Directory.CreateTempSubdirectory("harness-outside-");

    public void Dispose()
    {
        _repo.Dispose();
        _outside.Delete(recursive: true);
    }

    [Theory]
    [InlineData("scope", "scope needs <record>")]
    [InlineData("scope 0", "0: expected a pull request number such as #57")]
    [InlineData("scope AB#1", "AB#1: a work item is not a pull request")]
    [InlineData("scope #1 --html", "'--html'")]
    [InlineData("scope #1 --base origin/main", "'--base'")]
    [InlineData("status #1", "'status'")]
    [InlineData("test", "'test'")]
    [InlineData("measure", "'measure'")]
    public async Task RunAsync_UsageError_PrintsTheErrorAndExits2(string line, string error)
    {
        // Arrange
        using var services = Services(_repo.Root, GitHubRecords(), Substitute.For<IGit>());
        var (stdout, stderr) = (new StringWriter(), new StringWriter());

        // Act
        var code = await Program.RunAsync(
            line.Split(' '), stdout, stderr, _ => services, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, code);
        Assert.Equal("", stdout.ToString());
        Assert.Contains(error, stderr.ToString(), StringComparison.Ordinal);
    }

    // <record> accepts any token, so an unknown option would otherwise be parsed as a pull request number.
    [Fact]
    public async Task RunAsync_UnknownOption_PrintsOnlyUnrecognizedAndExits2()
    {
        // Arrange
        var gitHubRecords = Substitute.For<IGitHubRecords>();
        using var services = Services(_repo.Root, gitHubRecords, Substitute.For<IGit>());
        var stderr = new StringWriter();

        // Act
        var code = await Program.RunAsync(
            ["scope", "--bogus"], new StringWriter(), stderr, _ => services, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, code);
        Assert.Equal(
            "Unrecognized command or argument '--bogus'.\n", stderr.ToString().ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Main_Scope_WritesLogsToStderrOnly()
    {
        // Arrange
        // The console logger writes to the process's stdout unless it is sent to stderr, so only a real process can
        // show where the logs go. `scope` logs its first event, then fails before any network call: the repository
        // has no remote for gh to read, or gh is not installed.
        _repo.Write("README.md", "readme");
        _repo.Commit("init");
        string[] arguments = ["scope", "#1", "--json"];

        // Act
        var (code, stdout, stderr) = await RunProcessAsync(arguments);

        // Assert
        Assert.NotEqual(0, code);
        Assert.Equal("", stdout);
        Assert.Contains("scope #1", stderr, StringComparison.Ordinal);
        Assert.Contains("scope finished in", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("dbug:", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Main_ScopeVerbose_LogsEachToolCallToStderr()
    {
        // Arrange
        _repo.Write("README.md", "readme");
        _repo.Commit("init");
        string[] arguments = ["scope", "#1", "--json", "--verbose"];

        // Act
        var (_, stdout, stderr) = await RunProcessAsync(arguments);

        // Assert
        Assert.Equal("", stdout);
        Assert.Contains("git rev-parse exited 0 in", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Scope_PrintsTheMarkdownAndExits0()
    {
        // Arrange
        var head = WriteBranch();
        var gitHubRecords = GitHubRecords();
        gitHubRecords.ReadReturns(PullRequestNode.Create(number: 1, head: head));
        using var services = Services(_repo.Root, gitHubRecords);
        var stdout = new StringWriter();

        // Act
        var code = await Program.RunAsync(
            ["scope", "#1"], stdout, new StringWriter(), _ => services, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, code);
        var lines = stdout.ToString().ReplaceLineEndings("\n").TrimEnd().Split('\n');
        Assert.Equal("### Change scope", lines[0]);
        Assert.Equal("<!-- harness-scope -->", lines[^1]);
    }

    [Fact]
    public async Task RunAsync_ScopeJson_PrintsOneLineOfJson()
    {
        // Arrange
        var head = WriteBranch();
        var gitHubRecords = GitHubRecords();
        gitHubRecords.ReadReturns(PullRequestNode.Create(number: 1, head: head));
        using var services = Services(_repo.Root, gitHubRecords);
        var stdout = new StringWriter();

        // Act
        var code = await Program.RunAsync(
            ["scope", "#1", "--json"], stdout, new StringWriter(), _ => services, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, code);
        Assert.StartsWith("{\"number\":1,", stdout.ToString(), StringComparison.Ordinal);
        Assert.Single(stdout.ToString().TrimEnd().Split('\n'));
    }

    [Fact]
    public async Task RunAsync_ScopeJson_ReportsTheDurationAndTheToolCalls()
    {
        // Arrange
        var head = WriteBranch();
        var clock = new FakeTimeProvider();
        var gitHubRecords = GitHubRecords();
        gitHubRecords.ReadReturns(PullRequestNode.Create(number: 1, head: head));
        gitHubRecords
            .When(records => records.ReadPullRequestAsync(Arg.Any<RecordRef>(), Arg.Any<CancellationToken>()))
            .Do(_ => clock.Advance(TimeSpan.FromSeconds(3)));
        using var services = Services(_repo.Root, gitHubRecords, clock: clock);
        var stdout = new StringWriter();

        // Act
        await Program.RunAsync(
            ["scope", "#1", "--json"], stdout, new StringWriter(), _ => services, TestContext.Current.CancellationToken);

        // Assert
        using var json = JsonDocument.Parse(stdout.ToString());
        var run = json.RootElement.GetProperty("run");
        Assert.Equal(3000, run.GetProperty("durationMs").GetInt64());
        Assert.True(run.GetProperty("gitCalls").GetInt32() > 0);
        Assert.Equal(0, run.GetProperty("ghCalls").GetInt32());
        Assert.Equal(0, run.GetProperty("azCalls").GetInt32());
    }

    [Fact]
    public async Task RunAsync_OutsideARepository_ExitsTwoBeforeAnyGitHubCall()
    {
        // Arrange
        var gitHubRecords = GitHubRecords();
        using var services = Services(_outside.FullName, gitHubRecords);
        var stderr = new StringWriter();

        // Act
        var code = await Program.RunAsync(
            ["scope", "#1"], new StringWriter(), stderr, _ => services, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, code);
        Assert.StartsWith("git rev-parse:", stderr.ToString(), StringComparison.Ordinal);
    }

    // Nothing is resolved for the help, so it works where git or GitHub cannot be reached.
    [Fact]
    public async Task RunAsync_Help_BuildsNoServices()
    {
        // Arrange
        static ServiceProvider Refuse(LogLevel _) => throw new InvalidOperationException("services were built");

        // Act
        var code = await Program.RunAsync(
            ["--help"], new StringWriter(), new StringWriter(), Refuse, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, code);
    }

    [Theory]
    [InlineData("scope #1", LogLevel.Information)]
    [InlineData("scope #1 --verbose", LogLevel.Debug)]
    [InlineData("scope -v #1", LogLevel.Debug)]
    public async Task RunAsync_Verbose_BuildsTheServicesAtDebug(string line, LogLevel expected)
    {
        // Arrange
        var head = WriteBranch();
        var gitHubRecords = GitHubRecords();
        gitHubRecords.ReadReturns(PullRequestNode.Create(number: 1, head: head));
        using var services = Services(_repo.Root, gitHubRecords);
        var levels = new List<LogLevel>();
        ServiceProvider Record(LogLevel level)
        {
            levels.Add(level);
            return services;
        }

        // Act
        var code = await Program.RunAsync(
            line.Split(' '), new StringWriter(), new StringWriter(), Record, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, code);
        Assert.Equal([expected], levels);
    }

    [Fact]
    public async Task RunAsync_UnexpectedException_PrintsItsStackOnStderr()
    {
        // Arrange
        var gitHubRecords = Substitute.For<IGitHubRecords>();
        gitHubRecords.ReadPullRequestAsync(Arg.Any<RecordRef>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("no change"));
        using var services = Services(_repo.Root, gitHubRecords, Substitute.For<IGit>());
        var stderr = new StringWriter();

        // Act
        var code = await Program.RunAsync(
            ["scope", "#1"], new StringWriter(), stderr, _ => services, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, code);
        var text = stderr.ToString();
        Assert.Contains("harness bug: System.InvalidOperationException: no change", text, StringComparison.Ordinal);
        Assert.Contains("   at ", text, StringComparison.Ordinal);
    }

    // A signal cancels the command, which exits 130 like any interrupted process, not 5.
    [Fact]
    public async Task RunAsync_Cancelled_PrintsCancelledAndExits130()
    {
        // Arrange
        var gitHubRecords = Substitute.For<IGitHubRecords>();
        gitHubRecords.ReadPullRequestAsync(Arg.Any<RecordRef>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException("signal"));
        using var services = Services(_repo.Root, gitHubRecords, Substitute.For<IGit>());
        var stderr = new StringWriter();

        // Act
        var code = await Program.RunAsync(
            ["scope", "#1"], new StringWriter(), stderr, _ => services, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(130, code);
        Assert.Equal("Cancelled.\n", stderr.ToString().ReplaceLineEndings("\n"));
    }

    // The help names the tool command, whatever the process or assembly is called.
    [Theory]
    [InlineData("--help", "  harness [command] [options]")]
    [InlineData("scope --help", "  harness scope [<record>] [options]")]
    public async Task RunAsync_Help_NamesTheCommandHarness(string line, string usage)
    {
        // Arrange
        using var services = Services(_repo.Root, Substitute.For<IGitHubRecords>(), Substitute.For<IGit>());
        var stdout = new StringWriter();

        // Act
        var code = await Program.RunAsync(
            line.Split(' '), stdout, new StringWriter(), _ => services, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, code);
        var lines = stdout.ToString().ReplaceLineEndings("\n").Split('\n');
        Assert.Equal(usage, lines[Array.IndexOf(lines, "Usage:") + 1]);
    }

    [Fact]
    public async Task RunAsync_Version_PrintsTheVersionAndExits0()
    {
        // Arrange
        using var services = Services(_repo.Root, Substitute.For<IGitHubRecords>(), Substitute.For<IGit>());
        var stdout = new StringWriter();

        // Act
        var code = await Program.RunAsync(
            ["--version"], stdout, new StringWriter(), _ => services, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, code);
        Assert.Matches(@"^\d+\.\d+\.\d+", stdout.ToString());
    }

    // main holds src/A.cs and is pushed to origin; the branch `work` adds a test. Returns the branch's head.
    string WriteBranch()
    {
        _repo.Write("src/A.cs", "one\n");
        _repo.Commit("base");
        _repo.AddOrigin();
        _repo.Git("checkout -q -b work");
        _repo.Write("tests/ATests.cs", "// TC1201\n");
        return _repo.Commit("work");
    }

    // Runs the CLI as a real process in the repository: the console logger and stderr are process-wide.
    Task<ProcessResult> RunProcessAsync(string[] arguments) =>
        new ProcessRunner().RunAsync(
            "dotnet",
            [Path.Combine(AppContext.BaseDirectory, "Harness.Cli.dll"), .. arguments],
            _repo.Root,
            null,
            TestContext.Current.CancellationToken);
}
