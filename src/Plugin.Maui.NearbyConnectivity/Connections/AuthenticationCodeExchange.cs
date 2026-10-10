using System.Security.Cryptography;

namespace Plugin.Maui.NearbyConnectivity;

// One handshake's commit-then-reveal round, for a platform whose handshake commits nothing (iOS).
// Each side commits to a random nonce and reveals it only after the peer's commitment has arrived,
// so a relay fixes its nonce for each leg before it learns the honest nonce on that leg and cannot
// steer the two legs' codes into agreement. Every rule here is that property: the binding is fixed
// before this side's commitment can go out, this side's commitment goes out before its reveal, a
// second commitment is refused rather than replacing the first, and a reveal is checked against the
// commitment it answers. Do not relax any of them to tolerate a reordering.
//
// The binding identifies the encrypted link the payloads travel over. A code not bound to it
// would match across a relay that passes this round through untouched.
//
// Frames are sent under the gate: MPC raises the state change that commits and the peer frame that
// reveals on different threads, and a reveal overtaking this side's own commitment on the wire is a
// violation the peer faults on.
sealed class AuthenticationCodeExchange(byte[]? binding)
{
    readonly byte[]? _binding = binding;
    readonly Lock _gate = new();
    readonly byte[] _nonce = RandomNumberGenerator.GetBytes(ControlMessage.CodeNonceBytes);

    bool _committed;
    bool _opened;
    byte[]? _peerCommitment;

    byte[] Commitment => field ??= SHA256.HashData(_nonce);

    internal bool Revealed { get; private set; }

    internal void Commit(Action<byte[]> send)
    {
        lock (_gate)
        {
            CommitOnce(send);
        }
    }

    internal void AnswerCommit(ReadOnlySpan<byte> commitment, Action<byte[]> send)
    {
        lock (_gate)
        {
            if (_peerCommitment is not null)
            {
                throw Violation("the peer committed twice");
            }

            if (commitment.SequenceEqual(Commitment))
            {
                throw Violation("the peer sent this device's own commitment back");
            }

            _peerCommitment = commitment.ToArray();
            CommitOnce(send);
            send(ControlMessage.EncodeCodeReveal(_nonce));
            Revealed = true;
        }
    }

    // Null without a binding: no code rather than one derived from the nonces alone. The reveal is
    // still checked first, so a missing binding never excuses a bad reveal.
    internal string? Open(ReadOnlySpan<byte> reveal)
    {
        lock (_gate)
        {
            if (_peerCommitment is null)
            {
                throw Violation("the peer revealed before it committed");
            }

            if (_opened)
            {
                throw Violation("the peer revealed twice");
            }

            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(reveal), _peerCommitment))
            {
                throw Violation("the peer's reveal does not match its commitment");
            }

            _opened = true;

            return _binding is null
                ? null
                : AuthenticationCodeRules.FromBindingAndNonces(_binding, _nonce, reveal);
        }
    }

    void CommitOnce(Action<byte[]> send)
    {
        if (_committed)
        {
            return;
        }

        _committed = true;
        send(ControlMessage.EncodeCodeCommit(Commitment));
    }

    static NearbyConnectException Violation(string reason)
        => new(NearbyConnectFailure.Failed, $"The authentication code exchange failed: {reason}.");
}
