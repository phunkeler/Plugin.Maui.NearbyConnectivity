using System.Collections.Immutable;

namespace Harness;

/// <summary>An Azure Boards work item, as the Chain row reads it.</summary>
/// <param name="Id">The work item id.</param>
/// <param name="Type">The work item type, such as <c>User Story</c> or <c>Test Case</c>.</param>
/// <param name="Title">The title.</param>
/// <param name="State">The state, such as <c>Ready</c>.</param>
/// <param name="Tags">The tags, in the order Boards stores them.</param>
/// <param name="Relations">The links to other work items and artifacts.</param>
sealed record WorkItem(
    int Id,
    string Type,
    string Title,
    string State,
    ImmutableArray<string> Tags,
    ImmutableArray<WorkItemRelation> Relations);
