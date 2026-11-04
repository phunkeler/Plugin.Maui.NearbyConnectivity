namespace Harness.Cli;

/// <summary>
/// The exit codes of <c>harness</c>. Codes 1 and 4 are not used. System.CommandLine exits 143 itself when a SIGTERM
/// is not honoured within two seconds.
/// </summary>
static class ExitCodes
{
    /// <summary>
    /// The command succeeded.
    /// </summary>
    public const int Success = 0;

    /// <summary>
    /// The command line is wrong, or a ref, path, tool or definition it needs is missing or invalid.
    /// </summary>
    public const int UsageError = 2;

    /// <summary>
    /// A read found no record.
    /// </summary>
    public const int RecordNotFound = 3;

    /// <summary>
    /// The record system is unreachable, refused the caller, did not answer in time or sent a response the harness
    /// cannot read.
    /// </summary>
    public const int RecordUnavailable = 5;

    /// <summary>
    /// The command was cancelled, by Ctrl+C or a SIGTERM: the Unix convention for an interrupted process.
    /// </summary>
    public const int Cancelled = 130;

    /// <summary>Maps an exception to its exit code.</summary>
    /// <param name="exception">The exception the command threw.</param>
    /// <returns>2, 3 or 5.</returns>
    public static int FromException(Exception exception)
        => KnownCodeFor(exception) ?? RecordUnavailable;

    /// <summary>
    /// Maps a failure whose message is for the user, and needs no type name or stack, to its exit code.
    /// </summary>
    /// <param name="exception">The exception the command threw.</param>
    /// <returns>2, 3 or 5; <see langword="null"/> for any other exception.</returns>
    public static int? KnownCodeFor(Exception exception)
        => exception switch
        {
            UsageException => UsageError,
            RecordNotFoundException => RecordNotFound,
            RecordUnavailableException => RecordUnavailable,

            // A git or gh call that hangs past the runner's bound: the record system did not answer.
            TimeoutException => RecordUnavailable,
            _ => null,
        };
}
