using System.Globalization;
using NearbyChat.Services;

namespace NearbyChat.Converters;

/// <summary>
/// Turns an index into <see cref="PresenceAvatar.Palette"/> into its <see cref="Color"/>.
/// </summary>
public class PaletteColorConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
        => value is int index && index >= 0 && index < PresenceAvatar.Palette.Count
            ? PresenceAvatar.Palette[index].Value
            : Colors.Transparent;

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}
