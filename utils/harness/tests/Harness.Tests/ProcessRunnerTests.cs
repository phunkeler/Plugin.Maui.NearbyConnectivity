namespace Harness.Tests;

// The process runner against real child processes: what reaches the child and what comes back.
[Collection(CurrentDirectoryCollection.Name)]
public sealed class ProcessRunnerTests : IDisposable
{
    readonly TempRepo _repo = TempRepo.Create();

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task RunAsync_ChildExitsBeforeReadingStandardInput_KeepsStandardError()
    {
        // Arrange
        // Larger than a pipe buffer, so the write fails after the child has exited.
        var standardInput = new string('x', 1 << 20);

        // Act
        var result = await new ProcessRunner().RunAsync(
            "/bin/sh",
            ["-c", "echo nope >&2; exit 3"],
            workingDirectory: null,
            standardInput,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("nope\n", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_ProgramRunsPastTheBound_KillsItAndThrowsTimeout()
    {
        // Arrange
        var runner = new ProcessRunner(TimeSpan.FromMilliseconds(200));

        // Act
        var exception = await Record.ExceptionAsync(() => runner.RunAsync(
            "/bin/sh", ["-c", "sleep 30"], workingDirectory: null, standardInput: null,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.StartsWith(
            "/bin/sh -c did not finish within",
            Assert.IsType<TimeoutException>(exception).Message,
            StringComparison.Ordinal);
    }

    // A pull request can add an executable named like a tool at the repository root, and in CI the harness runs with
    // that root as its own current directory, where Process.Start looks before PATH.
    [Fact]
    public async Task RunAsync_ExecutableOfTheSameNameInTheCurrentDirectory_RunsTheOneOnPath()
    {
        // Arrange
        _repo.Write("git", "#!/bin/sh\necho PLANTED\n", executable: true);
        using var currentDirectory = new CurrentDirectory(_repo.Root);

        // Act
        var result = await new ProcessRunner().RunAsync(
            "git", ["--version"], workingDirectory: null, standardInput: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("git version", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ProgramNotOnPath_ThrowsUsageExceptionNamingIt()
    {
        // Arrange
        const string missing = "harness-no-such-program";

        // Act
        var exception = await Record.ExceptionAsync(() => new ProcessRunner().RunAsync(
            missing, [], workingDirectory: null, standardInput: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            $"{missing} is not installed or not on PATH", Assert.IsType<UsageException>(exception).Message);
    }

    // Moves the process's current directory for one test and moves it back.
    sealed class CurrentDirectory : IDisposable
    {
        readonly string _previous = Directory.GetCurrentDirectory();

        public CurrentDirectory(string path) => Directory.SetCurrentDirectory(path);

        public void Dispose() => Directory.SetCurrentDirectory(_previous);
    }
}
