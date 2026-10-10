using System.Globalization;

namespace NearbyChat.ViewModels;

/// <summary>
/// Words for an amount of data, sized for a list row.
/// </summary>
public static class DataSize
{
    static readonly string[] s_units = ["B", "KB", "MB", "GB"];

    /// <summary>
    /// Formats a byte count with the largest unit that keeps the number under a thousand, to one
    /// decimal place above bytes.
    /// </summary>
    /// <param name="bytes">The byte count. Negative values read as zero.</param>
    /// <returns>The caption, such as "340 B" or "1.2 MB".</returns>
    public static string Caption(long bytes)
    {
        double value = Math.Max(bytes, 0);
        var unit = 0;

        while (value >= 1000 && unit < s_units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{(long)value} {s_units[unit]}")
            : string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {s_units[unit]}");
    }
}
