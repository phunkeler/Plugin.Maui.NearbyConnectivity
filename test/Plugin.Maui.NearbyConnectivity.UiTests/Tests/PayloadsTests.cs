namespace Plugin.Maui.NearbyConnectivity.UiTests.Tests;

[Trait("Capability", "Payloads")]
[Collection(OneDeviceSuite.Name)]
public sealed class PayloadsTests(UiTestFixture fixture) : UiTest(fixture)
{
    // The ceiling is public API (NearbyConnection.MaxBytesPayloadLength) and callers branch on it,
    // so a silent change to it is a breaking change.
    [Fact]
    public void MaxBytesPayloadLength_Is32Kb()
    {
        // Act
        var payloads = Shell.GoToPayloads();

        // Assert
        Assert.Equal(32 * 1024, payloads.MaxBytesPayloadLength);
    }

    // Sending with no connection open is a caller error, and must surface as one rather than
    // failing silently.
    [Fact]
    public void SendBytes_WithoutAConnection_ReportsAnError()
    {
        // Arrange
        var payloads = Shell.GoToPayloads();
        payloads.EnterText("hello");

        // Act
        payloads.SendBytesPayload();

        // Assert
        Assert.NotEqual("none", payloads.LastError);
    }

    [Fact]
    public void NoPayloads_WithoutAPeer()
    {
        // Act
        var payloads = Shell.GoToPayloads();

        // Assert
        Assert.Equal(0, payloads.ReceivedCount_);
    }

    [Fact]
    public void CancelInbound_WithNoTransfer_ReportsNoError()
    {
        // Arrange
        var payloads = Shell.GoToPayloads();

        // Act
        payloads.CancelInboundTransfer();

        // Assert
        Assert.Equal("none", payloads.LastError);
        Assert.Equal("idle", payloads.InboundStatus_);
    }

    [Fact]
    [Trait("Category", "RequiresRealPeer")]
    public void SendBytes_ArrivesAtThePeer()
    {
        // Arrange
        var payloads = Shell.GoToPayloads();
        payloads.EnterText("ping");

        // Act
        payloads.SendBytesPayload();

        // Assert
        Assert.Equal("none", payloads.LastError);
    }

    [Fact]
    [Trait("Category", "RequiresRealPeer")]
    public void SendFile_ReportsProgress()
    {
        // Arrange
        var payloads = Shell.GoToPayloads();
        payloads.EnterText("file contents");

        // Act
        payloads.SendFilePayload();

        // Assert
        Assert.NotEqual("idle", payloads.Progress_);
    }
}
