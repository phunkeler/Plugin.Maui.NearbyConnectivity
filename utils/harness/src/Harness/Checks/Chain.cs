using System.Collections.Immutable;

namespace Harness;

/// <summary>
/// The Chain row's facts: for each work item the description names, its Test Cases in Azure Boards and the tests
/// that carry them.
/// </summary>
/// <param name="NotReadReason">
/// Why Boards was not read, such as a missing configuration or <c>az login</c>; <see langword="null"/> when it was
/// read or there was nothing to read.
/// </param>
/// <param name="Stories">One entry per work item id, ascending; empty when Boards was not read.</param>
/// <param name="CappedSearch">
/// The number of test files read on the head when the search for existing tests stopped at its cap;
/// <see langword="null"/> when it read every candidate.
/// </param>
sealed record Chain(string? NotReadReason, ImmutableArray<ChainStory> Stories, int? CappedSearch = null)
{
    /// <summary>The chain of a pull request whose description names no work item.</summary>
    public static Chain Empty { get; } = new(null, []);

    /// <summary>Creates the chain of a pull request whose work items could not be read.</summary>
    /// <param name="reason">Why Boards was not read.</param>
    /// <returns>The chain, with no stories.</returns>
    public static Chain NotRead(string reason) => new(reason, []);
}
