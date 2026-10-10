namespace Plugin.Maui.NearbyConnectivity;

// The in-band control frame: [ signature(4) | version(1) | type(1) | body ], little-endian.
static class ControlMessage
{
    const uint Signature = 0x504D4E43; // "PMNC" (Plugin.Maui.NearbyConnectivity)
    internal const byte ProtocolVersion = 1;

    static class Offset
    {
        internal const int Version = sizeof(uint);
        internal const int Type = Version + sizeof(byte);
        internal const int Body = Type + sizeof(byte);

        // StreamName body: [ payloadId(8) | nameByteCount(2) | name-utf8 ]
        internal const int StreamPayloadId = Body;
        internal const int StreamNameCount = StreamPayloadId + sizeof(long);
        internal const int StreamNameText = StreamNameCount + sizeof(ushort);

        // Presence body: [ nameByteCount(1) | name-utf8 | info (rest) ]
        internal const int AdvertisementNameCount = Body;
        internal const int AdvertisementNameText = AdvertisementNameCount + sizeof(byte);

        // ConnectRequest body: [ offerWindowMs(4) | presence body ]
        internal const int OfferWindow = Body;
        internal const int ConnectNameCount = OfferWindow + sizeof(uint);
        internal const int ConnectNameText = ConnectNameCount + sizeof(byte);
    }

    // CodeCommit body: SHA-256 of the nonce. CodeReveal body: the nonce.
    internal const int CodeNonceBytes = 16;
    internal const int CodeCommitmentBytes = 32;

    internal const int MaxStreamNameBytes = 1024;
    internal const int EndpointInfoBudget = 131;
    internal const int MaxInfoBytes = EndpointInfoBudget - Offset.ConnectNameText - NearbyText.MaxDisplayNameBytes;

    static byte[] NewFrame(ControlMessageType type, int bodySize)
    {
        var frame = new byte[Offset.Body + bodySize];

        BinaryPrimitives.WriteUInt32LittleEndian(frame, Signature);
        frame[Offset.Version] = ProtocolVersion;
        frame[Offset.Type] = (byte)type;

        return frame;
    }

    internal static bool HasSignature(ReadOnlySpan<byte> data)
        => data.Length >= sizeof(uint)
            && BinaryPrimitives.ReadUInt32LittleEndian(data) == Signature;

    static bool TryReadHeader(ReadOnlySpan<byte> data, out ControlMessageType type)
    {
        if (data.Length < Offset.Body
            || !HasSignature(data)
            || data[Offset.Version] != ProtocolVersion)
        {
            type = default;
            return false;
        }

        type = (ControlMessageType)data[Offset.Type];

        return true;
    }

    internal static void ThrowIfStreamNameTooLong(string name, string parameterName)
        => ThrowIfTooLong(
            Encoding.UTF8.GetBytes(name),
            MaxStreamNameBytes,
            nameof(ControlMessageType.StreamName),
            parameterName);

    static void ThrowIfTooLong(
        ReadOnlySpan<byte> utf8,
        int maxBytes,
        string what,
        string parameterName)
    {
        if (utf8.Length > maxBytes)
        {
            throw new ArgumentException(
                $"The {what} occupies {utf8.Length} UTF-8 bytes; the limit is {maxBytes}.",
                parameterName);
        }
    }

    internal static byte[] Encode(ControlMessageType type) => NewFrame(type, bodySize: 0);

    internal static byte[] EncodeCodeCommit(ReadOnlySpan<byte> commitment)
        => EncodeFixed(ControlMessageType.CodeCommit, commitment, CodeCommitmentBytes);

    internal static byte[] EncodeCodeReveal(ReadOnlySpan<byte> nonce)
        => EncodeFixed(ControlMessageType.CodeReveal, nonce, CodeNonceBytes);

    static byte[] EncodeFixed(ControlMessageType type, ReadOnlySpan<byte> body, int length)
    {
        if (body.Length != length)
        {
            throw new ArgumentException($"A {type} body is {length} bytes, not {body.Length}.", nameof(body));
        }

        var frame = NewFrame(type, bodySize: length);
        body.CopyTo(frame.AsSpan(Offset.Body));

        return frame;
    }

    // Valid only for a frame TryDecode accepted, which is what pins the body's length.
    internal static ReadOnlySpan<byte> Body(ReadOnlySpan<byte> frame) => frame[Offset.Body..];

    internal static byte[] EncodeStreamName(long payloadId, string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);

        ThrowIfTooLong(
            nameBytes,
            MaxStreamNameBytes,
            nameof(ControlMessageType.StreamName),
            nameof(name));

        var frame = NewFrame(
            ControlMessageType.StreamName,
            bodySize: sizeof(long) + sizeof(ushort) + nameBytes.Length);

        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(Offset.StreamPayloadId), payloadId);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(Offset.StreamNameCount), (ushort)nameBytes.Length);
        nameBytes.CopyTo(frame.AsSpan(Offset.StreamNameText));

        return frame;
    }

    internal static byte[] EncodeAdvertisement(NearbyPresence presence)
    {
        var nameBytes = Encoding.UTF8.GetBytes(presence.DisplayName);
        var frame = NewFrame(
            ControlMessageType.Advertisement,
            bodySize: sizeof(byte) + nameBytes.Length + presence.Info.Length);

        WritePresence(frame.AsSpan(Offset.AdvertisementNameCount), nameBytes, presence.Info.Span);

        return frame;
    }

    internal static byte[] EncodeConnectRequest(TimeSpan offerWindow, NearbyPresence presence)
    {
        var nameBytes = Encoding.UTF8.GetBytes(presence.DisplayName);
        var frame = NewFrame(
            ControlMessageType.ConnectRequest,
            bodySize: sizeof(uint) + sizeof(byte) + nameBytes.Length + presence.Info.Length);

        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(Offset.OfferWindow), ToWireWindow(offerWindow));
        WritePresence(frame.AsSpan(Offset.ConnectNameCount), nameBytes, presence.Info.Span);

        return frame;
    }

    // The body both frames carry: the name's byte count, the name, then the info to the end of the
    // frame. The caps are the record's invariants, so nothing here can overflow the count byte.
    static void WritePresence(Span<byte> body, ReadOnlySpan<byte> nameBytes, ReadOnlySpan<byte> info)
    {
        body[0] = (byte)nameBytes.Length;
        nameBytes.CopyTo(body[sizeof(byte)..]);
        info.CopyTo(body[(sizeof(byte) + nameBytes.Length)..]);
    }

    static uint ToWireWindow(TimeSpan offerWindow)
        => (uint)OfferWindowPolicy.Clamp(offerWindow).TotalMilliseconds;

    internal static bool TryDecode(ReadOnlySpan<byte> data, out ControlMessageType type)
    {
        if (!TryReadHeader(data, out type))
        {
            return false;
        }

        return type switch
        {
            ControlMessageType.Disconnect => data.Length == Offset.Body,
            ControlMessageType.StreamName => HasValidStreamNameLength(data),
            ControlMessageType.ConnectRequest => data.Length >= Offset.ConnectNameText,
            ControlMessageType.Advertisement => data.Length >= Offset.AdvertisementNameText,
            ControlMessageType.CodeCommit => data.Length == Offset.Body + CodeCommitmentBytes,
            ControlMessageType.CodeReveal => data.Length == Offset.Body + CodeNonceBytes,
            _ => data.Length == Offset.Body,
        };
    }

    static bool HasValidStreamNameLength(ReadOnlySpan<byte> data)
        => TryReadStreamNameCount(data, out var nameByteCount)
            && data.Length == Offset.StreamNameText + nameByteCount;

    static bool TryReadStreamNameCount(ReadOnlySpan<byte> data, out int nameByteCount)
    {
        nameByteCount = 0;

        if (data.Length < Offset.StreamNameText
            || !TryReadHeader(data, out _))
        {
            return false;
        }

        nameByteCount = BinaryPrimitives.ReadUInt16LittleEndian(data[Offset.StreamNameCount..]);

        return nameByteCount <= MaxStreamNameBytes;
    }

    internal static bool TryDecodeAdvertisement(
        ReadOnlySpan<byte> data,
        [NotNullWhen(true)] out string? displayName,
        out ReadOnlyMemory<byte> info)
    {
        displayName = null;
        info = default;

        return TryReadHeader(data, out var type)
            && type == ControlMessageType.Advertisement
            && TryReadPresence(data[Offset.AdvertisementNameCount..], out displayName, out info);
    }

    internal static bool TryDecodeConnectRequest(
        ReadOnlySpan<byte> data,
        out TimeSpan offerWindow,
        [NotNullWhen(true)] out string? displayName,
        out ReadOnlyMemory<byte> info)
    {
        offerWindow = default;
        displayName = null;
        info = default;

        if (data.Length < Offset.ConnectNameText
            || !TryReadHeader(data, out var type)
            || type != ControlMessageType.ConnectRequest
            || !TryReadPresence(data[Offset.ConnectNameCount..], out displayName, out info))
        {
            return false;
        }

        var windowMs = BinaryPrimitives.ReadUInt32LittleEndian(data[Offset.OfferWindow..]);

        offerWindow = OfferWindowPolicy.Clamp(TimeSpan.FromMilliseconds(windowMs));

        return true;
    }

    // The info is copied rather than sliced: the span belongs to the platform SDK's buffer, which
    // is valid only for the callback that handed it over.
    static bool TryReadPresence(
        ReadOnlySpan<byte> body,
        [NotNullWhen(true)] out string? displayName,
        out ReadOnlyMemory<byte> info)
    {
        displayName = null;
        info = default;

        if (body.IsEmpty)
        {
            return false;
        }

        int nameByteCount = body[0];
        var rest = body[sizeof(byte)..];

        if (nameByteCount > NearbyText.MaxDisplayNameBytes
            || nameByteCount > rest.Length
            || rest.Length - nameByteCount > MaxInfoBytes)
        {
            return false;
        }

        displayName = NearbyText.Truncate(Encoding.UTF8.GetString(rest[..nameByteCount]));
        info = rest[nameByteCount..].ToArray();

        return true;
    }

    internal static bool TryDecodeStreamName(ReadOnlySpan<byte> data, out long payloadId, out string? name)
    {
        payloadId = 0;
        name = null;

        if (!HasValidStreamNameLength(data))
        {
            return false;
        }

        payloadId = BinaryPrimitives.ReadInt64LittleEndian(data[Offset.StreamPayloadId..]);

        name = NearbyText.Truncate(
            Encoding.UTF8.GetString(data[Offset.StreamNameText..]),
            MaxStreamNameBytes);

        return true;
    }
}
