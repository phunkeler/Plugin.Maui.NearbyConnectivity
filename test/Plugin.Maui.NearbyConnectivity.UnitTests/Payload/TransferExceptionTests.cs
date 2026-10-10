namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Public-surface promises owned by this folder's types.
/// </summary>
[Trait("Category", "Session")]
public sealed class TransferExceptionTests
{
    // -------------------------------------------------------------------------
    // catching the transfer exception catches a stall
    // -------------------------------------------------------------------------

    [Fact]
    public void TransferTimeout_IsATransferException()
    {
        // Arrange & Act
        var timeout = new NearbyTransferTimeoutException("stalled", new IOException("inner"));

        // Assert
        Assert.IsAssignableFrom<NearbyTransferException>(timeout);
        Assert.NotNull(timeout.InnerException);
    }
}
