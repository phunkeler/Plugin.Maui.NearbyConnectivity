using System.Collections.Immutable;

namespace Harness;

/// <summary>A test file, the Azure Boards Test Cases it names, and the test methods that carry them.</summary>
/// <param name="TestFile">The test file's path from the top level.</param>
/// <param name="TestCaseIds">The Test Case ids the file's path and text name, distinct and ascending.</param>
/// <param name="Methods">The methods that carry a Test Case id, in file order.</param>
/// <param name="PathTestCaseIds">The Test Case ids the path alone names, distinct and ascending.</param>
sealed record TestReference(
    string TestFile,
    ImmutableArray<int> TestCaseIds,
    ImmutableArray<TestMethod> Methods,
    ImmutableArray<int> PathTestCaseIds);
