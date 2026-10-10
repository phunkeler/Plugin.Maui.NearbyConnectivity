using Microsoft.Extensions.Logging;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>An <see cref="ILogger"/> that records the event id of every entry written to it.</summary>
sealed class RecordingLogger : ILogger
{
    readonly List<int> _eventIds = [];

    /// <summary>Every event id logged so far, oldest first.</summary>
    public IReadOnlyList<int> EventIds
    {
        get { lock (_eventIds) { return [.. _eventIds]; } }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_eventIds)
        {
            _eventIds.Add(eventId.Id);
        }
    }
}
