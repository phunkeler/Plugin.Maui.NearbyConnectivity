namespace Harness;

/// <summary>
/// The <c>testImpact.searchLimit</c> block of <c>harness.json</c>: how much of a revision's test files the Chain row
/// may read when it looks for a Test Case id no changed test carries.
/// </summary>
/// <param name="Files">The most test files one search reads.</param>
/// <param name="Megabytes">The most megabytes one search reads, over all its files.</param>
sealed record SearchLimit(int Files, int Megabytes)
{
    /// <summary>The limit when the block is absent: 2,000 files and 20 MB.</summary>
    public static SearchLimit Default { get; } = new(Files: 2000, Megabytes: 20);

    /// <summary>The byte count of <see cref="Megabytes"/>.</summary>
    public long Bytes => Megabytes * 1024L * 1024L;
}
