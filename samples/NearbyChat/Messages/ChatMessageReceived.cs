using NearbyChat.Models;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.Messages;

public sealed record ChatMessageReceived(NearbyDevice Device, ChatMessage Message);
