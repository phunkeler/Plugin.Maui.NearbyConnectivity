using System.Collections.Immutable;

namespace Harness;

/// <summary>
/// Builds the Chain row: from each work item the description names, through its Test Cases in Azure Boards, to the
/// tests that carry their ids, changed by the pull request or already on its head. Advice only: a Boards failure
/// becomes a reason on the row, never an error.
/// </summary>
/// <param name="workItemsFactory">Opens the Azure Boards project.</param>
/// <param name="testImpactReader">Searches the head's test files for the ids no changed test carries.</param>
sealed class ChainBuilder(IWorkItemsFactory workItemsFactory, TestImpactReader testImpactReader)
{
    /// <summary>The reason a chain gives when the organization or the project is not configured.</summary>
    public const string NotConfigured =
        "no organization and project; set boards in harness.json, or ADO_ORG and ADO_PROJECT";

    /// <summary>Builds the chain.</summary>
    /// <param name="workItemIds">The work item ids the description names, ascending.</param>
    /// <param name="boards">Where the work items live.</param>
    /// <param name="changes">The changed tests and their Test Case ids.</param>
    /// <param name="definition">The <c>testImpact</c> block that says where the tests live.</param>
    /// <param name="headRevision">The pull request's head, searched for the tests the change does not touch.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// <see cref="Chain.Empty"/> for no ids; a chain that says why when <paramref name="boards"/> is not complete, its
    /// organization is not an https URL on an Azure DevOps host, or Boards cannot be read; otherwise one story per
    /// id.
    /// </returns>
    /// <exception cref="UsageException">The head revision does not exist.</exception>
    public async Task<Chain> BuildAsync(
        ImmutableArray<int> workItemIds,
        BoardsDefinition boards,
        TestImpact changes,
        TestImpactDefinition definition,
        string headRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(boards);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrEmpty(headRevision);

        if (workItemIds.IsEmpty)
        {
            return Chain.Empty;
        }

        if (!boards.IsComplete)
        {
            return Chain.NotRead(NotConfigured);
        }

        if (!BoardsDefinition.IsOrganizationUrl(boards.Organization))
        {
            return Chain.NotRead(
                $"the organization '{boards.Organization}' is not an https URL on "
                + string.Join(" or ", BoardsDefinition.AllowedHosts));
        }

        var workItems = workItemsFactory.Create(boards);
        var linked = new List<(int Id, WorkItem? Story, ImmutableArray<WorkItem> TestCases)>();
        try
        {
            foreach (var id in workItemIds)
            {
                var story = await workItems.ReadWorkItemAsync(id, cancellationToken);
                var testCases = story is null
                    ? []
                    : await workItems.ReadLinkedTestCasesAsync(id, cancellationToken);
                linked.Add((id, story, testCases));
            }
        }
        catch (RecordUnavailableException exception)
        {
            return Chain.NotRead(exception.Message);
        }

        var search = await testImpactReader
            .SearchAsync(
                definition,
                headRevision,
                [.. linked.SelectMany(story => story.TestCases).Select(testCase => testCase.Id)
                    .Where(id => !changes.TestCaseIds.Contains(id)).Distinct()],
                [.. changes.ChangedTests.Select(test => test.TestFile)],
                cancellationToken);
        return new(
            null,
            [
                .. linked.Select(story => new ChainStory(
                    story.Id,
                    story.Story,
                    [
                        .. story.TestCases.Select(testCase => new ChainTestCase(
                            testCase, changes.TestsFor(testCase.Id), search.Found.TestsFor(testCase.Id))),
                    ])),
            ],
            search.Capped ? search.Searched : null);
    }
}
