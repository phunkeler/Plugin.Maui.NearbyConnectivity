namespace NearbyChat.Models;

public sealed class PhotoAttachment : MediaAttachment
{
    public override AttachmentType Type => AttachmentType.Photo;
}
