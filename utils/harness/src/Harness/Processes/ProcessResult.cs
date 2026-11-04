namespace Harness;

/// <summary>How a program ended, and what it wrote.</summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="StandardOutput">Everything the program wrote to standard output.</param>
/// <param name="StandardError">Everything the program wrote to standard error.</param>
sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
