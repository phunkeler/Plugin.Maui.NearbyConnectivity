namespace NearbyChat.Models;

public abstract class MediaAttachment
{
    public abstract AttachmentType Type { get; }
    public string? FilePath { get; set; }
    public ImageSource? Thumbnail { get; set; }

    /// <summary>
    /// The picker result this attachment came from, when it was picked rather than received.
    /// Sending this directly lets the library stage the file, so the sample does not have to.
    /// </summary>
    public FileResult? SourceFile { get; set; }
}
