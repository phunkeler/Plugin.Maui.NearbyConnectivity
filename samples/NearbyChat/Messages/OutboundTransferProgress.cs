using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.Messages;

/// <summary>
/// Progress of a file this device is sending, mirroring <see cref="InboundTransferProgress"/> for
/// the other direction.
/// </summary>
/// <param name="Device">The device the file is going to.</param>
/// <param name="Progress">
/// The transfer's latest progress, or <see langword="null"/> once the send has ended however it
/// ended. The library does not always report a terminal status when a send throws, so the sender
/// says so itself.
/// </param>
public sealed record OutboundTransferProgress(NearbyDevice Device, NearbyTransferProgress? Progress);
