using System.Security.Cryptography;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Connections")]
public class AuthenticationCodeExchangeTests
{
    static readonly byte[] s_binding = AuthenticationCodeRules.Unordered(
        SHA256.HashData("alice"u8), SHA256.HashData("bob"u8));

    /// <summary>One direction of a reliable, ordered link: what one side sent, in order.</summary>
    sealed class Wire
    {
        public List<byte[]> Frames { get; } = [];

        public void Send(byte[] frame) => Frames.Add(frame);
    }

    static ControlMessageType TypeOf(byte[] frame)
    {
        Assert.True(ControlMessage.TryDecode(frame, out var type));
        return type;
    }

    static byte[] Commitment(ReadOnlySpan<byte> nonce)
        => ControlMessage.EncodeCodeCommit(SHA256.HashData(nonce));

    public sealed class Agreement : AuthenticationCodeExchangeTests
    {
        [Fact]
        public void TwoHonestSides_DeriveTheSameCode()
        {
            // Arrange
            var alice = new AuthenticationCodeExchange(s_binding);
            var bob = new AuthenticationCodeExchange(s_binding);
            var toBob = new Wire();
            var toAlice = new Wire();

            // Act
            alice.Commit(toBob.Send);
            bob.Commit(toAlice.Send);
            bob.AnswerCommit(ControlMessage.Body(toBob.Frames[0]), toAlice.Send);
            alice.AnswerCommit(ControlMessage.Body(toAlice.Frames[0]), toBob.Send);
            var onAlice = alice.Open(ControlMessage.Body(toAlice.Frames[1]));
            var onBob = bob.Open(ControlMessage.Body(toBob.Frames[1]));

            // Assert
            Assert.NotNull(onAlice);
            Assert.Equal(onAlice, onBob);
        }

        [Fact]
        public void TwoRounds_OverTheSameCertificates_GiveDifferentCodes()
        {
            // A reconnect over a live MPC link keeps the binding; the fresh nonces are what give
            // the second connection its own code.

            // Arrange
            static string? Round()
            {
                var alice = new AuthenticationCodeExchange(s_binding);
                var bob = new AuthenticationCodeExchange(s_binding);
                var toBob = new Wire();
                var toAlice = new Wire();
                alice.Commit(toBob.Send);
                bob.AnswerCommit(ControlMessage.Body(toBob.Frames[0]), toAlice.Send);
                alice.AnswerCommit(ControlMessage.Body(toAlice.Frames[0]), toBob.Send);
                return alice.Open(ControlMessage.Body(toAlice.Frames[1]));
            }

            // Act
            var codes = Enumerable.Range(0, 8).Select(_ => Round()).ToHashSet();

            // Assert
            Assert.True(codes.Count > 1, "Eight rounds over one certificate pair produced one code.");
        }

        [Fact]
        public void MissingBinding_GivesNoCode_ButStillChecksTheReveal()
        {
            // Arrange
            var alice = new AuthenticationCodeExchange(binding: null);
            var wrong = new AuthenticationCodeExchange(binding: null);
            var nonce = new byte[ControlMessage.CodeNonceBytes];
            alice.AnswerCommit(ControlMessage.Body(Commitment(nonce)), new Wire().Send);
            wrong.AnswerCommit(ControlMessage.Body(Commitment(nonce)), new Wire().Send);
            var mismatched = Enumerable.Repeat((byte)1, ControlMessage.CodeNonceBytes).ToArray();

            // Act
            var code = alice.Open(nonce);

            // Assert
            Assert.Null(code);
            Assert.Throws<NearbyConnectException>(() => wrong.Open(mismatched));
        }
    }

    public sealed class Ordering : AuthenticationCodeExchangeTests
    {
        [Fact]
        public void AnsweringFirst_SendsTheCommitmentBeforeTheReveal()
        {
            // The peer's commitment can arrive before this side's own state change has committed.
            // A reveal that overtook this side's commitment would let the peer choose its nonce
            // after seeing this one.

            // Arrange
            var alice = new AuthenticationCodeExchange(s_binding);
            var toBob = new Wire();
            ControlMessageType[] expected = [ControlMessageType.CodeCommit, ControlMessageType.CodeReveal];

            // Act
            alice.AnswerCommit(ControlMessage.Body(Commitment(new byte[ControlMessage.CodeNonceBytes])), toBob.Send);
            alice.Commit(toBob.Send);

            // Assert
            Assert.Equal(expected, toBob.Frames.Select(TypeOf));
        }

        [Fact]
        public void Revealed_TurnsTrueOnlyWhenTheNonceGoesOut()
        {
            // The adapter counts a failure after this point as a spent attempt.

            // Arrange
            var alice = new AuthenticationCodeExchange(s_binding);
            alice.Commit(new Wire().Send);
            var before = alice.Revealed;

            // Act
            alice.AnswerCommit(ControlMessage.Body(Commitment(new byte[ControlMessage.CodeNonceBytes])), new Wire().Send);

            // Assert
            Assert.False(before);
            Assert.True(alice.Revealed);
        }

        [Fact]
        public void Commit_IsSentOnce()
        {
            // Arrange
            var alice = new AuthenticationCodeExchange(s_binding);
            var toBob = new Wire();

            // Act
            alice.Commit(toBob.Send);
            alice.Commit(toBob.Send);

            // Assert
            Assert.Equal(ControlMessageType.CodeCommit, TypeOf(Assert.Single(toBob.Frames)));
        }
    }

    public sealed class Violations : AuthenticationCodeExchangeTests
    {
        [Fact]
        public void RevealWithoutACommitment_Faults()
        {
            // Arrange
            var alice = new AuthenticationCodeExchange(s_binding);
            alice.Commit(new Wire().Send);

            // Act
            void Act() => alice.Open(new byte[ControlMessage.CodeNonceBytes]);

            // Assert
            var ex = Assert.Throws<NearbyConnectException>(Act);
            Assert.Equal(NearbyConnectFailure.Failed, ex.Reason);
        }

        [Fact]
        public void RevealThatDoesNotMatchTheCommitment_Faults()
        {
            // Arrange
            var alice = new AuthenticationCodeExchange(s_binding);
            var committed = new byte[ControlMessage.CodeNonceBytes];
            var revealed = new byte[ControlMessage.CodeNonceBytes];
            revealed[0] = 1;
            alice.AnswerCommit(ControlMessage.Body(Commitment(committed)), new Wire().Send);

            // Act
            void Act() => alice.Open(revealed);

            // Assert
            Assert.Equal(NearbyConnectFailure.Failed, Assert.Throws<NearbyConnectException>(Act).Reason);
        }

        [Fact]
        public void SecondCommitment_Faults_AndDoesNotReplaceTheFirst()
        {
            // A relay that could recommit after seeing this side's reveal would choose its nonce
            // with the honest one in hand.

            // Arrange
            var alice = new AuthenticationCodeExchange(s_binding);
            var first = new byte[ControlMessage.CodeNonceBytes];
            var second = Enumerable.Repeat((byte)9, ControlMessage.CodeNonceBytes).ToArray();
            alice.AnswerCommit(ControlMessage.Body(Commitment(first)), new Wire().Send);

            // Act
            void Act() => alice.AnswerCommit(ControlMessage.Body(Commitment(second)), new Wire().Send);

            // Assert
            Assert.Throws<NearbyConnectException>(Act);
            Assert.Throws<NearbyConnectException>(() => alice.Open(second));
        }

        [Fact]
        public void ReflectedCommitment_Faults()
        {
            // Arrange
            var alice = new AuthenticationCodeExchange(s_binding);
            var toBob = new Wire();
            alice.Commit(toBob.Send);

            // Act
            void Act() => alice.AnswerCommit(ControlMessage.Body(toBob.Frames[0]), new Wire().Send);

            // Assert
            Assert.Throws<NearbyConnectException>(Act);
            Assert.False(alice.Revealed);
        }

        [Fact]
        public void SecondReveal_Faults()
        {
            // Arrange
            var alice = new AuthenticationCodeExchange(s_binding);
            var nonce = new byte[ControlMessage.CodeNonceBytes];
            alice.AnswerCommit(ControlMessage.Body(Commitment(nonce)), new Wire().Send);
            alice.Open(nonce);

            // Act
            void Act() => alice.Open(nonce);

            // Assert
            Assert.Throws<NearbyConnectException>(Act);
        }
    }
}
