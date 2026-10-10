using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.Messages;

public sealed record InboundTransferProgress(NearbyDevice Device, NearbyTransferProgress Progress);
