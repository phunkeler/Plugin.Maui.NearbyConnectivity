namespace NearbyChat.Data;

/// <summary>
/// What a device list needs to know about a conversation without loading it.
/// </summary>
/// <param name="Sent">What this device sent.</param>
/// <param name="Received">
/// What this device received. Its <see cref="ChatTraffic.Bytes"/> include held messages; its
/// <see cref="ChatTraffic.Messages"/> do not.
/// </param>
/// <param name="Held">
/// How many incoming messages are waiting for the person on this device to confirm the
/// authentication code. Until then they count toward <paramref name="Received"/> bytes only.
/// </param>
public readonly record struct ChatSummary(
    ChatTraffic Sent,
    ChatTraffic Received,
    int Held)
{
    /// <summary>
    /// How many text messages have been exchanged in both directions.
    /// </summary>
    public int Messages => Sent.Messages + Received.Messages;

    /// <summary>
    /// How many files have been exchanged in both directions.
    /// </summary>
    public int Files => Sent.Files + Received.Files;

    /// <summary>
    /// Payload bytes moved in both directions.
    /// </summary>
    public long Bytes => Sent.Bytes + Received.Bytes;
}
