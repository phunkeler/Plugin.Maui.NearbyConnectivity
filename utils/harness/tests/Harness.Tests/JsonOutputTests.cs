using Harness.Cli;
using static Harness.Tests.Create;

namespace Harness.Tests;

// The --json line: the pinned contract with the workflow and the measures.
public sealed class JsonOutputTests
{
    [Fact]
    public void Format_FullScope_IsThePinnedLine()
    {
        // Arrange
        var report = FullScope with { Run = new(1500, GitCalls: 7, GhCalls: 1, AzCalls: 3) };

        // Act
        var json = JsonOutput.Format(report);

        // Assert
        Assert.Equal(
            "{\"number\":57,\"title\":\"Fix the login timeout\",\"author\":\"copilot\",\"baseRef\":\"main\","
                + $"\"headSha\":\"{Sha('a')}\","
                + "\"run\":{\"durationMs\":1500,\"gitCalls\":7,\"ghCalls\":1,\"azCalls\":3},\"workItems\":[1234],"
                + "\"platformReach\":{\"platforms\":[\"android\"],\"shared\":true},"
                + "\"testCases\":[{\"id\":1240,\"testMethods\":[\"TimeoutTests.Times_out_TC1240\"]}],"
                + "\"boardsNotRead\":null,\"cappedSearch\":null,"
                + "\"chain\":[{\"id\":1234,\"found\":true,\"type\":\"User Story\",\"title\":\"Time out an idle login\","
                + "\"state\":\"Active\",\"tags\":[\"needs-qa\"],\"testCases\":[{\"id\":1240,"
                + "\"title\":\"Idle login times out\",\"state\":\"Ready\",\"tags\":[\"AI-Drafted\"],"
                + "\"testMethods\":[{\"name\":\"TimeoutTests.Times_out_TC1240\",\"changed\":true}]}]}]}",
            json);
    }

    [Fact]
    public void Format_BoardsNotRead_CarriesTheReasonAndNoStories()
    {
        // Arrange
        var report = FullScope with { Chain = Chain.NotRead("no az") };

        // Act
        var json = JsonOutput.Format(report);

        // Assert
        Assert.EndsWith("\"boardsNotRead\":\"no az\",\"cappedSearch\":null,\"chain\":[]}", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_StoryNotFound_IsFoundFalseWithNoFields()
    {
        // Arrange
        var report = FullScope with { Chain = new(null, [new(8, null, [])]) };

        // Act
        var json = JsonOutput.Format(report);

        // Assert
        Assert.EndsWith(
            "\"chain\":[{\"id\":8,\"found\":false,\"type\":null,\"title\":null,\"state\":null,\"tags\":null,"
                + "\"testCases\":[]}]}",
            json,
            StringComparison.Ordinal);
    }
}
