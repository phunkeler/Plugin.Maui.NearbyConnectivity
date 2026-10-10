namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// Inbound bytes payloads on iOS (<c>OnDataReceived</c>) are routed to the right connection's
/// receive stream and consumed through the internal <see cref="NearbyConnection.Inbound"/>
/// reader.
/// </summary>
public sealed class PayloadRoutingTests : DeviceTest
{
    [Fact]
    public async Task BytesPayload_RoutedToActiveConnectionReceiveStream()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (connection, _) = await Create.ConnectedAsync(platform, peerID, cts.Token);
        byte[] expected = [1, 2, 3];
        using var data = NSData.FromArray(expected);

        // Act
        platform.Ios().OnDataReceived(data, peerID);

        // Assert
        var received = await Receive.FirstAsync(connection, cts.Token);
        var bytesPayload = Assert.IsType<NearbyBytesPayload>(received);
        Assert.Equal(expected, bytesPayload.Data);
    }
}
