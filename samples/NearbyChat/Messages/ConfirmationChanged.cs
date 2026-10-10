namespace NearbyChat.Messages;

/// <summary>
/// Sent when a device's authentication-code confirmation is granted or forgotten.
/// </summary>
/// <remarks>
/// Confirmation is read on two screens at once — the connections row and an open chat sheet — and
/// changes from both the user interface thread and the ingestion loop's. Sampling it once per
/// navigation leaves whichever view did not make the change showing a stale answer, so the store
/// announces every change instead.
/// </remarks>
public sealed record ConfirmationChanged(string DeviceId, bool IsConfirmed);
