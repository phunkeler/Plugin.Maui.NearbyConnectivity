namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// What a device shows to every other device in range before any connection exists: a display name
/// and up to <see cref="MaxInfoBytes"/> bytes of application-defined information.
/// </summary>
/// <remarks>
/// <para>
/// <b>Public.</b> Both members are broadcast in the clear to every device in range that knows the
/// <see cref="NearbyOptions.ServiceId"/>, before encryption or consent apply. Never put a secret, a
/// token, or personal data in either one.
/// </para>
/// <para>
/// <b>Unauthenticated.</b> Anyone can claim anything. A remote presence is a label to filter and
/// display by, never a credential. See <c>docs/security.md</c> for the membership-tag recipe and
/// its limits.
/// </para>
/// <para>
/// <b>As sent.</b> The plugin bounds the length of both members and rewrites neither, so a remote
/// <see cref="DisplayName"/> may contain bidi controls, separators, or nothing at all, and a remote
/// <see cref="Info"/> is untrusted input to size-check and validate before parsing.
/// </para>
/// </remarks>
/// <seealso href="https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/presence.html">Show a presence before connecting</seealso>
/// <example>
/// <code language="csharp">
/// // Outbound: application-defined bytes, at most MaxInfoBytes.
/// options.Presence = new() { Info = "table:T3"u8.ToArray() };
///
/// // Inbound: the same bytes, unparsed and unverified.
/// var table = Encoding.UTF8.GetString(device.Presence.Info.Span);
/// </code>
/// </example>
public sealed record NearbyPresence
{
    /// <summary>
    /// The UTF-8 byte cap on <see cref="DisplayName"/>, local or remote.
    /// </summary>
    /// <remarks>
    /// What the plugin's connect request frame reserves for a name. The same cap applies on every
    /// platform, so a name that advertises on one advertises on all.
    /// </remarks>
    public const int MaxDisplayNameBytes = NearbyText.MaxDisplayNameBytes;

    /// <summary>
    /// The cap on <see cref="Info"/>, in bytes.
    /// </summary>
    /// <remarks>
    /// What is left of the plugin's connect request frame once its header, the offer window, and a
    /// maximum-length name are accounted for. Stated as a constant so an application that
    /// serializes a schema into <see cref="Info"/> can assert against it.
    /// </remarks>
    public const int MaxInfoBytes = ControlMessage.MaxInfoBytes;

    readonly string _displayName = NearbyOptions.DefaultDisplayName;
    readonly ReadOnlyMemory<byte> _info;

    /// <summary>
    /// Initializes a new presence with this device's own name and no info.
    /// </summary>
    public NearbyPresence()
    {
    }

    NearbyPresence(string displayName, ReadOnlyMemory<byte> info)
    {
        _displayName = displayName;
        _info = info;
    }

    /// <summary>
    /// Gets the name nearby devices see for this device.
    /// </summary>
    /// <value>
    /// 1 to <see cref="MaxDisplayNameBytes"/> UTF-8 bytes. The default is this device's own name,
    /// cut to the cap.
    /// </value>
    /// <exception cref="ArgumentException">
    /// The value is <see langword="null"/>, empty, or over <see cref="MaxDisplayNameBytes"/> UTF-8
    /// bytes. Never silently shortened: a name you cannot see the library change is one you would
    /// have to discover from someone else's screen.
    /// </exception>
    public string DisplayName
    {
        get => _displayName;
        init
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException(
                    $"{nameof(DisplayName)} must not be null or empty. A device advertises under "
                    + "this name, and some platforms reject an empty one outright.",
                    nameof(value));
            }

            var bytes = Encoding.UTF8.GetByteCount(value);

            if (bytes > MaxDisplayNameBytes)
            {
                throw new ArgumentException(
                    $"{nameof(DisplayName)} is {bytes} UTF-8 bytes; the limit is "
                    + $"{MaxDisplayNameBytes} on every platform, which is what the connect request "
                    + "frame reserves for a name. Note the limit counts bytes rather than "
                    + "characters, so a name outside the ASCII range reaches it sooner than its "
                    + "length suggests.",
                    nameof(value));
            }

            _displayName = value;
        }
    }

    /// <summary>
    /// Gets the application-defined information nearby devices see for this device.
    /// </summary>
    /// <value>
    /// At most <see cref="MaxInfoBytes"/> bytes, opaque to the plugin. The default is empty.
    /// </value>
    /// <remarks>
    /// The bytes are copied on assignment, so an array mutated afterwards cannot change what is
    /// advertised. The application owns the encoding: <c>"…"u8.ToArray()</c> in and
    /// <c>Encoding.UTF8.GetString(span)</c> out for text, or any serializer that fits the budget.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The value is longer than <see cref="MaxInfoBytes"/> bytes.
    /// </exception>
    public ReadOnlyMemory<byte> Info
    {
        get => _info;
        init
        {
            if (value.Length > MaxInfoBytes)
            {
                throw new ArgumentException(
                    $"{nameof(Info)} is {value.Length} bytes; the limit is {MaxInfoBytes} on every "
                    + "platform, which is what is left of the connect request frame once its header, "
                    + "the offer window, and a maximum-length display name are accounted for.",
                    nameof(value));
            }

            _info = value.ToArray();
        }
    }

    /// <summary>
    /// A presence exactly as a remote device sent it, bypassing the local invariants.
    /// </summary>
    /// <remarks>
    /// A remote name may be empty and a remote info may be anything the decoders let through, both
    /// of which the init accessors reject for a locally configured value. Rejecting them here would
    /// mean throwing inside a platform callback over a peer's choice, so what arrived is kept and
    /// the caps are enforced where the frame is decoded instead. The values are not copied: the
    /// decoders already hand over fresh arrays.
    /// </remarks>
    internal static NearbyPresence FromRemote(string? displayName, ReadOnlyMemory<byte> info)
        => new(displayName ?? string.Empty, info);

    /// <summary>
    /// Determines whether the specified presence has the same name and the same info bytes.
    /// </summary>
    /// <param name="other">The presence to compare with this one.</param>
    /// <returns>
    /// <see langword="true"/> when the names are ordinally equal and the info bytes are the same
    /// sequence.
    /// </returns>
    /// <remarks>
    /// Written out because the generated version would compare <see cref="Info"/> by reference, so
    /// two presences built from equal but distinct arrays would not be equal.
    /// </remarks>
    public bool Equals(NearbyPresence? other)
        => other is not null
            && string.Equals(_displayName, other._displayName, StringComparison.Ordinal)
            && _info.Span.SequenceEqual(other._info.Span);

    /// <summary>
    /// Returns the hash code for this presence.
    /// </summary>
    /// <returns>A hash code derived from the name and the info bytes.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(_displayName, StringComparer.Ordinal);
        hash.AddBytes(_info.Span);

        return hash.ToHashCode();
    }
}
