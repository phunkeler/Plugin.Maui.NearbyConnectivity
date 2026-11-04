namespace Harness;

/// <summary>What one run of a verb cost.</summary>
/// <param name="DurationMilliseconds">The verb's wall-clock time.</param>
/// <param name="GitCalls">The calls made to <c>git</c>.</param>
/// <param name="GhCalls">The calls made to <c>gh</c>.</param>
/// <param name="AzCalls">The calls made to <c>az</c>.</param>
sealed record RunStats(long DurationMilliseconds, int GitCalls, int GhCalls, int AzCalls)
{
    /// <summary>The stats of a report no run has measured.</summary>
    public static RunStats None { get; } = new(0, 0, 0, 0);
}
