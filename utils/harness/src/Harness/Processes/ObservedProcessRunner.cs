using Microsoft.Extensions.Logging;

namespace Harness;

/// <summary>Counts each program a runner starts, and logs how it ended at <see cref="LogLevel.Debug"/>.</summary>
/// <param name="inner">The runner that starts the program.</param>
/// <param name="calls">Receives the count.</param>
/// <param name="timeProvider">Times the call.</param>
/// <param name="logger">Receives the event, which names the tool and its first argument only.</param>
sealed class ObservedProcessRunner(
    IProcessRunner inner,
    ToolCalls calls,
    TimeProvider timeProvider,
    ILogger<ObservedProcessRunner> logger) : IProcessRunner
{
    /// <inheritdoc/>
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        calls.Add(fileName);
        var started = timeProvider.GetTimestamp();
        var result = await inner.RunAsync(fileName, arguments, workingDirectory, standardInput, cancellationToken);
        logger.ToolFinished(
            fileName,
            arguments.Count > 0 ? arguments[0] : "",
            result.ExitCode,
            (long)timeProvider.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }
}
