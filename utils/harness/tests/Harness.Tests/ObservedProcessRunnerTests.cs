using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Harness.Tests;

public sealed class ObservedProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_SeveralTools_CountsEachToolAndReturnsTheInnerResult()
    {
        // Arrange
        var answer = new ProcessResult(0, "out", "");
        var inner = Substitute.For<IProcessRunner>();
        inner.RunAsync(default!, default!, default, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(answer);
        var calls = new ToolCalls();
        var runner = new ObservedProcessRunner(
            inner, calls, TimeProvider.System, NullLogger<ObservedProcessRunner>.Instance);
        var cancellationToken = TestContext.Current.CancellationToken;
        await runner.RunAsync("git", ["status"], null, null, cancellationToken);

        // Act
        var result = await runner.RunAsync("git", ["log"], null, null, cancellationToken);

        // Assert
        Assert.Same(answer, result);
        Assert.Equal(2, calls.CountOf("git"));
        Assert.Equal(0, calls.CountOf("az"));
    }

    [Fact]
    public async Task RunAsync_ToolFails_StillCountsTheCall()
    {
        // Arrange
        var inner = Substitute.For<IProcessRunner>();
        inner.RunAsync(default!, default!, default, default, Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs<ProcessResult>(_ => throw new TimeoutException("hung"));
        var calls = new ToolCalls();
        var runner = new ObservedProcessRunner(
            inner, calls, TimeProvider.System, NullLogger<ObservedProcessRunner>.Instance);

        // Act
        var exception = await Record.ExceptionAsync(
            () => runner.RunAsync("gh", ["api"], null, null, TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<TimeoutException>(exception);
        Assert.Equal(1, calls.CountOf("gh"));
    }
}
