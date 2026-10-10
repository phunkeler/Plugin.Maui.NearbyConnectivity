using System.Globalization;
using NearbyChat.Services;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.Converters;

public class AvailabilityTextConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
        => value is NearbyAvailability availability
            ? NearbyAvailabilityText.Describe(availability)
            : string.Empty;

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}
