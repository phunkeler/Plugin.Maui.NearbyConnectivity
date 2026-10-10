namespace Plugin.Maui.NearbyConnectivity;

static partial class NearbyLog
{
    [LoggerMessage(EventId = 1107, Level = LogLevel.Debug, Message = "Device {DeviceId} changed state to {State}.")]
    internal static partial void PeerStateChanged(ILogger logger, string deviceId, MCSessionState state);
}
