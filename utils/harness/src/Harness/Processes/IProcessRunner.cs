namespace Harness;

/// <summary>Runs programs, such as <c>gh</c> and <c>git</c>, to completion.</summary>
interface IProcessRunner
{
    /// <summary>Runs a program with its standard streams redirected as UTF-8, and waits for it to exit.</summary>
    /// <param name="fileName">The program, found on PATH.</param>
    /// <param name="arguments">The arguments. They are passed one by one, never joined into one string.</param>
    /// <param name="workingDirectory">The directory to run in; <see langword="null"/> for the current one.</param>
    /// <param name="standardInput">
    /// The text to write to standard input before it is closed; <see langword="null"/> to close it at once.
    /// </param>
    /// <param name="cancellationToken">Kills the program's process tree.</param>
    /// <returns>The exit code and the output.</returns>
    /// <exception cref="UsageException">The program is not installed or not on PATH.</exception>
    /// <exception cref="TimeoutException">The program ran past the runner's time bound and was killed.</exception>
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        string? standardInput,
        CancellationToken cancellationToken);
}
