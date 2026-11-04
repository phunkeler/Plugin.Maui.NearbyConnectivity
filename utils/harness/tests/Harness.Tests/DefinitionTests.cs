using static Harness.Tests.Create;

namespace Harness.Tests;

public sealed class DefinitionTests
{
    public const string Valid = """
        {
          // the repository's overrides
          "version": 1,
          "boards": { "organization": "https://dev.azure.com/fabrikam", "project": "Mobile App" },
        }
        """;

    [Fact]
    public void Parse_Boards_ReadsTheOrganizationAndTheProject()
    {
        // Arrange
        const string json = Valid;

        // Act
        var definition = ProcessDefinition.Parse(json);

        // Assert
        Assert.Equal(Boards, definition.Boards);
    }

    // A placeholder left in the template must not fail the report; the Chain row reports it.
    [Fact]
    public void Parse_OrganizationPlaceholder_IsKeptAsWritten()
    {
        // Arrange
        var json = Definition(Valid, "https://dev.azure.com/fabrikam", "https://<ado-host>/<org>");

        // Act
        var definition = ProcessDefinition.Parse(json);

        // Assert
        Assert.Equal("https://<ado-host>/<org>", definition.Boards?.Organization);
    }

    [Fact]
    public void Parse_NoBoardsBlock_HasNoBoards()
    {
        // Arrange
        const string json = """{ "version": 1 }""";

        // Act
        var definition = ProcessDefinition.Parse(json);

        // Assert
        Assert.Null(definition.Boards);
    }

    [Fact]
    public void Parse_NoTestImpactBlock_UsesTheDefaults()
    {
        // Arrange
        const string json = Valid;

        // Act
        var definition = ProcessDefinition.Parse(json);

        // Assert
        var testImpact = definition.TestImpact;
        Assert.Equal(["uitest/**/*.cs"], testImpact.UiTests);
        Assert.Equal(["**/*Tests/**/*.cs", "test/**/*.cs", "tests/**/*.cs"], testImpact.UnitTests);
        Assert.Equal(TestImpactDefinition.DefaultTestCaseIdPattern, testImpact.TestCaseIdPattern.ToString());
        Assert.Equal(["android", "ios"], testImpact.Platforms.Keys);
        Assert.Equal(
            ["**/Platforms/Android/**", "**/*.Android.cs", "**/*.android.cs"], testImpact.Platforms["android"]);
        Assert.Equal(["**/Platforms/iOS/**", "**/*.iOS.cs", "**/*.ios.cs"], testImpact.Platforms["ios"]);
        Assert.Equal([".cs", ".xaml"], testImpact.SourceExtensions);
        Assert.Equal(new SearchLimit(Files: 2000, Megabytes: 20), testImpact.SearchLimit);
    }

    [Fact]
    public void Parse_SourceExtensionsAndSearchLimit_ReadsThem()
    {
        // Arrange
        var json = Definition(
            Valid,
            "\"version\": 1,",
            "\"version\": 1, \"testImpact\": "
                + "{ \"sourceExtensions\": [\".kt\", \".swift\"], \"searchLimit\": { \"files\": 5000 } },");

        // Act
        var definition = ProcessDefinition.Parse(json);

        // Assert
        Assert.Equal([".kt", ".swift"], definition.TestImpact.SourceExtensions);
        Assert.Equal(new SearchLimit(Files: 5000, Megabytes: 20), definition.TestImpact.SearchLimit);
    }

    [Fact]
    public void Parse_PartialTestImpactBlock_KeepsTheDefaultOfEachAbsentKey()
    {
        // Arrange
        var json = Definition(
            Valid,
            "\"version\": 1,",
            "\"version\": 1, \"testImpact\": "
                + "{ \"uiTests\": [\"ui/**\"], \"platforms\": { \"tizen\": [\"**/Tizen/**\"] } },");

        // Act
        var definition = ProcessDefinition.Parse(json);

        // Assert
        var testImpact = definition.TestImpact;
        Assert.Equal(["ui/**"], testImpact.UiTests);
        Assert.Equal(["**/*Tests/**/*.cs", "test/**/*.cs", "tests/**/*.cs"], testImpact.UnitTests);
        Assert.Equal(TestImpactDefinition.DefaultTestCaseIdPattern, testImpact.TestCaseIdPattern.ToString());
        Assert.Equal(["tizen"], testImpact.Platforms.Keys);
    }

    [Fact]
    public void Parse_TestCaseIdPattern_CompilesIt()
    {
        // Arrange
        var json = Definition(
            Valid, "\"version\": 1,", "\"version\": 1, \"testImpact\": { \"testCaseIdPattern\": \"Case([0-9]+)\" },");

        // Act
        var definition = ProcessDefinition.Parse(json);

        // Assert
        Assert.Equal("42", definition.TestImpact.TestCaseIdPattern.Match("Case42").Groups[1].Value);
    }

    [Theory]
    [InlineData("\"version\": 1,", "\"version\": 1, \"robot\": { \"githubAppId\": 1 },", "robot", Label = "cut-key")]
    [InlineData("\"version\": 1,", "", "version", Label = "no-version")]
    [InlineData("\"version\": 1", "\"version\": 2", "version", Label = "version-2")]
    [InlineData(
        "\"https://dev.azure.com/fabrikam\"", "\" \"", "boards.organization", Label = "blank-organization")]
    [InlineData("\"Mobile App\"", "\" \"", "boards.project", Label = "blank-project")]
    [InlineData("\"project\"", "\"team\"", "boards", Label = "unknown-boards-key")]
    [InlineData(
        "\"version\": 1,",
        "\"version\": 1, \"testImpact\": { \"testCaseIdPattern\": \"TC([\" },",
        "testImpact.testCaseIdPattern",
        Label = "bad-regex")]
    [InlineData(
        "\"version\": 1,",
        "\"version\": 1, \"testImpact\": { \"testCaseIdPattern\": \"TC[0-9]+\" },",
        "testImpact.testCaseIdPattern",
        Label = "regex-without-group")]
    [InlineData(
        "\"version\": 1,",
        "\"version\": 1, \"testImpact\": { \"automationIdPattern\": \"Id=([A-Za-z]+)\" },",
        "testImpact",
        Label = "cut-automation-id-pattern")]
    [InlineData(
        "\"version\": 1,",
        "\"version\": 1, \"testImpact\": { \"platforms\": { \"Android\": [\"**/Android/**\"] } },",
        "testImpact.platforms.Android",
        Label = "platform-name-upper-case")]
    [InlineData(
        "\"version\": 1,",
        "\"version\": 1, \"testImpact\": { \"platforms\": { \"ios\": [] } },",
        "testImpact.platforms.ios",
        Label = "platform-without-globs")]
    [InlineData(
        "\"version\": 1,",
        "\"version\": 1, \"testImpact\": { \"unitTests\": [\"/abs/**\"] },",
        "testImpact.unitTests[0]",
        Label = "absolute-test-glob")]
    [InlineData(
        "\"version\": 1,",
        "\"version\": 1, \"testImpact\": { \"e2eTests\": [] },",
        "testImpact",
        Label = "unknown-test-impact-key")]
    [InlineData(
        "\"version\": 1,",
        "\"version\": 1, \"testImpact\": { \"sourceExtensions\": [\".cs\", \"kt\"] },",
        "testImpact.sourceExtensions[1]",
        Label = "extension-without-dot")]
    [InlineData(
        "\"version\": 1,",
        "\"version\": 1, \"testImpact\": { \"sourceExtensions\": [] },",
        "testImpact.sourceExtensions",
        Label = "no-extensions")]
    [InlineData(
        "\"version\": 1,",
        "\"version\": 1, \"testImpact\": { \"searchLimit\": { \"files\": 0 } },",
        "testImpact.searchLimit.files",
        Label = "zero-files")]
    [InlineData(
        "\"version\": 1,",
        "\"version\": 1, \"testImpact\": { \"searchLimit\": { \"megabytes\": -1 } },",
        "testImpact.searchLimit.megabytes",
        Label = "negative-megabytes")]
    [InlineData(
        "\"version\": 1,",
        "\"version\": 1, \"testImpact\": { \"searchLimit\": { \"bytes\": 1 } },",
        "testImpact.searchLimit",
        Label = "unknown-search-limit-key")]
    [InlineData(Valid, "{", "harness.json", Label = "not-json")]
    public void Parse_InvalidDefinition_ThrowsUsageExceptionNamingThePath(string old, string replacement, string path)
    {
        // Arrange
        var json = Definition(Valid, old, replacement);

        // Act
        var exception = Record.Exception(() => ProcessDefinition.Parse(json));

        // Assert
        Assert.StartsWith(path, Assert.IsType<UsageException>(exception).Message, StringComparison.Ordinal);
    }
}
