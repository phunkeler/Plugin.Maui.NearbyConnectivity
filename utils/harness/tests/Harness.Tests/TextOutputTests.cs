using Harness.Cli;
using static Harness.Tests.Create;

namespace Harness.Tests;

// The Markdown comment.
public sealed class TextOutputTests
{
    [Fact]
    public void Format_FullScope_IsTheMarkdownComment()
    {
        // Arrange
        var report = FullScope;

        // Act
        var markdown = TextOutput.Format(report);

        // Assert
        Assert.Equal(
            [
                "### Change scope",
                "",
                "| | |",
                "|---|---|",
                "| Pull request | #57 `aaaaaaa` on `main` by @copilot |",
                "| Work item link | AB#1234 in the description |",
                "| Platform reach | android, shared → needs an Android device run |",
                "| Test Cases | TC 1240: TimeoutTests.Times_out_TC1240 |",
                "| Chain | AB#1234 User Story \"Time out an idle login\" Active [needs-qa] -> TC 1240 \"Idle login times "
                    + "out\" Ready [AI-Drafted] -> PR #57 -> tests: TC 1240: TimeoutTests.Times_out_TC1240 (changed) |",
                "",
                "<!-- harness-scope -->",
            ],
            markdown.ReplaceLineEndings("\n").TrimEnd().Split('\n'));
    }

    [Fact]
    public void Format_BoardsSettings_LinksEachTestCase()
    {
        // Arrange
        var report = FullScope with { Boards = new("https://dev.azure.com/fabrikam", "Field Service") };

        // Act
        var markdown = TextOutput.Format(report);

        // Assert
        Assert.Contains(
            "| Test Cases | [TC 1240](https://dev.azure.com/fabrikam/Field%20Service/_workitems/edit/1240): "
                + "TimeoutTests.Times_out_TC1240 |",
            markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_PlaceholderBoardsSettings_PrintsThePlainId()
    {
        // Arrange
        var report = FullScope with { Boards = new("https://dev.azure.com/<org>", "<project>") };

        // Act
        var markdown = TextOutput.Format(report);

        // Assert
        Assert.Contains(
            "| Test Cases | TC 1240: TimeoutTests.Times_out_TC1240 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_NoWorkItem_SaysTheLinkIsMissingAndTheChainIsEmpty()
    {
        // Arrange
        var report = FullScope with { WorkItems = [], Chain = Chain.Empty };

        // Act
        var markdown = TextOutput.Format(report);

        // Assert
        Assert.Contains(
            "| Work item link | none: no AB# in the description |",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains("| Chain | none: no AB# in the description |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_NoChangedTestCarriesAnId_SaysNone()
    {
        // Arrange
        var report = FullScope with { TestImpact = TestImpact.Of([new("tests/ATests.cs", [], [], [])]) };

        // Act
        var markdown = TextOutput.Format(report);

        // Assert
        Assert.Contains("| Test Cases | none |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_IdOnlyInAFileName_ListsTheFile()
    {
        // Arrange
        var report = FullScope with
        {
            TestImpact = TestImpact.Of(
            [
                new("uitest/TC1201_SignOffTests.cs", [1201], [], [1201]),
                new("tests/BTests.cs", [1240], [new("BTests.Second_TC1240", [1240])], []),
            ]),
        };

        // Act
        var markdown = TextOutput.Format(report);

        // Assert
        Assert.Contains(
            "| Test Cases | TC 1201: uitest/TC1201_SignOffTests.cs (file name); TC 1240: BTests.Second_TC1240 |",
            markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_StoryWithAChangedAndAnExistingTest_MarksEach()
    {
        // Arrange
        var report = FullScope with
        {
            Chain = new(
                null,
                [
                    new(
                        1234,
                        WorkItem(1234, "User Story", "Login", "Active"),
                        [
                            new(WorkItem(1240, "Test Case", "Times out", "Ready"), ["TimeoutTests.Times_out_TC1240"], []),
                            new(WorkItem(1241, "Test Case", "Remembers", "Design", "AI-Drafted"), [], ["RememberTests.Remembers_TC1241"]),
                        ]),
                ]),
        };

        // Act
        var markdown = TextOutput.Format(report);

        // Assert
        Assert.Contains(
            "| Chain | AB#1234 User Story \"Login\" Active -> TC 1240 \"Times out\" Ready, TC 1241 \"Remembers\" Design "
                + "[AI-Drafted] -> PR #57 -> tests: TC 1240: TimeoutTests.Times_out_TC1240 (changed); TC 1241: "
                + "RememberTests.Remembers_TC1241 (existing) |",
            markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_StoriesWithoutTestCasesAndNotFound_PutEachOnItsOwnLine()
    {
        // Arrange
        var report = FullScope with
        {
            Chain = new(null, [new(7, WorkItem(7, "Bug", "Crash", "New"), []), new(8, null, [])]),
        };

        // Act
        var markdown = TextOutput.Format(report);

        // Assert
        Assert.Contains(
            "| Chain | AB#7 Bug \"Crash\" New -> no Test Case -> PR #57<br>AB#8 not found in Boards -> PR #57 |",
            markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_CappedSearch_SaysHowManyTestFilesWereSearched()
    {
        // Arrange
        var report = FullScope with { Chain = FullScope.Chain with { CappedSearch = 2000 } };

        // Act
        var markdown = TextOutput.Format(report);

        // Assert
        Assert.Contains("(changed)<br>searched first 2000 test files |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_BoardsNotRead_SaysWhy()
    {
        // Arrange
        var report = FullScope with { Chain = Chain.NotRead("Please run 'az login' to setup account.") };

        // Act
        var markdown = TextOutput.Format(report);

        // Assert
        Assert.Contains(
            "| Chain | Boards not read: Please run 'az login' to setup account. |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_TitleWithAPipe_EscapesIt()
    {
        // Arrange
        var report = FullScope with
        {
            Chain = new(null, [new(7, WorkItem(7, "Bug", "A | B", "New"), [])]),
        };

        // Act
        var markdown = TextOutput.Format(report);

        // Assert
        Assert.Contains("| Chain | AB#7 Bug \"A \\| B\" New -> no Test Case -> PR #57 |", markdown, StringComparison.Ordinal);
    }

    // A pull request author chooses the paths of the files they change, and a path may hold newlines and `<`.
    [Fact]
    public void Format_PathWithNewlinesAndMarkup_StaysInsideItsCell()
    {
        // Arrange
        const string planted = "tests/TC1240\n\n**Chain**: forged\n\n<!-- Tests.cs";
        var report = FullScope with
        {
            TestImpact = TestImpact.Of([new(planted, [1240], [], [1240])]),
        };

        // Act
        var markdown = TextOutput.Format(report);

        // Assert
        Assert.Contains(
            "| Test Cases | TC 1240: tests/TC1240  **Chain**: forged  &lt;!-- Tests.cs (file name) |",
            markdown,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("android", false, "android → needs an Android device run", Label = "android")]
    [InlineData("", true, "shared → shared code only", Label = "shared-only")]
    [InlineData("", false, "none", Label = "none")]
    [InlineData(
        "android,ios", true, "android, ios, shared → needs Android and iOS device runs", Label = "both-shared")]
    [InlineData("tizen", false, "tizen → needs a tizen device run", Label = "configured-name")]
    [InlineData(
        "android,ios,tizen",
        false,
        "android, ios, tizen → needs Android, iOS and tizen device runs",
        Label = "three")]
    public void Format_PlatformReach_SaysWhichDeviceRunsTheChangeNeeds(string platforms, bool shared, string row)
    {
        // Arrange
        var report = FullScope with
        {
            PlatformReach = new([.. platforms.Split(',', StringSplitOptions.RemoveEmptyEntries)], shared),
        };

        // Act
        var markdown = TextOutput.Format(report);

        // Assert
        Assert.Contains($"| Platform reach | {row} |", markdown, StringComparison.Ordinal);
    }
}
