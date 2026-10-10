using System.Security.Cryptography;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers the derivation behind <c>NearbyConnection.AuthenticationCode</c>.
/// </summary>
/// <remarks>
/// The code is only useful if two phones that share a connection show the same six digits and two
/// phones that do not show different ones. Neither property can be observed from one device, so
/// both are pinned here rather than on-device.
/// </remarks>
[Trait("Category", "Connections")]
public class AuthenticationCodeRulesTests
{
    static byte[] Bytes(byte seed, int length = 32)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, seed);
        return bytes;
    }

    public sealed class Shape : AuthenticationCodeRulesTests
    {
        [Theory]
        [InlineData(0, TestDisplayName = "all zero bytes")]
        [InlineData(1, TestDisplayName = "all one bytes")]
        [InlineData(255, TestDisplayName = "all high bytes")]
        public void Code_IsAlwaysSixDigits(byte seed)
        {
            // Arrange
            var token = Bytes(seed);

            // Act
            var code = AuthenticationCodeRules.FromToken(token);

            // Assert
            Assert.Equal(6, code.Length);
            Assert.All(code, c => Assert.True(char.IsAsciiDigit(c), $"'{code}' contains a non-digit."));
        }

        [Fact]
        public void SmallValue_IsZeroPaddedRatherThanShortened()
        {
            // A code that renders as "482" when the value is 482 would be compared against a
            // six-box entry field on the other phone and read as a mismatch.

            // Arrange
            // Search for a token whose derived value is under 100000, so the padding path is real
            // rather than assumed.
            byte[]? small = null;
            for (var i = 0; i < 1000 && small is null; i++)
            {
                var candidate = Bytes((byte)(i % 256), length: 1 + (i / 256));
                if (AuthenticationCodeRules.FromToken(candidate).StartsWith('0'))
                {
                    small = candidate;
                }
            }

            Assert.SkipWhen(small is null, "No short-value token found in the search space.");

            // Act
            var code = AuthenticationCodeRules.FromToken(small);

            // Assert
            Assert.Equal(6, code.Length);
        }

        [Fact]
        public void Code_IsCultureInvariant()
        {
            // A locale with non-ASCII digits or a different negative/grouping convention must not
            // change what the two phones display.

            // Arrange
            var token = Bytes(7);
            var original = Thread.CurrentThread.CurrentCulture;

            try
            {
                // Act
                Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
                var arabic = AuthenticationCodeRules.FromToken(token);

                Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                var invariant = AuthenticationCodeRules.FromToken(token);

                // Assert
                Assert.Equal(invariant, arabic);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }
    }

    public sealed class Rejection : AuthenticationCodeRulesTests
    {
        [Fact]
        public void EmptyToken_IsRefused()
        {
            // Hashing nothing would give every tokenless connection the same six digits, and two
            // people comparing them would see a match. Android hands over null when its handshake
            // produced no token; the adapter leaves the code empty rather than reaching this.

            // Act & Assert
            Assert.Throws<ArgumentException>(() => AuthenticationCodeRules.FromToken(ReadOnlySpan<byte>.Empty));
        }
    }

    public sealed class Agreement : AuthenticationCodeRulesTests
    {
        [Fact]
        public void SameToken_ProducesTheSameCode()
        {
            // Android's case: both sides read identical bytes from the UKEY2 handshake.

            // Arrange
            var initiator = Bytes(42);
            var acceptor = Bytes(42);

            // Act
            var left = AuthenticationCodeRules.FromToken(initiator);
            var right = AuthenticationCodeRules.FromToken(acceptor);

            // Assert
            Assert.Equal(left, right);
        }

        [Theory]
        [InlineData(1, 2, TestDisplayName = "Alice's half sorts first")]
        [InlineData(2, 1, TestDisplayName = "Bob's half sorts first")]
        public void BindingAndNonces_AreOrderIndependent(byte aliceSeed, byte bobSeed)
        {
            // iOS's case: each side names its own certificate and nonce "local" and the peer's
            // "remote", so the two sides pass the same inputs in opposite roles and must agree.

            // Arrange
            var alice = Bytes(aliceSeed);
            var bob = Bytes(bobSeed);
            var aliceNonce = Bytes(aliceSeed, length: 16);
            var bobNonce = Bytes(bobSeed, length: 16);

            // Act
            var onAlice = AuthenticationCodeRules.FromBindingAndNonces(
                AuthenticationCodeRules.Unordered(alice, bob), aliceNonce, bobNonce);
            var onBob = AuthenticationCodeRules.FromBindingAndNonces(
                AuthenticationCodeRules.Unordered(bob, alice), bobNonce, aliceNonce);

            // Assert
            Assert.Equal(onAlice, onBob);
        }

        [Fact]
        public void IdenticalHalves_StillProduceACode()
        {
            // Degenerate but reachable: a relay echoing a side's own commitment and nonce back.

            // Arrange
            var same = Bytes(3);
            var nonce = Bytes(4, length: 16);

            // Act
            var code = AuthenticationCodeRules.FromBindingAndNonces(
                AuthenticationCodeRules.Unordered(same, same), nonce, nonce);

            // Assert
            Assert.Equal(6, code.Length);
        }
    }

    public sealed class Discrimination : AuthenticationCodeRulesTests
    {
        [Fact]
        public void DifferentTokens_ProduceDifferentCodes()
        {
            // Arrange
            var left = Bytes(1);
            var right = Bytes(2);

            // Act & Assert
            Assert.NotEqual(
                AuthenticationCodeRules.FromToken(left),
                AuthenticationCodeRules.FromToken(right));
        }

        [Fact]
        public void RelayedPair_ProducesADifferentCodeOnEachLeg()
        {
            // The whole point of the feature. A machine in the middle terminates one connection
            // with Alice and opens another with Bob, so Alice compares against the attacker's
            // certificate and Bob against a different one. The two legs must not agree, or the
            // people comparing codes would see a match.

            // Arrange
            var alice = Bytes(10);
            var bob = Bytes(20);
            var attacker = Bytes(30);

            var nonce = Bytes(40, length: 16);

            // Act
            var aliceLeg = AuthenticationCodeRules.FromBindingAndNonces(
                AuthenticationCodeRules.Unordered(alice, attacker), nonce, nonce);
            var bobLeg = AuthenticationCodeRules.FromBindingAndNonces(
                AuthenticationCodeRules.Unordered(bob, attacker), nonce, nonce);

            // Assert
            Assert.NotEqual(aliceLeg, bobLeg);
        }

        [Theory]
        [InlineData(true, TestDisplayName = "the local nonce changes")]
        [InlineData(false, TestDisplayName = "the remote nonce changes")]
        public void EitherNonce_ChangesTheCode(bool changeLocal)
        {
            // Every reconnect over one live MPC link keeps the binding; only the nonces differ, so
            // each one must reach the code on its own.

            // Arrange
            var binding = AuthenticationCodeRules.Unordered(Bytes(1), Bytes(2));
            var first = Bytes(7, length: 16);
            var second = Bytes(8, length: 16);
            var changed = Bytes(9, length: 16);

            // Act
            var before = AuthenticationCodeRules.FromBindingAndNonces(binding, first, second);
            var after = changeLocal
                ? AuthenticationCodeRules.FromBindingAndNonces(binding, changed, second)
                : AuthenticationCodeRules.FromBindingAndNonces(binding, first, changed);

            // Assert
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void OneFlippedBit_ChangesTheCode()
        {
            // Truncating a hash keeps its avalanche only if the truncation is of the hash and not
            // of the input. Pinned so nobody "simplifies" this into reading the token's own bytes.

            // Arrange
            var token = Bytes(9);
            var flipped = Bytes(9);
            flipped[^1] ^= 0x01;

            // Act & Assert
            Assert.NotEqual(
                AuthenticationCodeRules.FromToken(token),
                AuthenticationCodeRules.FromToken(flipped));
        }

        [Fact]
        public void CodesSpreadAcrossTheRange()
        {
            // A derivation that read the wrong end of the hash, or masked instead of taking a
            // remainder, could still pass every test above while collapsing onto a few values.

            // Arrange
            var codes = new HashSet<string>(StringComparer.Ordinal);

            // Act
            for (var i = 0; i < 500; i++)
            {
                codes.Add(AuthenticationCodeRules.FromToken(SHA256.HashData(BitConverter.GetBytes(i))));
            }

            // Assert
            Assert.True(codes.Count > 490, $"500 distinct tokens produced only {codes.Count} distinct codes.");
        }
    }

    public sealed class Stability : AuthenticationCodeRulesTests
    {
        [Fact]
        public void KnownToken_ProducesAKnownCode()
        {
            // Not a compatibility guarantee — the derivation may change in any release, because
            // both ends of a connection run the same build. This pins it against an accidental
            // change, so that altering it stays a deliberate act with a CHANGELOG line.

            // Arrange
            var token = new byte[32];
            for (var i = 0; i < token.Length; i++)
            {
                token[i] = (byte)i;
            }

            // Act
            var code = AuthenticationCodeRules.FromToken(token);

            // Assert
            // SHA-256 of 0x00..0x1f begins 0x63 0x0d 0xcd 0x29; 0x630dcd29 mod 1e6 = 848873.
            Assert.Equal("848873", code);
        }
    }
}
