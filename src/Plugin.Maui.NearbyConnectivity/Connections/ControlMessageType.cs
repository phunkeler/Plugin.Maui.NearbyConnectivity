namespace Plugin.Maui.NearbyConnectivity;

enum ControlMessageType : byte
{
    Disconnect = 0x01,
    StreamName = 0x02,
    ConnectRequest = 0x03,
    ConnectAccepted = 0x04,
    ConnectRejected = 0x05,
    Advertisement = 0x06,
    CodeCommit = 0x07,
    CodeReveal = 0x08,
}
