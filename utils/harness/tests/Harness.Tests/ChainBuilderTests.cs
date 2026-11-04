using System.Collections.Immutable;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static Harness.Tests.Create;

namespace Harness.Tests;

// How the Chain row's facts are composed from the work item ids, Boards, the changed tests and the tests on the head.
public sealed class ChainBuilderTests
{
    const string Head = "head";
    const string ExistingFile = "tests/LeaveTests.cs";

    static readonly WorkItem s_story = WorkItem(1, "User Story", "Show nearby peers", "New", "needs-qa");
    static readonly WorkItem s_carried = WorkItem(1202, "Test Case", "Peers appear", "Ready", "AI-Drafted");
    static readonly WorkItem s_existing = WorkItem(1203, "Test Case", "Peers leave", "Ready");
    static readonly WorkItem s_uncarried = WorkItem(1204, "Test Case", "Peers rejoin", "Design");

    // One changed test carries Test Case 1202; an unchanged test on the head carries 1203; nothing carries 1204.
    static readonly TestImpact s_changes =
        TestImpact.Of([new("tests/PeersTests.cs", [1202], [new("PeersTests.Peers_appear_TC1202", [1202])], [])]);

    readonly IWorkItems _workItems = WorkItems();
    readonly IGit _git = Substitute.For<IGit>();

    public ChainBuilderTests()
    {
        _git.ListFilesAsync(Head, Arg.Any<CancellationToken>())
            .Returns([new TreeFile(ExistingFile, 100), new TreeFile("tests/PeersTests.cs", 100)]);
        _git.FindFilesContainingAsync(
                Head, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([ExistingFile]);
        _git.ReadFilesAsync(Head, Arg.Any<IReadOnlyList<string>>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ImmutableDictionary<string, string>.Empty.Add(
                ExistingFile, "class LeaveTests\n{\n    public void Peers_leave_TC1203()\n    {\n    }\n}\n"));
    }

    [Fact]
    public async Task BuildAsync_StoryWithChangedExistingAndUncarriedTestCases_ListsEachWithItsTests()
    {
        // Arrange
        _workItems.ReadWorkItemAsync(1, Arg.Any<CancellationToken>()).Returns(s_story);
        _workItems.ReadLinkedTestCasesAsync(1, Arg.Any<CancellationToken>())
            .Returns([s_carried, s_existing, s_uncarried]);

        // Act
        var chain = await Build([1]);

        // Assert
        Assert.Null(chain.NotReadReason);
        var story = Assert.Single(chain.Stories);
        Assert.Same(s_story, story.Story);
        Assert.Equal([1202, 1203, 1204], story.TestCases.Select(link => link.TestCase.Id));
        Assert.Equal(["PeersTests.Peers_appear_TC1202"], story.TestCases[0].ChangedTests);
        Assert.Empty(story.TestCases[0].ExistingTests);
        Assert.Equal(["LeaveTests.Peers_leave_TC1203"], story.TestCases[1].ExistingTests);
        Assert.Empty(story.TestCases[2].ChangedTests);
        Assert.Empty(story.TestCases[2].ExistingTests);
        Assert.Null(chain.CappedSearch);
    }

    [Fact]
    public async Task BuildAsync_StoryNotInBoards_IsListedWithoutTestCases()
    {
        // Arrange
        _workItems.ReadWorkItemAsync(9, Arg.Any<CancellationToken>()).Returns((WorkItem?)null);

        // Act
        var chain = await Build([9]);

        // Assert
        var story = Assert.Single(chain.Stories);
        Assert.Equal((9, null), (story.Id, story.Story));
        Assert.Empty(story.TestCases);
    }

    [Fact]
    public async Task BuildAsync_ProviderUnavailable_SaysWhyAndListsNoStory()
    {
        // Arrange
        _workItems.ReadWorkItemAsync(1, Arg.Any<CancellationToken>())
            .ThrowsAsync(new RecordUnavailableException("Please run 'az login' to setup account."));

        // Act
        var chain = await Build([1]);

        // Assert
        Assert.Equal("Please run 'az login' to setup account.", chain.NotReadReason);
        Assert.Empty(chain.Stories);
    }

    [Fact]
    public async Task BuildAsync_BoardsNotConfigured_SaysSoWithoutReadingBoards()
    {
        // Arrange
        var boards = new BoardsDefinition(Organization: null, Project: "Mobile App");

        // Act
        var chain = await Build([1], boards);

        // Assert
        Assert.Equal(ChainBuilder.NotConfigured, chain.NotReadReason);
    }

    [Fact]
    public async Task BuildAsync_OrganizationNotAUrl_SaysSoWithoutReadingBoards()
    {
        // Arrange
        var boards = new BoardsDefinition(Organization: "<org>", Project: "Mobile App");

        // Act
        var chain = await Build([1], boards);

        // Assert
        Assert.Equal(
            "the organization '<org>' is not an https URL on dev.azure.com or visualstudio.com", chain.NotReadReason);
    }

    [Fact]
    public async Task BuildAsync_NoWorkItem_IsEmpty()
    {
        // Arrange
        var boards = new BoardsDefinition(Organization: null, Project: null);

        // Act
        var chain = await Build([], boards);

        // Assert
        Assert.Same(Chain.Empty, chain);
    }

    Task<Chain> Build(int[] workItemIds, BoardsDefinition? boards = null) =>
        new ChainBuilder(WorkItemsFactory(_workItems), new TestImpactReader(_git)).BuildAsync(
            [.. workItemIds],
            boards ?? Boards,
            s_changes,
            TestImpactDefinition.Default,
            Head,
            TestContext.Current.CancellationToken);
}
