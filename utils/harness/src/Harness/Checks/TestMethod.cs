using System.Collections.Immutable;

namespace Harness;

/// <summary>A test method that carries Test Case ids on its declaration or on an attribute line above it.</summary>
/// <param name="Name">The method as <c>Class.Method</c>, the class being the nearest one declared above it.</param>
/// <param name="TestCaseIds">The Test Case ids it carries, distinct and ascending.</param>
sealed record TestMethod(string Name, ImmutableArray<int> TestCaseIds);
