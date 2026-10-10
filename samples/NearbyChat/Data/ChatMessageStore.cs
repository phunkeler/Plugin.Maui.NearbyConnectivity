using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.Messaging;
using NearbyChat.Messages;
using NearbyChat.Models;
using NearbyChat.Services;

namespace NearbyChat.Data;

/// <summary>
/// The process-lifetime store for chat history, keyed by device id.
/// </summary>
/// <remarks>
/// <para>
/// A thread-safe in-memory dictionary, standing in for the database a real app would use. A
/// database-backed store is inherently async and an EF Core <c>DbContext</c> is a unit of work
/// that must not be shared, so replacing this means an async interface registered <c>Scoped</c>,
/// with one scope resolved per operation.
/// </para>
/// <para>
/// Every change is announced as a <see cref="ChatHistoryChanged"/>, for the same reason
/// <see cref="ConnectionConfirmations"/> announces its own: the connections list reads a
/// summary of what the chat sheet writes, and neither knows the other exists.
/// </para>
/// <para>
/// Incoming messages are held back until the person on this device has confirmed the
/// authentication code. The transport delivers them regardless, and the store keeps them, but
/// nothing reads them out until the confirmation lands. A confirmation therefore changes what the
/// history looks like, so it is announced as a history change too.
/// </para>
/// </remarks>
public sealed class ChatMessageStore : IRecipient<ConfirmationChanged>
{
    readonly ConcurrentDictionary<string, Session> _sessions = [];
    readonly IMessenger _messenger;
    readonly ConnectionConfirmations _confirmations;

    public ChatMessageStore(IMessenger messenger, ConnectionConfirmations confirmations)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(confirmations);

        _messenger = messenger;
        _confirmations = confirmations;

        messenger.Register(this);
    }

    /// <summary>
    /// A confirmation releases anything held for that device, which every reader sees as the
    /// history changing.
    /// </summary>
    public void Receive(ConfirmationChanged confirmation)
        => _messenger.Send(new ChatHistoryChanged(confirmation.DeviceId));

    public IReadOnlyList<ChatMessage> GetAll(string deviceId)
    {
        if (!_sessions.TryGetValue(deviceId, out var session))
        {
            return [];
        }

        var confirmed = _confirmations.IsConfirmed(deviceId);

        lock (session)
        {
            return [.. session.Messages.Where(m => confirmed || !IsHeld(m))];
        }
    }

    public ChatSummary Summarize(string deviceId)
    {
        if (!_sessions.TryGetValue(deviceId, out var session))
        {
            return default;
        }

        var confirmed = _confirmations.IsConfirmed(deviceId);

        lock (session)
        {
            var summary = default(ChatSummary);

            foreach (var message in session.Messages)
            {
                if (message.IsNotice || message.IsUndelivered)
                {
                    continue;
                }

                if (!confirmed && IsHeld(message))
                {
                    summary = summary with
                    {
                        Held = summary.Held + 1,
                        Received = summary.Received with { Bytes = summary.Received.Bytes + message.Bytes }
                    };
                    continue;
                }

                var isFile = message.Attachments.Count > 0;
                summary = message.Direction is NearbyDirection.Outgoing
                    ? summary with { Sent = summary.Sent.Add(isFile, message.Bytes) }
                    : summary with { Received = summary.Received.Add(isFile, message.Bytes) };
            }

            return summary;
        }
    }

    public void Add(string deviceId, ChatMessage message)
    {
        var session = _sessions.GetOrAdd(deviceId, _ => new Session());

        lock (session)
        {
            session.Messages.Add(message);
        }

        _messenger.Send(new ChatHistoryChanged(deviceId));
    }

    /// <summary>
    /// Records whether an outgoing message's last send failed, which changes the totals every
    /// reader shows.
    /// </summary>
    public void SetUndelivered(string deviceId, ChatMessage message, bool undelivered)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_sessions.TryGetValue(deviceId, out var session))
        {
            return;
        }

        lock (session)
        {
            if (message.IsUndelivered == undelivered)
            {
                return;
            }

            message.IsUndelivered = undelivered;
        }

        _messenger.Send(new ChatHistoryChanged(deviceId));
    }

    public void Clear(string deviceId)
    {
        if (!_sessions.TryRemove(deviceId, out var session))
        {
            return;
        }

        lock (session)
        {
            session.Messages.Clear();
        }

        _messenger.Send(new ChatHistoryChanged(deviceId));
    }

    static bool IsHeld(ChatMessage message) => message.Direction is NearbyDirection.Incoming && !message.IsNotice;

    sealed class Session
    {
        public List<ChatMessage> Messages { get; } = [];
    }
}
