using System.Globalization;
using NearbyChat.Models;

namespace NearbyChat.ViewModels;

public sealed class VideoMessageViewModel(ChatMessage model) : ChatMessageViewModel(model)
{
    public VideoAttachment? Attachment =>
        Model.Attachments.OfType<VideoAttachment>().FirstOrDefault();

    /// <summary>
    /// How long the video runs and how big it is, such as "0:03 · 2.2 KB", or the size alone when
    /// the duration could not be read.
    /// </summary>
    public string DetailsCaption => Attachment?.Duration is { } duration
        ? $"{FormatDuration(duration)} · {DataSize.Caption(Model.Bytes)}"
        : DataSize.Caption(Model.Bytes);

    static string FormatDuration(TimeSpan duration)
        => duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.CurrentCulture)
            : duration.ToString(@"m\:ss", CultureInfo.CurrentCulture);
}
