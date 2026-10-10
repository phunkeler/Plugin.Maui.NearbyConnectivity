using System.Globalization;
using NearbyChat.Data;

namespace NearbyChat.ViewModels;

/// <summary>
/// One direction of a conversation, phrased for a stat tile.
/// </summary>
/// <param name="Data">The payload size, such as "640 KB".</param>
/// <param name="Messages">The message count, such as "1 message".</param>
/// <param name="Files">The file count, such as "0 files".</param>
/// <param name="HasMessages">Whether any message moved this way.</param>
/// <param name="HasFiles">Whether any file moved this way.</param>
public sealed record TrafficCaption(
    string Data,
    string Messages,
    string Files,
    bool HasMessages,
    bool HasFiles)
{
    /// <summary>
    /// Phrases <paramref name="traffic"/>.
    /// </summary>
    /// <param name="traffic">What moved in one direction.</param>
    /// <returns>The captions.</returns>
    public static TrafficCaption From(ChatTraffic traffic) => new(
        DataSize.Caption(traffic.Bytes),
        Count(traffic.Messages, "message", "messages"),
        Count(traffic.Files, "file", "files"),
        traffic.Messages > 0,
        traffic.Files > 0);

    static string Count(int count, string one, string many)
        => string.Create(CultureInfo.CurrentCulture, $"{count} {(count == 1 ? one : many)}");
}
