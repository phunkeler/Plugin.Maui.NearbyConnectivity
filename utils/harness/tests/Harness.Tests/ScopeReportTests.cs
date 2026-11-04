using NSubstitute;
using static Harness.Tests.Create;

namespace Harness.Tests;

// ScopeReport.Build over a hand-built pull request, and Change.ScopeAsync, which feeds it from a real repository,
// GitHub records and Azure Boards from substitutes.
public sealed class ScopeReportTests : IDisposable
{
    const string Story = "AB#1";
    const string TestFile = "tests/PeersTests.cs";
    const string TestText = "class PeersTests\n{\n    [Fact]\n    public void TC1201_Peers_appear()\n    {\n    }\n}\n";

    readonly TempRepo _repo = TempRepo.Create();
    readonly IGitHubRecords _gitHubRecords = GitHubRecords();

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void Build_WorkItemsInTheDescription_AreDistinctAndAscending()
    {
        // Arrange
        var pullRequest = PullRequestNode.Create(body: "Closes AB#12. See ab#3, AB#12 and ABC#99.").ToPullRequest();

        // Act
        var report = Build(pullRequest);

        // Assert
        Assert.Equal([3, 12], report.WorkItems);
    }

    [Fact]
    public void Build_WorkItemOnlyInTheTitle_IsNotALink()
    {
        // Arrange
        var pullRequest = PullRequestNode.Create(title: "Fix AB#12", body: "No ticket here.").ToPullRequest();

        // Act
        var report = Build(pullRequest);

        // Assert
        Assert.Empty(report.WorkItems);
    }

    [Fact]
    public void Build_NoBody_HasNoWorkItem()
    {
        // Arrange
        var pullRequest = PullRequestNode.Create(body: null).ToPullRequest();

        // Act
        var report = Build(pullRequest);

        // Assert
        Assert.Empty(report.WorkItems);
    }

    [Fact]
    public void Build_ImpactReachAndChain_AreCarriedOnTheReport()
    {
        // Arrange
        var pullRequest = PullRequestNode.Create(number: 57, author: "Copilot").ToPullRequest();
        var testImpact = TestImpact.Of([new(TestFile, [1201], [], [])]);
        var platformReach = new PlatformReach(["ios"], Shared: false);
        var chain = Chain.NotRead("reason");

        // Act
        var report = ScopeReport.Build(pullRequest, testImpact, platformReach, chain);

        // Assert
        Assert.Equal((57, "copilot"), (report.Number, report.Author));
        Assert.Same(testImpact, report.TestImpact);
        Assert.Same(platformReach, report.PlatformReach);
        Assert.Same(chain, report.Chain);
    }

    [Fact]
    public async Task ScopeAsync_ChangedTest_ReportsItsTestCaseAndMethod()
    {
        // Arrange
        var head = WriteBranchOnTopOfMain();
        _gitHubRecords.ReadReturns(PullRequestNode.Create(number: 57, head: head));
        var change = Change(_repo.Root, _gitHubRecords);

        // Act
        var report = await change.ScopeAsync(new RecordRef(57), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(head, report.HeadSha);
        Assert.Equal([1201], report.TestImpact.TestCaseIds);
        Assert.Equal(["PeersTests.TC1201_Peers_appear"], report.TestImpact.TestsFor(1201));
        Assert.Equal(["android"], report.PlatformReach.Platforms);
        Assert.True(report.PlatformReach.Shared);
    }

    [Fact]
    public async Task ScopeAsync_StoryInTheDescription_ChainsItToTheTestMethod()
    {
        // Arrange
        var head = WriteBranchOnTopOfMain();
        _gitHubRecords.ReadReturns(PullRequestNode.Create(number: 57, body: Story, head: head));
        var workItems = WorkItems();
        workItems.ReadWorkItemAsync(1, Arg.Any<CancellationToken>())
            .Returns(WorkItem(1, "User Story", "Show nearby peers", "New"));
        workItems.ReadLinkedTestCasesAsync(1, Arg.Any<CancellationToken>())
            .Returns([WorkItem(1201, "Test Case", "Peers appear", "Ready", "AI-Drafted")]);
        var change = Change(
            _repo.Root, _gitHubRecords, workItems, new BoardsEnvironment(Boards.Organization, Boards.Project));

        // Act
        var report = await change.ScopeAsync(new RecordRef(57), TestContext.Current.CancellationToken);

        // Assert
        var story = Assert.Single(report.Chain.Stories);
        var testCase = Assert.Single(story.TestCases);
        Assert.Equal(1201, testCase.TestCase.Id);
        Assert.Equal(["PeersTests.TC1201_Peers_appear"], testCase.ChangedTests);
    }

    [Fact]
    public async Task ScopeAsync_TestCaseCarriedByAnUnchangedTest_ChainsItAsExisting()
    {
        // Arrange
        _repo.Write("tests/LeaveTests.cs", "class LeaveTests\n{\n    public void Peers_leave_TC1203()\n    {\n    }\n}\n");
        var head = WriteBranchOnTopOfMain();
        _gitHubRecords.ReadReturns(PullRequestNode.Create(number: 57, body: Story, head: head));
        var workItems = WorkItems();
        workItems.ReadWorkItemAsync(1, Arg.Any<CancellationToken>())
            .Returns(WorkItem(1, "User Story", "Show nearby peers", "New"));
        workItems.ReadLinkedTestCasesAsync(1, Arg.Any<CancellationToken>())
            .Returns([WorkItem(1203, "Test Case", "Peers leave", "Ready")]);
        var change = Change(
            _repo.Root, _gitHubRecords, workItems, new BoardsEnvironment(Boards.Organization, Boards.Project));

        // Act
        var report = await change.ScopeAsync(new RecordRef(57), TestContext.Current.CancellationToken);

        // Assert
        var testCase = Assert.Single(Assert.Single(report.Chain.Stories).TestCases);
        Assert.Empty(testCase.ChangedTests);
        Assert.Equal(["LeaveTests.Peers_leave_TC1203"], testCase.ExistingTests);
    }

    [Fact]
    public async Task ScopeAsync_NoBoardsConfigured_SaysWhyTheChainIsNotRead()
    {
        // Arrange
        var head = WriteBranchOnTopOfMain();
        _gitHubRecords.ReadReturns(PullRequestNode.Create(number: 57, body: Story, head: head));
        var change = Change(_repo.Root, _gitHubRecords);

        // Act
        var report = await change.ScopeAsync(new RecordRef(57), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ChainBuilder.NotConfigured, report.Chain.NotReadReason);
    }

    [Fact]
    public async Task ScopeAsync_BoardsInTheDefinition_ReadsThatProject()
    {
        // Arrange
        var head = WriteBranchOnTopOfMain(DefinitionTests.Valid);
        _gitHubRecords.ReadReturns(PullRequestNode.Create(number: 57, body: Story, head: head));
        var workItems = WorkItems();
        workItems.ReadWorkItemAsync(1, Arg.Any<CancellationToken>()).Returns((WorkItem?)null);
        var change = Change(_repo.Root, _gitHubRecords, workItems);

        // Act
        var report = await change.ScopeAsync(new RecordRef(57), TestContext.Current.CancellationToken);

        // Assert
        var story = Assert.Single(report.Chain.Stories);
        Assert.Null(story.Story);
    }

    static ScopeReport Build(PullRequest pullRequest) =>
        ScopeReport.Build(pullRequest, TestImpact.Empty, PlatformReach.None, Chain.Empty);

    // main holds the definition, when given, and a shared source; the branch `work` makes it Android-only in part and
    // adds a test with a Test Case id on its method. Returns the branch's head, which the pull request names.
    string WriteBranchOnTopOfMain(string? definition = null)
    {
        if (definition is not null)
        {
            _repo.Write("harness.json", definition);
        }

        _repo.Write("src/Scanner.cs", "class Scanner { }\n");
        _repo.Commit("base");
        _repo.AddOrigin();
        _repo.Git("checkout -q -b work");
        _repo.Write("src/Scanner.cs", "#if ANDROID\nclass Scanner { }\n#endif\n");
        _repo.Write("src/Clock.cs", "class Clock { }\n");
        _repo.Write(TestFile, TestText);
        return _repo.Commit("work");
    }
}
