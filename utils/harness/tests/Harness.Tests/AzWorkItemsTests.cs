using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static Harness.Tests.Create;

namespace Harness.Tests;

// The az rest command lines AzWorkItems sends and how it reads their output. The responses are recorded from a live
// organization: story 1 is Tested By Test Case 2, which is Ready and tagged AI-Drafted.
public sealed class AzWorkItemsTests
{
    readonly IProcessRunner _runner = RunnerAnsweringNothing();

    AzWorkItems WorkItems => new(_runner, TimeProvider.System, Boards);

    [Fact]
    public async Task ReadWorkItemAsync_RecordedStory_ReadsItsFieldsAndRelations()
    {
        // Arrange
        _runner.AzWorkItem(1).Returns(Ok(Fixture("workitem-1.json")));

        // Act
        var story = await WorkItems.ReadWorkItemAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(story);
        Assert.Equal((1, "User Story", "Show nearby peers in a list", "New"), (story.Id, story.Type, story.Title, story.State));
        Assert.Equal(["needs-qa"], story.Tags);
        var relation = Assert.Single(story.Relations);
        Assert.Equal(AzWorkItems.TestedBy, relation.Type);
        Assert.EndsWith("/_apis/wit/workItems/2", relation.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadWorkItemAsync_Id_SendsAzRestForTheProjectWithRelations()
    {
        // Arrange
        _runner.AzWorkItem(1).Returns(Ok(Fixture("workitem-1.json")));

        // Act
        await WorkItems.ReadWorkItemAsync(1, TestContext.Current.CancellationToken);

        // Assert
        var call = Assert.Single(_runner.AzCalls());
        Assert.Equal(
            [
                "rest", "--resource", "499b84ac-1321-427f-aa17-267ca6975798", "--only-show-errors", "-o", "json",
                "--url", "https://dev.azure.com/fabrikam/Mobile%20App/_apis/wit/workitems/1",
                "--url-parameters", "$expand=relations", "api-version=7.1",
            ],
            call.Arguments);
    }

    [Fact]
    public async Task ReadLinkedTestCasesAsync_RecordedStory_ReadsTheTestedByTestCase()
    {
        // Arrange
        _runner.AzWorkItem(1).Returns(Ok(Fixture("workitem-1.json")));
        _runner.AzWorkItem(2).Returns(Ok(Fixture("workitem-2.json")));

        // Act
        var testCases = await WorkItems
            .ReadLinkedTestCasesAsync(1, TestContext.Current.CancellationToken);

        // Assert
        var testCase = Assert.Single(testCases);
        Assert.Equal((2, "Test Case", "Ready"), (testCase.Id, testCase.Type, testCase.State));
        Assert.Equal(["AI-Drafted"], testCase.Tags);
    }

    [Fact]
    public async Task ReadLinkedTestCasesAsync_LinkedItemIsNotATestCase_LeavesItOut()
    {
        // Arrange
        _runner.AzWorkItem(1).Returns(Ok(Fixture("workitem-1.json")));
        _runner.AzWorkItem(2).Returns(Ok(WithType(Fixture("workitem-2.json"), "Task")));

        // Act
        var testCases = await WorkItems
            .ReadLinkedTestCasesAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(testCases);
    }

    [Fact]
    public async Task ReadLinkedTestCasesAsync_TestCaseLinkedAsRelated_LeavesItOut()
    {
        // Arrange
        _runner.AzWorkItem(1).Returns(Ok(WithRelation(Fixture("workitem-1.json"), "System.LinkTypes.Related")));
        _runner.AzWorkItem(2).Returns(Ok(Fixture("workitem-2.json")));

        // Act
        var testCases = await WorkItems
            .ReadLinkedTestCasesAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(testCases);
    }

    [Fact]
    public async Task ReadLinkedTestCasesAsync_AfterReadingTheStory_UsesTheFirstRead()
    {
        // Arrange
        _runner.AzWorkItem(1).Returns(Ok(Fixture("workitem-1.json")), Ok(Fixture("workitem-2.json")));
        _runner.AzWorkItem(2).Returns(Ok(Fixture("workitem-2.json")));
        var workItems = WorkItems;
        await workItems.ReadWorkItemAsync(1, TestContext.Current.CancellationToken);

        // Act
        var testCases = await workItems.ReadLinkedTestCasesAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([2], testCases.Select(testCase => testCase.Id));
    }

    [Fact]
    public async Task ReadWorkItemAsync_RecordedNotFound_ReturnsNull()
    {
        // Arrange
        _runner.AzWorkItem(999).Returns(new ProcessResult(1, "", Fixture("workitem-not-found.txt")));

        // Act
        var workItem = await WorkItems
            .ReadWorkItemAsync(999, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(workItem);
    }

    [Fact]
    public async Task ReadLinkedTestCasesAsync_StoryNotFound_IsEmpty()
    {
        // Arrange
        _runner.AzWorkItem(999).Returns(new ProcessResult(1, "", Fixture("workitem-not-found.txt")));

        // Act
        var testCases = await WorkItems
            .ReadLinkedTestCasesAsync(999, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(testCases);
    }

    [Fact]
    public async Task ReadWorkItemAsync_NotLoggedIn_ThrowsRecordUnavailableNamingBothWaysToSignIn()
    {
        // Arrange
        _runner.AzWorkItem(1).Returns(new ProcessResult(1, "", "ERROR: Please run 'az login' to setup account.\n"));

        // Act
        var exception = await Record.ExceptionAsync(() => WorkItems
            .ReadWorkItemAsync(1, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "no Azure sign-in here, which is expected in CI until one is set up. Locally, run az login",
            Assert.IsType<RecordUnavailableException>(exception).Message);
    }

    [Fact]
    public async Task ReadWorkItemAsync_AzNotInstalled_ThrowsRecordUnavailable()
    {
        // Arrange
        _runner.AzWorkItem(1).ThrowsAsync(new UsageException("az is not installed or not on PATH"));

        // Act
        var exception = await Record.ExceptionAsync(() => WorkItems
            .ReadWorkItemAsync(1, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "az is not installed or not on PATH", Assert.IsType<RecordUnavailableException>(exception).Message);
    }

    [Theory]
    [InlineData("az-unauthorized.txt", "TF400813: The user '00000000-0000-0000-0000-000000000001' is not authorized to access this resource.", Label = "tf400813")]
    [InlineData("az-not-found-html.txt", "Not Found: 404 - Page not found", Label = "html-404")]
    public async Task ReadWorkItemAsync_RecordedFailure_ThrowsRecordUnavailableWithTheTellingLine(
        string fixture, string reason)
    {
        // Arrange
        _runner.AzWorkItem(1).Returns(new ProcessResult(1, "", Fixture(fixture)));

        // Act
        var exception = await Record.ExceptionAsync(() => WorkItems
            .ReadWorkItemAsync(1, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(reason, Assert.IsType<RecordUnavailableException>(exception).Message);
    }

    [Fact]
    public async Task ReadWorkItemAsync_RecordedNetworkError_NamesTheConnectionFailureWithin200Characters()
    {
        // Arrange
        _runner.AzWorkItem(1).Returns(new ProcessResult(1, "", Fixture("az-network-error.txt")));

        // Act
        var exception = await Record.ExceptionAsync(() => WorkItems
            .ReadWorkItemAsync(1, TestContext.Current.CancellationToken));

        // Assert
        var message = Assert.IsType<RecordUnavailableException>(exception).Message;
        Assert.StartsWith(
            "HTTPSConnectionPool(host='nonexistent-host.invalid', port=443): the test-automation QA retries exceeded",
            message,
            StringComparison.Ordinal);
        Assert.Equal(200, message.Length);
    }

    [Fact]
    public async Task ReadWorkItemAsync_FailureWithNoErrorOutput_SaysSo()
    {
        // Arrange
        _runner.AzWorkItem(1).Returns(new ProcessResult(3, "", ""));

        // Act
        var exception = await Record.ExceptionAsync(() => WorkItems
            .ReadWorkItemAsync(1, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "az rest exited with code 3 and wrote no error", Assert.IsType<RecordUnavailableException>(exception).Message);
    }

    [Fact]
    public async Task ReadWorkItemAsync_AzNeverAnswers_TimesOutAfterSixtySeconds()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        _runner.AzWorkItem(1).Returns(call => NeverAsync(call.ArgAt<CancellationToken>(4)));
        var read = new AzWorkItems(_runner, clock, Boards).ReadWorkItemAsync(1, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(60));

        // Act
        var exception = await Record.ExceptionAsync(() => read);

        // Assert
        Assert.Equal("az rest timed out after 60 s", Assert.IsType<RecordUnavailableException>(exception).Message);
    }

    [Fact]
    public async Task ReadWorkItemAsync_RunnerTimesOut_ThrowsRecordUnavailable()
    {
        // Arrange
        _runner.AzWorkItem(1).ThrowsAsync(new TimeoutException("az rest did not finish within 00:10:00"));

        // Act
        var exception = await Record.ExceptionAsync(() => WorkItems
            .ReadWorkItemAsync(1, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "az rest did not finish within 00:10:00", Assert.IsType<RecordUnavailableException>(exception).Message);
    }

    [Theory]
    [InlineData("not json", Label = "not-json")]
    [InlineData("{\"id\":1}", Label = "no-fields")]
    [InlineData("{\"id\":1,\"fields\":{\"System.WorkItemType\":\"Bug\"}}", Label = "missing-field")]
    public async Task ReadWorkItemAsync_UnreadableResponse_ThrowsRecordUnavailable(string standardOutput)
    {
        // Arrange
        _runner.AzWorkItem(1).Returns(Ok(standardOutput));

        // Act
        var exception = await Record.ExceptionAsync(() => WorkItems
            .ReadWorkItemAsync(1, TestContext.Current.CancellationToken));

        // Assert
        Assert.NotNull(Assert.IsType<RecordUnavailableException>(exception).InnerException);
    }

    // A run that ends only when it is cancelled, as a hung az would.
    static async Task<ProcessResult> NeverAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("a delay without end ended");
    }

    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    // The recorded Test Case with another work item type.
    static string WithType(string json, string type) =>
        json.Replace("\"System.WorkItemType\": \"Test Case\"", $"\"System.WorkItemType\": \"{type}\"", StringComparison.Ordinal);

    // The recorded story with its Tested By link changed to another link type.
    static string WithRelation(string json, string type) =>
        json.Replace(AzWorkItems.TestedBy, type, StringComparison.Ordinal);

    static ProcessResult Ok(string standardOutput) => new(0, standardOutput, "");
}
