namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// How an activity stopped.
/// </summary>
/// <param name="Reason">Why it stopped.</param>
/// <param name="Error">
/// The fault that ended it when <paramref name="Reason"/> is
/// <see cref="NearbyActivityStopReason.Faulted"/>; otherwise <see langword="null"/>.
/// </param>
/// <remarks>
/// Read from <see cref="INearbyActivity.LastStop"/>. What blocks a restart is deliberately not
/// carried here: computing it needs a fresh platform check that a routine stop should not pay for.
/// Call <see cref="INearbyConnectivity.CheckAvailabilityAsync(CancellationToken)"/> when diagnosing a
/// fault.
/// </remarks>
public sealed record NearbyActivityStop(NearbyActivityStopReason Reason, Exception? Error);
