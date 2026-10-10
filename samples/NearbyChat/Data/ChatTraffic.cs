namespace NearbyChat.Data;

/// <summary>
/// What moved in one direction of a conversation.
/// </summary>
/// <param name="Messages">How many text messages.</param>
/// <param name="Files">How many files.</param>
/// <param name="Bytes">How many payload bytes.</param>
public readonly record struct ChatTraffic(int Messages, int Files, long Bytes)
{
    /// <summary>
    /// Adds one message or file of <paramref name="bytes"/> payload bytes.
    /// </summary>
    /// <param name="isFile">Whether it was a file rather than a text message.</param>
    /// <param name="bytes">Its payload size.</param>
    /// <returns>The traffic with the item counted.</returns>
    public ChatTraffic Add(bool isFile, long bytes) => isFile
        ? this with { Files = Files + 1, Bytes = Bytes + bytes }
        : this with { Messages = Messages + 1, Bytes = Bytes + bytes };
}
