using System.Collections.Immutable;

namespace Harness;

/// <summary>
/// The work items of one Azure Boards project, read only. A Boards that cannot be reached or read is a
/// <see cref="RecordUnavailableException"/>.
/// </summary>
interface IWorkItems
{
    /// <summary>Reads a work item with its relations.</summary>
    /// <param name="id">The work item id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The work item; <see langword="null"/> when it does not exist in the project or the caller may not read it.
    /// </returns>
    Task<WorkItem?> ReadWorkItemAsync(int id, CancellationToken cancellationToken);

    /// <summary>Reads the Test Cases a work item is Tested By.</summary>
    /// <param name="storyId">The work item id, usually a User Story.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// The targets of its <c>Microsoft.VSTS.Common.TestedBy-Forward</c> links whose type is <c>Test Case</c>, by id;
    /// empty when the work item does not exist or has none.
    /// </returns>
    Task<ImmutableArray<WorkItem>> ReadLinkedTestCasesAsync(int storyId, CancellationToken cancellationToken);
}
