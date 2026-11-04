namespace Harness;

/// <summary>
/// What a host may set when it registers the harness with <c>AddHarness</c>. Every property has a default, so a host
/// that sets nothing gets the harness over the process's current directory.
/// </summary>
sealed class HarnessOptions
{
    /// <summary>
    /// A directory inside the repository the harness reads. <see langword="null"/>, the default, is the process's
    /// current directory, read when git is first resolved.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>How long one <c>git</c>, <c>gh</c> or <c>az</c> call may run before it is killed. Ten minutes by default.</summary>
    public TimeSpan ProcessTimeout { get; set; } = ProcessRunner.DefaultTimeout;
}
