namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers the pairing of a named stream's two halves: the name frame and the stream payload.
/// </summary>
/// <remarks>
/// The halves arrive separately and in either order, so the first one waits for the other. A peer
/// decides how many halves it sends without a partner, so the number that can wait is bounded for
/// each device, and a released device leaves none behind.
/// </remarks>
[Trait("Category", "Connections")]
public sealed class StreamPairingTests
{
    [Fact]
    public void AStreamPairsWithTheNameThatArrivedFirst()
    {
        // Arrange
        var pairing = new StreamPairing();
        using var stream = new MemoryStream();
        pairing.OfferName("a", payloadId: 7, "vitals", out _);

        // Act
        var half = pairing.OfferStream("a", payloadId: 7, stream, out var name);

        // Assert
        Assert.Equal(StreamHalf.Paired, half);
        Assert.Equal("vitals", name);
    }

    [Fact]
    public void ANamePairsWithTheStreamThatArrivedFirst()
    {
        // Arrange
        var pairing = new StreamPairing();
        using var stream = new MemoryStream();
        pairing.OfferStream("a", payloadId: 7, stream, out _);

        // Act
        var half = pairing.OfferName("a", payloadId: 7, "vitals", out var paired);

        // Assert
        Assert.Equal(StreamHalf.Paired, half);
        Assert.Same(stream, paired);
    }

    [Fact]
    public void AHalfFromAnotherDevice_DoesNotPair()
    {
        // Arrange
        var pairing = new StreamPairing();
        using var stream = new MemoryStream();
        pairing.OfferName("a", payloadId: 7, "vitals", out _);

        // Act
        var half = pairing.OfferStream("b", payloadId: 7, stream, out var name);

        // Assert
        Assert.Equal(StreamHalf.Waiting, half);
        Assert.Null(name);
    }

    [Fact]
    public void NamesPastTheLimit_AreRefused_ForThatDeviceOnly()
    {
        // Arrange
        var pairing = new StreamPairing();

        for (var id = 0; id < StreamPairing.MaxWaitingPerDevice; id++)
        {
            pairing.OfferName("a", id, "name", out _);
        }

        // Act
        var overLimit = pairing.OfferName("a", payloadId: 1000, "name", out _);
        var otherDevice = pairing.OfferName("b", payloadId: 1000, "name", out _);

        // Assert
        Assert.Equal(StreamHalf.Refused, overLimit);
        Assert.Equal(StreamHalf.Waiting, otherDevice);
    }

    [Fact]
    public void StreamsPastTheLimit_AreRefused()
    {
        // Arrange
        var pairing = new StreamPairing();
        using var stream = new MemoryStream();

        for (var id = 0; id < StreamPairing.MaxWaitingPerDevice; id++)
        {
            pairing.OfferStream("a", id, stream, out _);
        }

        // Act
        var overLimit = pairing.OfferStream("a", payloadId: 1000, stream, out _);

        // Assert
        Assert.Equal(StreamHalf.Refused, overLimit);
    }

    [Fact]
    public void Release_ReturnsTheWaitingStreamsAndForgetsTheWaitingNames()
    {
        // Arrange
        var pairing = new StreamPairing();
        using var waiting = new MemoryStream();
        using var late = new MemoryStream();
        pairing.OfferStream("a", payloadId: 1, waiting, out _);
        pairing.OfferName("a", payloadId: 2, "stale", out _);

        // Act
        var released = pairing.Release("a");
        var afterRelease = pairing.OfferStream("a", payloadId: 2, late, out var name);

        // Assert
        Assert.Same(waiting, Assert.Single(released));
        Assert.Equal(StreamHalf.Waiting, afterRelease);
        Assert.Null(name);
    }
}
