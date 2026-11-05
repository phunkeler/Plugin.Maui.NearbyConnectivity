namespace Harness.Tests;

public sealed class BoardsDefinitionTests
{
    static readonly BoardsDefinition s_configured = new("https://dev.azure.com/fabrikam", "Mobile App");

    [Theory]
    [InlineData("https://dev.azure.com/fabrikam", "Field Service", "https://dev.azure.com/fabrikam/Field%20Service/_workitems/edit/7")]
    [InlineData("https://dev.azure.com/fabrikam/", "Fiber", "https://dev.azure.com/fabrikam/Fiber/_workitems/edit/7")]
    [InlineData("https://fabrikam.visualstudio.com", "Fiber", "https://fabrikam.visualstudio.com/Fiber/_workitems/edit/7")]
    [InlineData("https://boards.example/fabrikam", "Fiber", null)]
    [InlineData(null, "Fiber", null)]
    public void WorkItemUrl_Settings_LinksOnlyOnAnAzureDevOpsHost(
        string? organization, string? project, string? expected)
    {
        // Arrange
        var boards = new BoardsDefinition(organization, project);

        // Act
        var url = boards.WorkItemUrl(7);

        // Assert
        Assert.Equal(expected, url);
    }

    [Fact]
    public void Resolve_NothingInTheEnvironment_KeepsTheConfiguredKeys()
    {
        // Arrange
        var environment = BoardsEnvironment.None;

        // Act
        var boards = BoardsDefinition.Resolve(s_configured, environment);

        // Assert
        Assert.Equal(s_configured, boards);
    }

    [Fact]
    public void Resolve_BothVariablesSet_TheEnvironmentWins()
    {
        // Arrange
        var environment = new BoardsEnvironment("https://dev.azure.com/northwind", "Tablet");

        // Act
        var boards = BoardsDefinition.Resolve(s_configured, environment);

        // Assert
        Assert.Equal(new BoardsDefinition("https://dev.azure.com/northwind", "Tablet"), boards);
    }

    [Fact]
    public void Resolve_OnlyTheProjectSet_WinsKeyByKey()
    {
        // Arrange
        var environment = new BoardsEnvironment(Organization: " ", Project: "Tablet");

        // Act
        var boards = BoardsDefinition.Resolve(s_configured, environment);

        // Assert
        Assert.Equal(new BoardsDefinition("https://dev.azure.com/fabrikam", "Tablet"), boards);
    }

    [Fact]
    public void Resolve_NoBlockAndNoEnvironment_IsNotComplete()
    {
        // Arrange
        var environment = BoardsEnvironment.None;

        // Act
        var boards = BoardsDefinition.Resolve(configured: null, environment);

        // Assert
        Assert.False(boards.IsComplete);
    }

    [Fact]
    public void Resolve_NoBlockButTheEnvironment_IsComplete()
    {
        // Arrange
        var environment = new BoardsEnvironment("https://dev.azure.com/northwind", "Tablet");

        // Act
        var boards = BoardsDefinition.Resolve(configured: null, environment);

        // Assert
        Assert.True(boards.IsComplete);
    }

    [Theory]
    [InlineData("https://dev.azure.com/fabrikam", true, Label = "dev-azure-com")]
    [InlineData("https://fabrikam.visualstudio.com", true, Label = "visualstudio-com")]
    [InlineData("https://DEV.AZURE.COM/fabrikam/", true, Label = "case-and-trailing-slash")]
    [InlineData("https://boards.example/fabrikam", false, Label = "other-host")]
    [InlineData("https://dev.azure.com.evil.example/fabrikam", false, Label = "host-prefix-spoof")]
    [InlineData("https://evilvisualstudio.com", false, Label = "suffix-without-dot")]
    [InlineData("http://dev.azure.com/fabrikam", false, Label = "http")]
    [InlineData("dev.azure.com/fabrikam", false, Label = "relative")]
    [InlineData("https://dev.azure.com/<org>", false, Label = "placeholder")]
    public void IsOrganizationUrl_Value_AcceptsOnlyHttpsOnAnAzureDevOpsHost(string organization, bool expected)
    {
        // Arrange
        var value = organization;

        // Act
        var isUrl = BoardsDefinition.IsOrganizationUrl(value);

        // Assert
        Assert.Equal(expected, isUrl);
    }
}
