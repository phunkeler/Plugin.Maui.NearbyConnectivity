using System.Collections.Immutable;

namespace Harness;

/// <summary>A Test Case linked to a story, and the tests that carry its id.</summary>
/// <param name="TestCase">The Test Case.</param>
/// <param name="ChangedTests">
/// The changed test methods, as <c>Class.Method</c>, or test files, that carry the id; see
/// <see cref="TestImpact.TestsFor"/>.
/// </param>
/// <param name="ExistingTests">
/// The same, from the test files on the pull request's head that the change does not touch; searched only when no
/// changed test carries the id.
/// </param>
sealed record ChainTestCase(
    WorkItem TestCase, ImmutableArray<string> ChangedTests, ImmutableArray<string> ExistingTests);
