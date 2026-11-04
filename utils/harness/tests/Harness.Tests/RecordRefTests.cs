namespace Harness.Tests;

public sealed class RecordRefTests
{
    [Theory]
    [InlineData("#57", 57)]
    [InlineData("57", 57)]
    public void Parse_PullRequestReference_ReturnsItsNumber(string text, int number)
    {
        // Arrange
        var expected = new RecordRef(number);

        // Act
        var record = RecordRef.Parse(text);

        // Assert
        Assert.Equal(expected, record);
    }

    [Theory]
    [InlineData("https://github.com/dotnet/maui/pull/21", 21, "dotnet/maui", Label = "address")]
    [InlineData("https://github.com/dotnet/maui/pull/21/files", 21, "dotnet/maui", Label = "address-with-tab")]
    [InlineData("https://github.com/dotnet/maui/pull/21#issuecomment-1", 21, "dotnet/maui", Label = "address-with-anchor")]
    public void Parse_PullRequestAddress_ReturnsItsNumberAndRepository(string text, int number, string repository)
    {
        // Arrange
        var expected = new RecordRef(number, repository);

        // Act
        var record = RecordRef.Parse(text);

        // Assert
        Assert.Equal(expected, record);
    }

    [Fact]
    public void ToString_Record_WritesHashAndNumber()
    {
        // Arrange
        var record = new RecordRef(57);

        // Act
        var text = record.ToString();

        // Assert
        Assert.Equal("#57", text);
    }

    [Fact]
    public void Parse_WorkItemReference_SaysToPassThePullRequest()
    {
        // Arrange
        const string text = "AB#4711";

        // Act
        var exception = Record.Exception(() => RecordRef.Parse(text));

        // Assert
        Assert.Contains("pass the pull request", Assert.IsType<UsageException>(exception).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("#0")]
    [InlineData("#-1")]
    [InlineData("")]
    [InlineData("https://github.com/dotnet/maui/pull/0")]
    [InlineData("https://github.com/dotnet/maui/issues/21")]
    public void Parse_OtherText_ThrowsUsageException(string text)
    {
        // Arrange
        Action parse = () => RecordRef.Parse(text);

        // Act
        var exception = Record.Exception(parse);

        // Assert
        Assert.IsType<UsageException>(exception);
    }
}
