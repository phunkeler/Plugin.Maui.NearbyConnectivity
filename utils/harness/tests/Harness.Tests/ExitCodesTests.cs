using Harness.Cli;

namespace Harness.Tests;

// The exit code of each failure.
public sealed class ExitCodesTests
{
    // A row is the exception's type, so Test Explorer can list it; an exception is not serializable theory data. Each
    // type has a constructor that takes only the message.
    [Theory]
    [InlineData(typeof(UsageException), 2, Label = "usage")]
    [InlineData(typeof(RecordNotFoundException), 3, Label = "not-found")]
    [InlineData(typeof(RecordUnavailableException), 5, Label = "unavailable")]
    [InlineData(typeof(TimeoutException), 5, Label = "timeout")]
    [InlineData(typeof(InvalidOperationException), 5, Label = "invalid-operation")]
    public void FromException_Failure_MapsToTheDocumentedCode(Type type, int expected)
    {
        // Arrange
        var exception = Assert.IsType<Exception>(Activator.CreateInstance(type, "x"), exactMatch: false);

        // Act
        var code = ExitCodes.FromException(exception);

        // Assert
        Assert.Equal(expected, code);
    }
}
