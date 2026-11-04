using System.Collections.Immutable;

namespace Harness;

/// <summary>Test files and the Test Cases they name: the tests a change edits, or the tests found on a revision.</summary>
/// <param name="ChangedTests">The test files, by path, each with its Test Cases and test methods.</param>
/// <param name="TestCaseIds">Every Test Case id over <see cref="ChangedTests"/>, distinct and ascending.</param>
sealed record TestImpact(ImmutableArray<TestReference> ChangedTests, ImmutableArray<int> TestCaseIds)
{
    /// <summary>The impact of a change with no test.</summary>
    public static TestImpact Empty { get; } = new([], []);

    /// <summary>Builds the impact from the changed tests, sorted by path, and derives the ids.</summary>
    /// <param name="changedTests">The changed test files with their Test Cases, in any order.</param>
    /// <returns>The impact.</returns>
    public static TestImpact Of(IEnumerable<TestReference> changedTests)
    {
        ArgumentNullException.ThrowIfNull(changedTests);
        ImmutableArray<TestReference> tests =
            [.. changedTests.OrderBy(reference => reference.TestFile, StringComparer.Ordinal)];
        return new(tests, [.. tests.SelectMany(reference => reference.TestCaseIds).Distinct().Order()]);
    }

    /// <summary>Lists the tests that carry a Test Case id.</summary>
    /// <param name="testCaseId">The Test Case id.</param>
    /// <returns>
    /// The test methods that carry it, as <c>Class.Method</c>, distinct, in path and file order. When no method does,
    /// the test files whose path names it, as <c>path (file name)</c>, else those whose text names it, as the path.
    /// Empty when no test names it.
    /// </returns>
    public ImmutableArray<string> TestsFor(int testCaseId)
    {
        ImmutableArray<string> methods =
        [
            .. ChangedTests.SelectMany(reference => reference.Methods)
                .Where(method => method.TestCaseIds.Contains(testCaseId))
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal),
        ];
        if (!methods.IsEmpty)
        {
            return methods;
        }

        ImmutableArray<string> named =
        [
            .. ChangedTests.Where(reference => reference.PathTestCaseIds.Contains(testCaseId))
                .Select(reference => reference.TestFile + " (file name)"),
        ];
        return named.IsEmpty
            ? [.. ChangedTests.Where(reference => reference.TestCaseIds.Contains(testCaseId))
                .Select(reference => reference.TestFile)]
            : named;
    }
}
