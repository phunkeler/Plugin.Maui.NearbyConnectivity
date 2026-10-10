namespace NearbyChat.Models;

public sealed class VideoAttachment : MediaAttachment
{
    public override AttachmentType Type => AttachmentType.Video;

    /// <summary>
    /// How long the video runs, or <see langword="null"/> when the file could not be read.
    /// </summary>
    public TimeSpan? Duration { get; set; }
}
