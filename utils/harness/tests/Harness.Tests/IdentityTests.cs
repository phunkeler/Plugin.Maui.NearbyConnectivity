namespace Harness.Tests;

public sealed class IdentityTests
{
    // GitHub logins are case-insensitive, and an App's bot account is written with a trailing [bot].
    [Theory]
    [InlineData("AGENT", Label = "upper-case")]
    [InlineData("agent[bot]", Label = "bot-suffix")]
    [InlineData("AGENT[bot]", Label = "both")]
    public void FromGitHubLogin_OtherSpelling_IsTheSameIdentity(string login)
    {
        // Arrange
        var agent = Identity.FromGitHubLogin("agent");

        // Act
        var identity = Identity.FromGitHubLogin(login);

        // Assert
        Assert.Equal(agent, identity);
    }
}
