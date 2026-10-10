using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace NearbyChat.Converters;

/// <summary>
/// Turns a face's corner fraction into the <see cref="RoundRectangle"/> for a tile of the size
/// given as the parameter, so a view model can say "0.5" and leave the shape to the view.
/// </summary>
public class AvatarShapeConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
        => value is double fraction && parameter is string size
            ? new RoundRectangle { CornerRadius = fraction * double.Parse(size, CultureInfo.InvariantCulture) }
            : new RoundRectangle();

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}
