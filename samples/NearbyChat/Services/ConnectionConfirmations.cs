using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.Messaging;
using NearbyChat.Messages;

namespace NearbyChat.Services;

/// <summary>
/// Remembers which connections a person has confirmed by comparing authentication codes out loud.
/// </summary>
/// <remarks>
/// <para>
/// The sample's own state. Nothing in the plugin tracks a confirmation or refuses to send without
/// one: the library derives <see cref="Plugin.Maui.NearbyConnectivity.NearbyConnection.AuthenticationCode"/>
/// and stops there, so gating is the application's decision. This is what that decision looks like.
/// </para>
/// <para>
/// Keyed by device id and deliberately not persisted. A device id lasts one session, and so does the
/// handshake a code came from, so a confirmation carried into a later session would vouch for a
/// connection nobody compared. Forgetting on disconnect is what makes a reconnect ask again.
/// </para>
/// <para>
/// Every change is announced as a <see cref="ConfirmationChanged"/>. Two views read this state and
/// only one of them makes any given change — a chat sheet open while somebody confirms on the
/// connections page behind it would otherwise keep its send button disabled with nothing to
/// re-enable it.
/// </para>
/// </remarks>
public sealed class ConnectionConfirmations(IMessenger messenger)
{
    // A set, not a map: nothing is stored against the id. Concurrent because Forget runs on the
    // ingestion loop's thread while the reads and Confirm run on the UI thread.
    readonly ConcurrentDictionary<string, bool> _confirmed = new(StringComparer.Ordinal);
    readonly IMessenger _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));

    /// <summary>Records that the two codes matched for this device.</summary>
    public void Confirm(string deviceId)
    {
        if (_confirmed.TryAdd(deviceId, true))
        {
            _messenger.Send(new ConfirmationChanged(deviceId, IsConfirmed: true));
        }
    }

    /// <summary>Whether this device's codes have been compared and matched this session.</summary>
    public bool IsConfirmed(string deviceId) => _confirmed.ContainsKey(deviceId);

    /// <summary>
    /// Forgets a device's confirmation, so a reconnect has to be compared again.
    /// </summary>
    /// <remarks>
    /// A new connection to the same device is a new handshake and therefore a new code. Keeping the
    /// old answer would let a relayed reconnect inherit a confirmation from a connection that was
    /// never relayed.
    /// </remarks>
    public void Forget(string deviceId)
    {
        if (_confirmed.TryRemove(deviceId, out _))
        {
            _messenger.Send(new ConfirmationChanged(deviceId, IsConfirmed: false));
        }
    }
}
