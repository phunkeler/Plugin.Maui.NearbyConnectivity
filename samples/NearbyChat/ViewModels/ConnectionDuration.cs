namespace NearbyChat.ViewModels;

/// <summary>
/// Words for how long a connection has been up.
/// </summary>
public static class ConnectionDuration
{
    /// <summary>
    /// Describes an elapsed time as a caption, coarsening as it grows so a list row never has to
    /// tick faster than the unit it shows.
    /// </summary>
    /// <param name="elapsed">Time since the connection came up. Zero or negative reads as just now.</param>
    /// <returns>The caption.</returns>
    public static string Caption(TimeSpan elapsed) => elapsed switch
    {
        { TotalMinutes: < 1 } => "Connected just now",
        { TotalHours: < 1 } => $"Connected for {(int)elapsed.TotalMinutes} min",
        { TotalDays: < 1 } => $"Connected for {(int)elapsed.TotalHours}h {elapsed.Minutes}m",
        _ => $"Connected for {(int)elapsed.TotalDays}d {elapsed.Hours}h",
    };
}
