namespace NearbyChat.Messages;

/// <summary>
/// A device's chat history changed, so anything summarizing it should read it again.
/// </summary>
/// <param name="DeviceId">The device whose conversation changed.</param>
public sealed record ChatHistoryChanged(string DeviceId);
