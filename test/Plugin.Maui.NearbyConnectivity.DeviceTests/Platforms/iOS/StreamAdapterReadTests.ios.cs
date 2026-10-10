namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// The inbound stream adapter against a real <c>NSInputStream</c>: a partial-buffer read — the
/// shape a framing consumer uses — reads into the caller's array directly rather than through an
/// intermediate allocated per poll, and every read path returns the bytes the carrier holds.
/// </summary>
public sealed class StreamAdapterReadTests : DeviceTest
{
    static NsInputStreamAdapter Open(byte[] payload)
    {
        var stream = new NSInputStream(NSData.FromArray(payload));
        stream.Open();

        return new NsInputStreamAdapter(stream);
    }

    [Fact]
    public void Read_PartialBuffer_FillsRequestedRangeOnly()
    {
        // Arrange
        using var adapter = Open([1, 2, 3, 4, 5, 6, 7, 8]);
        var buffer = new byte[8];

        // Act
        // A header-sized read into a larger buffer — count < buffer.Length.
        var read = adapter.Read(buffer, 0, 4);

        // Assert
        Assert.Equal(4, read);
        Assert.Equal<byte>([1, 2, 3, 4], buffer[..4]);
        Assert.Equal<byte>([0, 0, 0, 0], buffer[4..]); // the tail is untouched
    }

    [Fact]
    public void Read_CountLargerThanBuffer_Throws()
    {
        // Arrange
        using var adapter = Open([1, 2, 3, 4, 5, 6, 7, 8]);
        var buffer = new byte[6];

        // Act & Assert
        // The range does not fit, so it is rejected at the boundary rather than
        // consuming bytes off the wire and then failing in the internal copy.
        // ArgumentOutOfRangeException, the subtype the framework's own range check throws;
        // Assert.Throws matches the exact type, not the ArgumentException base.
        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.Read(buffer, 2, 8));
    }

    [Fact]
    public void Read_AtOffset_WritesAtThatOffset()
    {
        // Arrange
        using var adapter = Open([9, 9, 9, 9]);
        var buffer = new byte[6];

        // Act
        // Offset != 0 still routes through the intermediate array.
        var read = adapter.Read(buffer, 2, 4);

        // Assert
        Assert.Equal(4, read);
        Assert.Equal<byte>([0, 0, 9, 9, 9, 9], buffer);
    }

    [Fact]
    public async Task ReadAsync_PartialMemory_FillsRequestedRangeOnly()
    {
        // Arrange
        using var adapter = Open([1, 2, 3, 4, 5, 6, 7, 8]);
        var buffer = new byte[8];

        // Act
        // The Memory overload with a slice — array-backed, count < array length.
        var read = await adapter.ReadAsync(buffer.AsMemory(0, 4), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, read);
        Assert.Equal<byte>([1, 2, 3, 4], buffer[..4]);
        Assert.Equal<byte>([0, 0, 0, 0], buffer[4..]);
    }

    [Fact]
    public async Task CopyToAsync_ReadsEveryByte()
    {
        // Arrange
        var payload = Enumerable.Range(0, 512).Select(i => (byte)i).ToArray();
        using var adapter = Open(payload);
        var destination = new MemoryStream();

        // Act
        await adapter.CopyToAsync(destination, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(payload, destination.ToArray());
    }
}
