using System.Buffers.Binary;
namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Connections")]
public class ControlMessageTests
{
    /// <summary>A presence whose content is beside the point, for the window-only assertions.</summary>
    static NearbyPresence Presence => new() { DisplayName = "Alice" };

    /// <summary>
    /// The signature bytes, little-endian, spelling "PMNC" when read as ASCII.
    /// </summary>
    static byte[] SignatureBytes => [0x43, 0x4E, 0x4D, 0x50];

    public sealed class Encode : ControlMessageTests
    {
        [Fact]
        public void Disconnect_ProducesTheExactExpectedBytes()
        {
            // Arrange
            const byte ExpectedDisconnectByte = 0x01;

            // Act
            var encoded = ControlMessage.Encode(ControlMessageType.Disconnect);

            // Assert
            // The frame is a 4-byte signature, a 1-byte version, and a 1-byte type.
            Assert.Equal(6, encoded.Length);
            // Signature bytes changed — every peer on the previous version will stop recognising control messages.
            Assert.Equal(SignatureBytes, encoded[..4]);
            // The declared version changed: bump ControlMessage.ProtocolVersion deliberately, never by accident.
            Assert.Equal(ControlMessage.ProtocolVersion, encoded[4]);
            // Disconnect's wire value changed.
            Assert.Equal(ExpectedDisconnectByte, encoded[5]);
        }

        [Fact]
        public void SignatureOnTheWire_IsPmncReversed()
        {
            // Act
            var encoded = ControlMessage.Encode(ControlMessageType.Disconnect);

            // Assert
            Assert.Equal("CNMP", System.Text.Encoding.ASCII.GetString(encoded[..4]));
        }

        [Fact]
        public void VersionByte_SitsBetweenTheSignatureAndTheType()
        {
            // The version has to be readable before the type, because the type's body layout is
            // what the version describes. Moving it later would mean parsing a body to find out
            // whether this build can parse that body.

            // Act
            var encoded = ControlMessage.Encode(ControlMessageType.Disconnect);

            // Assert
            Assert.Equal(ControlMessage.ProtocolVersion, encoded[4]);
            Assert.Equal((byte)ControlMessageType.Disconnect, encoded[5]);
        }

        [Fact]
        public void EveryFrameKind_DeclaresTheSameVersion()
        {
            // Act
            var disconnect = ControlMessage.Encode(ControlMessageType.Disconnect);
            var streamName = ControlMessage.EncodeStreamName(payloadId: 1, "vitals");
            var connectRequest = ControlMessage.EncodeConnectRequest(TimeSpan.FromSeconds(30), Presence);
            var advertisement = ControlMessage.EncodeAdvertisement(Presence);

            // Assert
            Assert.Equal(ControlMessage.ProtocolVersion, disconnect[4]);
            Assert.Equal(ControlMessage.ProtocolVersion, streamName[4]);
            Assert.Equal(ControlMessage.ProtocolVersion, connectRequest[4]);
            Assert.Equal(ControlMessage.ProtocolVersion, advertisement[4]);
        }
    }

    /// <summary>
    /// The version byte's whole job: a frame from a build that lays its bodies out differently is
    /// refused before its body is read, so the receiver takes the same fallback it takes for a peer
    /// that sent no frame. These pin the rejection, not a migration — accepting an older version
    /// is a deliberate decision made when one is introduced.
    /// </summary>
    public sealed class Versioning : ControlMessageTests
    {
        [Theory]
        [InlineData(0, TestDisplayName = "a version before this format existed")]
        [InlineData(2, TestDisplayName = "the next version")]
        [InlineData(0xFF, TestDisplayName = "a far-future version")]
        public void AForeignVersion_IsRejected(byte version)
        {
            // Arrange
            var frame = ControlMessage.Encode(ControlMessageType.Disconnect);
            frame[4] = version;

            // Act
            var decoded = ControlMessage.TryDecode(frame, out var type);

            // Assert
            Assert.False(decoded, $"A frame declaring version {version} was accepted.");
            Assert.Equal(default, type);
        }

        [Fact]
        public void AForeignVersion_IsRejected_ForAConnectRequest()
        {
            // The case that matters most in a staged rollout: the offer window and the display
            // name are read by offset, so a body laid out differently would be read as nonsense.

            // Arrange
            var frame = ControlMessage.EncodeConnectRequest(TimeSpan.FromSeconds(30), new() { DisplayName = "Alice" });
            frame[4] = ControlMessage.ProtocolVersion + 1;

            // Act
            var decoded = ControlMessage.TryDecodeConnectRequest(frame, out var window, out var name, out _);

            // Assert
            Assert.False(decoded);
            Assert.Null(name);
            Assert.Equal(default, window);
        }

        [Fact]
        public void AForeignVersion_IsRejected_ForAStreamNameFrame()
        {
            // Arrange
            var frame = ControlMessage.EncodeStreamName(payloadId: 42, "vitals.live");
            frame[4] = ControlMessage.ProtocolVersion + 1;

            // Act
            var recognized = ControlMessage.TryDecode(frame, out _);
            var decoded = ControlMessage.TryDecodeStreamName(frame, out var payloadId, out var name);

            // Assert
            Assert.False(recognized);
            Assert.False(decoded);
            Assert.Null(name);
            Assert.Equal(0, payloadId);
        }

        [Fact]
        public void APreVersionFrame_IsRejected_RatherThanReadAsThisFormat()
        {
            // A frame from a build that shipped before the version byte: signature, then the type
            // byte where the version now sits. Disconnect's 0x01 collides with version 1, so this
            // pins the type that does not collide — the receiver must not read a ConnectRequest's
            // first body byte as its type.

            // Arrange
            byte[] preVersion = [.. SignatureBytes, (byte)ControlMessageType.ConnectRequest, 0x00, 0x00, 0x00, 0x00];

            // Act
            var decoded = ControlMessage.TryDecode(preVersion, out var type);

            // Assert
            Assert.False(decoded);
            Assert.Equal(default, type);
        }
    }

    public sealed class TryDecode : ControlMessageTests
    {
        [Fact]
        public void EncodedMessage_RoundTrips()
        {
            // Arrange
            var encoded = ControlMessage.Encode(ControlMessageType.Disconnect);

            // Act
            var decoded = ControlMessage.TryDecode(encoded, out var type);

            // Assert
            Assert.True(decoded);
            Assert.Equal(ControlMessageType.Disconnect, type);
        }

        [Theory]
        [InlineData(0, TestDisplayName = "empty")]
        [InlineData(4, TestDisplayName = "signature only, type byte missing")]
        [InlineData(6, TestDisplayName = "one byte too long")]
        [InlineData(64, TestDisplayName = "an ordinary small payload")]
        public void WrongLength_IsRejected(int length)
        {
            // Arrange
            var buffer = new byte[length];
            SignatureBytes.AsSpan()[..Math.Min(4, length)].CopyTo(buffer);

            // Act
            var decoded = ControlMessage.TryDecode(buffer, out _);

            // Assert
            Assert.False(decoded, $"A {length}-byte buffer was accepted as a control message.");
        }

        [Fact]
        public void WrongSignature_IsRejected()
        {
            // Arrange
            byte[] userPayload = [0x00, 0x01, 0x02, 0x03, 0x01];

            // Act
            var decoded = ControlMessage.TryDecode(userPayload, out _);

            // Assert
            Assert.False(decoded);
        }

        [Fact]
        public void SignatureInWrongByteOrder_IsRejected()
        {
            // Arrange
            byte[] bigEndian = [0x50, 0x4D, 0x4E, 0x43, 0x01];

            // Act
            var decoded = ControlMessage.TryDecode(bigEndian, out _);

            // Assert
            Assert.False(decoded);
        }

        [Fact]
        public void UndefinedType_DecodesSuccessfully_ForForwardCompatibility()
        {
            // Arrange
            byte[] futureType = [.. SignatureBytes, ControlMessage.ProtocolVersion, 0xFE];

            // Act
            var decoded = ControlMessage.TryDecode(futureType, out var type);

            // Assert
            Assert.True(decoded);
            Assert.Equal((ControlMessageType)0xFE, type);
            Assert.False(Enum.IsDefined(type), "0xFE should not be a defined type in this build.");
        }

        [Fact]
        public void RejectedMessage_LeavesTypeAtDefault()
        {
            // Arrange
            byte[] notAControlMessage = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

            // Act
            ControlMessage.TryDecode(notAControlMessage, out var type);

            // Assert
            Assert.Equal(default, type);
        }
    }

    public sealed class StreamNameFrame : ControlMessageTests
    {
        [Fact]
        public void RoundTrips_PayloadIdAndName()
        {
            // Arrange
            var frame = ControlMessage.EncodeStreamName(payloadId: 42, "vitals.live");

            // Act
            var recognized = ControlMessage.TryDecode(frame, out var type);
            var decoded = ControlMessage.TryDecodeStreamName(frame, out var payloadId, out var name);

            // Assert
            Assert.True(recognized);
            Assert.Equal(ControlMessageType.StreamName, type);
            Assert.True(decoded);
            Assert.Equal(42, payloadId);
            Assert.Equal("vitals.live", name);
        }

        [Fact]
        public void RoundTrips_NonAsciiName()
        {
            // Arrange
            var frame = ControlMessage.EncodeStreamName(payloadId: 7, "café-Δ");

            // Act
            ControlMessage.TryDecodeStreamName(frame, out _, out var name);

            // Assert
            Assert.Equal("café-Δ", name);
        }

        [Fact]
        public void Decode_ReturnsAPeerSuppliedNameAsSent()
        {
            // Arrange
            // Nothing is stripped any more: a stream is identified by the name the
            // sender chose, and rendering it safely is the application's job.
            var streamName = "live\u202Evitals";
            var frame = ControlMessage.EncodeStreamName(payloadId: 3, streamName);

            // Act
            ControlMessage.TryDecodeStreamName(frame, out _, out var name);

            // Assert
            Assert.Equal(streamName, name);
        }

        [Fact]
        public void Encode_RejectsANameOverTheWireLimit()
        {
            // Arrange
            var oversized = new string('x', ControlMessage.MaxStreamNameBytes + 1);

            // Act
            void Act() => ControlMessage.EncodeStreamName(payloadId: 1, oversized);

            // Assert
            Assert.Throws<ArgumentException>(Act);
        }

        [Fact]
        public void TryDecodeStreamName_FailsSoftOnATruncatedBody()
        {
            // Arrange
            var frame = ControlMessage.EncodeStreamName(payloadId: 42, "vitals.live");
            var truncated = frame.AsSpan(0, frame.Length - 3);

            // Act
            var decoded = ControlMessage.TryDecodeStreamName(truncated, out _, out var name);

            // Assert
            Assert.False(decoded);
            Assert.Null(name);
        }

        [Fact]
        public void DisconnectFrame_KeepsItsSixByteLayout()
        {
            // The header is the wire contract: a Disconnect frame is header-only, and its layout
            // must not grow without the version byte moving with it.

            // Arrange
            var frame = ControlMessage.Encode(ControlMessageType.Disconnect);

            // Act
            var recognized = ControlMessage.TryDecode(frame, out var type);

            // Assert
            Assert.Equal(6, frame.Length);
            Assert.True(recognized);
            Assert.Equal(ControlMessageType.Disconnect, type);
        }
    }

    public sealed class AdvertisementFrame : ControlMessageTests
    {
        [Fact]
        public void RoundTrips_NameAndInfo()
        {
            // Arrange
            var info = "route:R17"u8.ToArray();
            var frame = ControlMessage.EncodeAdvertisement(
                new() { DisplayName = "Alice", Info = info });

            // Act
            var recognized = ControlMessage.TryDecode(frame, out var type);
            var decoded = ControlMessage.TryDecodeAdvertisement(frame, out var name, out var decodedInfo);

            // Assert
            Assert.True(recognized);
            Assert.Equal(ControlMessageType.Advertisement, type);
            Assert.True(decoded);
            Assert.Equal("Alice", name);
            Assert.Equal(info, decodedInfo.ToArray());
        }

        [Fact]
        public void RoundTrips_WithoutInfo()
        {
            // Arrange
            var frame = ControlMessage.EncodeAdvertisement(new() { DisplayName = "Alice" });

            // Act
            var decoded = ControlMessage.TryDecodeAdvertisement(frame, out var name, out var info);

            // Assert
            Assert.True(decoded);
            Assert.Equal("Alice", name);
            Assert.True(info.IsEmpty);
        }

        [Fact]
        public void RoundTrips_InfoThatIsNotValidUtf8()
        {
            // Arrange
            // The bytes are opaque, so no decoder ever touches them.
            var info = new byte[] { 0x00, 0xFF, 0xC0 };
            var frame = ControlMessage.EncodeAdvertisement(
                new() { DisplayName = "Alice", Info = info });

            // Act
            ControlMessage.TryDecodeAdvertisement(frame, out _, out var decodedInfo);

            // Assert
            Assert.Equal(info, decodedInfo.ToArray());
        }

        [Fact]
        public void AMaxSizedFrame_IsUnderTheEndpointInfoBudget()
        {
            // Arrange
            // It carries no offer window, so it has four bytes more headroom than the
            // connect request that sets the cap.
            var frame = ControlMessage.EncodeAdvertisement(new()
            {
                DisplayName = new string('x', NearbyPresence.MaxDisplayNameBytes),
                Info = new byte[NearbyPresence.MaxInfoBytes],
            });

            // Assert
            Assert.True(frame.Length < ControlMessage.EndpointInfoBudget);
        }

        [Fact]
        public void ANameCountPastTheEndOfTheFrame_IsRejected()
        {
            // Arrange
            var frame = ControlMessage.EncodeAdvertisement(new() { DisplayName = "Alice" });
            frame[6] = 63;

            // Act
            var decoded = ControlMessage.TryDecodeAdvertisement(frame, out var name, out _);

            // Assert
            Assert.False(decoded);
            Assert.Null(name);
        }

        [Fact]
        public void ANameCountOverTheCap_IsRejected()
        {
            // Arrange
            var frame = ControlMessage.EncodeAdvertisement(new() { DisplayName = "Alice" });
            frame[6] = NearbyPresence.MaxDisplayNameBytes + 1;

            // Act & Assert
            Assert.False(ControlMessage.TryDecodeAdvertisement(frame, out _, out _));
        }

        [Fact]
        public void APlainNameIsNotMistakenForAFrame()
        {
            // Arrange
            // What a peer on 1.0.0-preview.1 advertises.
            var legacy = System.Text.Encoding.UTF8.GetBytes("Alice's Phone");

            // Act & Assert
            Assert.False(ControlMessage.TryDecodeAdvertisement(legacy, out _, out _));
        }
    }

    public sealed class ConnectRequestFrame : ControlMessageTests
    {
        [Fact]
        public void RoundTrips_WindowAndName()
        {
            // Arrange
            // The Android shape: the frame re-carries the display name.
            var frame = ControlMessage.EncodeConnectRequest(TimeSpan.FromSeconds(30), new() { DisplayName = "Alice" });

            // Act
            var recognized = ControlMessage.TryDecode(frame, out var type);
            var decoded = ControlMessage.TryDecodeConnectRequest(frame, out var window, out var name, out _);

            // Assert
            Assert.True(recognized);
            Assert.Equal(ControlMessageType.ConnectRequest, type);
            Assert.True(decoded);
            Assert.Equal(TimeSpan.FromSeconds(30), window);
            Assert.Equal("Alice", name);
        }

        [Fact]
        public void RoundTrips_NameAndInfo()
        {
            // Arrange
            // Info the plugin never interprets, including bytes that are not valid UTF-8.
            var info = new byte[] { 0x00, 0xFF, 0xC0, 0x7B };
            var frame = ControlMessage.EncodeConnectRequest(
                TimeSpan.FromSeconds(10), new() { DisplayName = "Alice", Info = info });

            // Act
            var decoded = ControlMessage.TryDecodeConnectRequest(
                frame, out var window, out var name, out var decodedInfo);

            // Assert
            Assert.True(decoded);
            Assert.Equal(TimeSpan.FromSeconds(10), window);
            Assert.Equal("Alice", name);
            Assert.Equal(info, decodedInfo.ToArray());
        }

        [Fact]
        public void AMaxSizedFrame_FitsTheEndpointInfoBudget()
        {
            // Arrange
            // The binding constraint: Android carries at most 131 bytes pre-connection.
            var presence = new NearbyPresence
            {
                DisplayName = new string('x', NearbyPresence.MaxDisplayNameBytes),
                Info = new byte[NearbyPresence.MaxInfoBytes],
            };

            // Act
            var frame = ControlMessage.EncodeConnectRequest(TimeSpan.FromSeconds(30), presence);

            // Assert
            Assert.Equal(ControlMessage.EndpointInfoBudget, frame.Length);
        }

        [Fact]
        public void OverLongWindow_IsDeclaredAsTheTrustBound()
        {
            // Arrange
            var frame = ControlMessage.EncodeConnectRequest(TimeSpan.FromDays(365), Presence);

            // Act
            var decoded = ControlMessage.TryDecodeConnectRequest(frame, out var window, out _, out _);

            // Assert
            Assert.True(decoded);
            Assert.Equal(OfferWindowPolicy.s_max, window);
        }

        [Fact]
        public void WindowBeyondTheBound_OnTheWire_IsClampedOnReceipt()
        {
            var frame = ControlMessage.EncodeConnectRequest(TimeSpan.FromSeconds(10), Presence);
            // Past the signature, the version, and the type byte, to the window field.
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4 + 1 + 1), 0xFFFFFFFF);

            // Act
            var decoded = ControlMessage.TryDecodeConnectRequest(frame, out var window, out _, out _);

            // Assert
            Assert.True(decoded);
            Assert.Equal(OfferWindowPolicy.s_max, window);
        }

        [Fact]
        public void ZeroWindow_IsValidAndDecodesToZero()
        {
            // A degenerate declared window is honored — it lands already-expired on the receiver
            // and only hurts the sender.

            // Arrange
            var frame = ControlMessage.EncodeConnectRequest(TimeSpan.Zero, Presence);

            // Act
            var decoded = ControlMessage.TryDecodeConnectRequest(frame, out var window, out _, out _);

            // Assert
            Assert.True(decoded);
            Assert.Equal(TimeSpan.Zero, window);
        }

        [Fact]
        public void Encode_RejectsANameOverTheWireBudget()
        {
            // 9 bytes of frame overhead + a 63-byte name is what keeps the whole frame inside
            // Google's ~131-byte pre-connection cap.

            // Arrange
            var oversized = new string('x', NearbyText.MaxDisplayNameBytes + 1);

            // Act & Assert
            // The presence refuses it before an encoder ever sees it.
            Assert.Throws<ArgumentException>(() => new NearbyPresence { DisplayName = oversized });
        }

        [Fact]
        public void ANameThatIsNotValidUtf8_StaysWithinTheCap()
        {
            // Arrange
            var frame = ControlMessage.EncodeConnectRequest(
                TimeSpan.FromSeconds(30),
                new NearbyPresence { DisplayName = new string('a', NearbyPresence.MaxDisplayNameBytes) });

            frame.AsSpan(^NearbyPresence.MaxDisplayNameBytes..).Fill(0xFF);

            // Act
            var decoded = ControlMessage.TryDecodeConnectRequest(frame, out _, out var name, out _);

            // Assert
            Assert.True(decoded);
            Assert.NotNull(name);
            Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(name), 1, NearbyPresence.MaxDisplayNameBytes);
        }

        [Fact]
        public void TruncatedFrame_IsRejected()
        {
            // Arrange
            // Cut into the window field.
            var frame = ControlMessage.EncodeConnectRequest(TimeSpan.FromSeconds(30), Presence);
            var truncated = frame.AsSpan(0, ControlMessage.EndpointInfoBudget - 131 + 8);

            // Act
            var recognized = ControlMessage.TryDecode(truncated, out _);
            var decoded = ControlMessage.TryDecodeConnectRequest(truncated, out _, out var name, out _);

            // Assert
            Assert.False(recognized);
            Assert.False(decoded);
            Assert.Null(name);
        }

        [Fact]
        public void LegacyRawName_IsNotMistakenForAFrame()
        {
            // A legacy Android peer's endpointInfo is its raw UTF-8 display name. It must fail the
            // decode so the receiver falls back to name-as-string plus the default window.

            // Arrange
            var legacy = System.Text.Encoding.UTF8.GetBytes("Alice's Phone");

            // Act
            var decoded = ControlMessage.TryDecodeConnectRequest(legacy, out _, out var name, out _);

            // Assert
            Assert.False(decoded);
            Assert.Null(name);
        }

        [Fact]
        public void AppPayloadStartingWithTheSignature_IsSwallowedAsAControlFrame()
        {
            // The false-positive case, pinned: an application payload that starts with the
            // signature, this build's version byte, the 0x03 type byte, and at least four more
            // bytes decodes as a control frame and is not delivered as an application payload.
            // The version byte narrows this window without closing it.

            // Arrange
            // The name count must be plausible too, which narrows the window further: 0x01 here
            // claims a one-byte name, and the byte after it supplies one.
            byte[] payload =
                [.. SignatureBytes, ControlMessage.ProtocolVersion, 0x03, 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x41];

            // Act
            var recognized = ControlMessage.TryDecode(payload, out var type);

            // Assert
            Assert.True(recognized);
            Assert.Equal(ControlMessageType.ConnectRequest, type);
        }
    }

    /// <summary>
    /// The two frames that answer an offer. Both adapters now build their inbound request through
    /// one transport helper, so the handshake's answer frames are decoded by shared code and are
    /// pinned here rather than per platform.
    /// </summary>
    public class AnswerFrames
    {
        [Theory]
        [InlineData(ControlMessageType.ConnectAccepted)]
        [InlineData(ControlMessageType.ConnectRejected)]
        internal void RoundTrips(ControlMessageType type)
        {
            // Arrange
            var encoded = ControlMessage.Encode(type);

            // Act
            var recognized = ControlMessage.TryDecode(encoded, out var decoded);

            // Assert
            Assert.True(recognized);
            Assert.Equal(type, decoded);
        }

        [Fact]
        public void AcceptedAndRejected_AreDistinct()
        {
            // A single-byte type field separates "connected" from "declined". Were the two to
            // decode alike, a decline would read as a connection.

            // Act
            ControlMessage.TryDecode(ControlMessage.Encode(ControlMessageType.ConnectAccepted), out var accepted);
            ControlMessage.TryDecode(ControlMessage.Encode(ControlMessageType.ConnectRejected), out var rejected);

            // Assert
            Assert.NotEqual(accepted, rejected);
            Assert.Equal(ControlMessageType.ConnectAccepted, accepted);
            Assert.Equal(ControlMessageType.ConnectRejected, rejected);
        }

        [Theory]
        [InlineData(ControlMessageType.ConnectAccepted)]
        [InlineData(ControlMessageType.ConnectRejected)]
        internal void CarryNoBody_SoTheyDoNotDecodeAsAConnectRequest(ControlMessageType type)
        {
            // Act
            var decodedAsRequest = ControlMessage.TryDecodeConnectRequest(
                ControlMessage.Encode(type), out _, out var name, out _);

            // Assert
            Assert.False(decodedAsRequest);
            Assert.Null(name);
        }
    }

    public sealed class CodeFrames : ControlMessageTests
    {
        [Fact]
        public void Commit_RoundTripsItsBody()
        {
            // Arrange
            var commitment = Enumerable.Range(0, ControlMessage.CodeCommitmentBytes).Select(i => (byte)i).ToArray();

            // Act
            var frame = ControlMessage.EncodeCodeCommit(commitment);
            var decoded = ControlMessage.TryDecode(frame, out var type);

            // Assert
            Assert.True(decoded);
            Assert.Equal(ControlMessageType.CodeCommit, type);
            Assert.Equal(commitment, ControlMessage.Body(frame).ToArray());
        }

        [Fact]
        public void Reveal_RoundTripsItsBody()
        {
            // Arrange
            var nonce = Enumerable.Range(100, ControlMessage.CodeNonceBytes).Select(i => (byte)i).ToArray();

            // Act
            var frame = ControlMessage.EncodeCodeReveal(nonce);
            var decoded = ControlMessage.TryDecode(frame, out var type);

            // Assert
            Assert.True(decoded);
            Assert.Equal(ControlMessageType.CodeReveal, type);
            Assert.Equal(nonce, ControlMessage.Body(frame).ToArray());
        }

        [Theory]
        [InlineData(true, -1, TestDisplayName = "commit one byte short")]
        [InlineData(true, 1, TestDisplayName = "commit one byte long")]
        [InlineData(true, -32, TestDisplayName = "commit with no body")]
        [InlineData(false, -1, TestDisplayName = "reveal one byte short")]
        [InlineData(false, 1, TestDisplayName = "reveal one byte long")]
        [InlineData(false, -16, TestDisplayName = "reveal with no body")]
        public void WrongBodyLength_IsRejected(bool commit, int delta)
        {
            // Arrange
            var frame = commit
                ? ControlMessage.EncodeCodeCommit(new byte[ControlMessage.CodeCommitmentBytes])
                : ControlMessage.EncodeCodeReveal(new byte[ControlMessage.CodeNonceBytes]);
            var resized = new byte[frame.Length + delta];
            frame.AsSpan(0, Math.Min(frame.Length, resized.Length)).CopyTo(resized);

            // Act
            var decoded = ControlMessage.TryDecode(resized, out _);

            // Assert
            Assert.False(decoded);
        }

        [Fact]
        public void Encode_RefusesABodyOfTheWrongLength()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => ControlMessage.EncodeCodeCommit(new byte[ControlMessage.CodeNonceBytes]));
            Assert.Throws<ArgumentException>(() => ControlMessage.EncodeCodeReveal(new byte[ControlMessage.CodeCommitmentBytes]));
        }

        [Fact]
        public void WireValues_ArePinned()
        {
            // Act & Assert
            Assert.Equal(0x07, ControlMessage.EncodeCodeCommit(new byte[ControlMessage.CodeCommitmentBytes])[5]);
            Assert.Equal(0x08, ControlMessage.EncodeCodeReveal(new byte[ControlMessage.CodeNonceBytes])[5]);
        }
    }
}
