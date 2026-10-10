namespace NearbyChat.Models;

public record ChatMessage(
    string? Text,
    NearbyDirection Direction,
    DateTimeOffset Timestamp)
{
    public IList<MediaAttachment> Attachments { get; } = [];

    /// <summary>
    /// How many payload bytes this message put on the wire: the encoded text, or the file's size.
    /// Framing and encryption are the transport's business and are not counted.
    /// </summary>
    public long Bytes { get; init; }

    /// <summary>
    /// Whether this is a line the app added to the conversation, such as a file that never
    /// arrived, rather than something either person sent. A notice is never held, counted or sent.
    /// </summary>
    public bool IsNotice { get; init; }

    /// <summary>
    /// Whether the last attempt to send this outgoing message failed. Undelivered messages do not
    /// count toward the conversation's totals, since none of their bytes reached the other device.
    /// </summary>
    public bool IsUndelivered { get; set; }
}
