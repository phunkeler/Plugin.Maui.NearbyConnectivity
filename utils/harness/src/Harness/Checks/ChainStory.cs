using System.Collections.Immutable;

namespace Harness;

/// <summary>One work item the description names, with its Test Cases.</summary>
/// <param name="Id">The work item id the description names.</param>
/// <param name="Story">The work item; <see langword="null"/> when Boards has no such work item in the project.</param>
/// <param name="TestCases">The Test Cases it is Tested By, by id.</param>
sealed record ChainStory(int Id, WorkItem? Story, ImmutableArray<ChainTestCase> TestCases);
