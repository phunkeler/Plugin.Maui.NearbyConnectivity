using System.Security.Cryptography;

namespace Plugin.Maui.NearbyConnectivity.DeviceTests;

/// <summary>
/// Drives the inbound data callback the way MPC does, owning the <see cref="NSData"/> lifetime so
/// tests read as "this peer sent this" rather than as native buffer construction.
/// </summary>
static class Deliver
{
    /// <summary>Delivers a plugin control frame from <paramref name="peerID"/>.</summary>
    /// <param name="platform">The platform whose callback receives the frame.</param>
    /// <param name="peerID">The peer the frame arrives from.</param>
    /// <param name="type">The control type to encode.</param>
    internal static void ControlFrame(NearbyTransport platform, MCPeerID peerID, ControlMessageType type)
        => Bytes(platform, peerID, ControlMessage.Encode(type));

    /// <summary>
    /// Delivers an honest peer's half of the authentication code exchange: a commitment to a fresh
    /// nonce, then the nonce. Every iOS handshake surfaces only on this reveal.
    /// </summary>
    /// <param name="platform">The platform whose callback receives the frames.</param>
    /// <param name="peerID">The peer the frames arrive from.</param>
    internal static void CodeExchange(NearbyTransport platform, MCPeerID peerID)
    {
        var nonce = RandomNumberGenerator.GetBytes(ControlMessage.CodeNonceBytes);

        Bytes(platform, peerID, ControlMessage.EncodeCodeCommit(SHA256.HashData(nonce)));
        Bytes(platform, peerID, ControlMessage.EncodeCodeReveal(nonce));
    }

    /// <summary>
    /// Delivers a frame carrying a valid signature but an undefined control type, which a peer on a
    /// newer plugin version would send.
    /// </summary>
    /// <param name="platform">The platform whose callback receives the frame.</param>
    /// <param name="peerID">The peer the frame arrives from.</param>
    internal static void UnknownControlFrame(NearbyTransport platform, MCPeerID peerID)
    {
        var frame = ControlMessage.Encode(ControlMessageType.Disconnect);
        frame[^1] = 0xFF;

        Bytes(platform, peerID, frame);
    }

    /// <summary>Delivers raw application bytes from <paramref name="peerID"/>.</summary>
    /// <param name="platform">The platform whose callback receives the data.</param>
    /// <param name="peerID">The peer the data arrives from.</param>
    /// <param name="data">The bytes to deliver.</param>
    internal static void Bytes(NearbyTransport platform, MCPeerID peerID, byte[] data)
    {
        using var native = NSData.FromArray(data);

        platform.Ios().OnDataReceived(native, peerID);
    }
}
