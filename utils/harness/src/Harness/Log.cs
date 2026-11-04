using Microsoft.Extensions.Logging;

namespace Harness;

// The harness's log events, with stable ids. No field carries a body, a diff or a token.
static partial class Log
{
    [LoggerMessage(1, LogLevel.Information, "{Verb} {Record}")]
    public static partial void VerbStarted(this ILogger logger, string verb, string record);

    [LoggerMessage(2, LogLevel.Information, "{Verb} finished in {DurationMs} ms: {GitCalls} git, {GhCalls} gh, {AzCalls} az")]
    public static partial void VerbFinished(
        this ILogger logger, string verb, long durationMs, int gitCalls, int ghCalls, int azCalls);

    [LoggerMessage(3, LogLevel.Debug, "{Tool} {Subcommand} exited {ExitCode} in {DurationMs} ms")]
    public static partial void ToolFinished(
        this ILogger logger, string tool, string subcommand, int exitCode, long durationMs);

    [LoggerMessage(10, LogLevel.Warning, "Boards not read: {Reason}")]
    public static partial void BoardsNotRead(this ILogger logger, string reason);
}
