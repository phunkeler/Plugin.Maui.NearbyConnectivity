namespace Harness;

/// <summary>What a search of a revision's test files for Test Case ids found.</summary>
/// <param name="Found">The test files read that name a sought id, with their methods.</param>
/// <param name="Searched">The number of test files read.</param>
/// <param name="Candidates">
/// The number of test files that contain a sought id as text; more than <paramref name="Searched"/> when the search
/// was capped.
/// </param>
sealed record TestSearch(TestImpact Found, int Searched, int Candidates)
{
    /// <summary>The search of no id.</summary>
    public static TestSearch None { get; } = new(TestImpact.Empty, 0, 0);

    /// <summary>Tells whether the search stopped at its cap before reading every candidate.</summary>
    public bool Capped => Searched < Candidates;
}
