using System.Text.Json;
using System.Text.Json.Serialization;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.Services;

/// <summary>
/// The face and color a device picks for itself, carried in <see cref="NearbyPresence.Info"/>.
/// </summary>
/// <param name="Face">Index into <see cref="PresenceAvatar.FaceCount"/> shipped faces.</param>
/// <param name="Color">Index into <see cref="PresenceAvatar.Palette"/>.</param>
public readonly record struct AvatarChoice(int Face, int Color);

/// <summary>
/// Encodes an <see cref="AvatarChoice"/> into the presence's <c>Info</c> bytes and back.
/// </summary>
/// <remarks>
/// The wire form is the short-name JSON record the plugin's docs teach,
/// <c>{"a":7,"c":3}</c>, 13 of the 57 bytes. A peer's bytes are untrusted: anything that is not
/// that record, or is out of range, decodes to nothing and the peer shows initials instead.
/// The palette is content, not theme: a peer's color must look the same on every phone, so it
/// does not follow the app's light and dark tokens.
/// </remarks>
public static partial class PresenceAvatar
{
    public const int FaceCount = 10;

    public static IReadOnlyList<(string Name, Color Value)> Palette { get; } =
    [
        ("blue", Color.FromArgb("#2563EB")),
        ("green", Color.FromArgb("#15803D")),
        ("purple", Color.FromArgb("#9333EA")),
        ("amber", Color.FromArgb("#B45309")),
        ("rose", Color.FromArgb("#BE123C")),
        ("teal", Color.FromArgb("#0F766E")),
    ];

    /// <summary>
    /// The face's glyph in the <c>NearbyChatIcons</c> font: <c>face-0</c> to <c>face-9</c> sit at
    /// U+E020 to U+E029, see <c>Scripts/.fantasticonrc.js</c>.
    /// </summary>
    public static string FaceGlyph(int face) => ((char)(0xE020 + face)).ToString();

    /// <summary>
    /// The face's tile silhouette as a corner radius, a fraction of the tile size: 0.5 is a
    /// circle. The shape travels with the face index, so every phone draws the same one.
    /// </summary>
    public static double CornerFraction(int face) => s_corners[face];

    /// <summary>
    /// The palette entry the sheet's gallery shows a face in, so the ten faces read as ten and not
    /// as one block. The chosen color applies only in the preview and on the wire.
    /// </summary>
    public static int GalleryColorIndex(int face) => face % Palette.Count;

    static readonly double[] s_corners = [0.21, 0.5, 0.32, 0.21, 0.5, 0.29, 0.5, 0.21, 0.32, 0.5];

    /// <summary>
    /// The spoken name of a face, "Face 8, square": what the sheet's tile and the preview share.
    /// </summary>
    public static string DescribeFace(int face)
        => $"Face {face + 1}, {(CornerFraction(face) >= 0.5 ? "circle" : "square")}";

    /// <summary>
    /// The spoken name of a palette entry, capitalised for a label of its own: "Teal".
    /// </summary>
    public static string DescribeColor(int color)
    {
        var name = Palette[color].Name;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    public static string Describe(AvatarChoice choice)
        => $"{DescribeFace(choice.Face)}, {Palette[choice.Color].Name}";

    public static byte[] Encode(AvatarChoice choice)
    {
        return JsonSerializer.SerializeToUtf8Bytes(
            new PresenceInfo(choice.Face, choice.Color),
            PresenceJsonContext.Default.PresenceInfo);
    }

    public static AvatarChoice? Decode(ReadOnlyMemory<byte> info)
    {
        if (info.IsEmpty || info.Length > NearbyPresence.MaxInfoBytes)
        {
            return null;
        }

        PresenceInfo? record;

        try
        {
            record = JsonSerializer.Deserialize(info.Span, PresenceJsonContext.Default.PresenceInfo);
        }
        catch (JsonException)
        {
            return null;
        }

        return record is { Face: >= 0 and < FaceCount } r && r.Color >= 0 && r.Color < Palette.Count
            ? new AvatarChoice(r.Face, r.Color)
            : null;
    }

    /// <summary>
    /// Up to two letters for a device with no readable avatar. Counts text elements, not UTF-16
    /// units, so a name that starts with an emoji keeps the whole emoji.
    /// </summary>
    public static string Initials(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => "?",
            1 => string.Concat(parts[0].EnumerateRunes().Take(2).Select(r => r.ToString())).ToUpperInvariant(),
            _ => string.Concat(FirstRune(parts[0]), FirstRune(parts[^1])).ToUpperInvariant(),
        };

        static string FirstRune(string s) => s.EnumerateRunes().First().ToString();
    }

    sealed record PresenceInfo(
        [property: JsonPropertyName("a")] int Face,
        [property: JsonPropertyName("c")] int Color);

    [JsonSerializable(typeof(PresenceInfo))]
    sealed partial class PresenceJsonContext : JsonSerializerContext;
}
