namespace Plugin.Maui.NearbyConnectivity.DeviceTests;

partial class Create
{
    /// <summary>The real platform type on Android, wired with the default <see cref="PeerLookup"/>.</summary>
    /// <param name="options">Options to wire the platform with, or <see langword="null"/> for the suite defaults.</param>
    /// <returns>The platform under test.</returns>
    internal NearbyTransport NearbyTransport(NearbyOptions? options = null)
        => new(
            TimeProvider.System,
            options ?? DefaultOptions(),
            Logger,
            // Gateway: devices and requests act through one, so a peer minted without it cannot be
            // answered at all. See PlatformOnlyGateway for why it is this one.
            new PeerLookup { Logger = Logger, Gateway = new PlatformOnlyGateway() },
            static transport => new AndroidAdapter(transport));

    /// <summary>A transfer update built via the SDK's Builder, the only construction path GMS exposes.</summary>
    /// <param name="payloadId">The payload the update refers to.</param>
    /// <param name="status">One of the <see cref="PayloadTransferUpdate.Status"/> values.</param>
    /// <param name="total">Total bytes in the transfer.</param>
    /// <param name="transferred">Bytes transferred so far.</param>
    /// <returns>The transfer update.</returns>
    internal static PayloadTransferUpdate TransferUpdate(
        long payloadId, int status, long total = 3, long transferred = 3)
        => new PayloadTransferUpdate.Builder()
            .SetPayloadId(payloadId)
            .SetStatus(status)
            .SetTotalBytes(total)
            .SetBytesTransferred(transferred)
            .Build();

    /// <summary>
    /// An inbound file payload backed by a real file in the cache directory. The copy path reads it
    /// through <c>ContentResolver</c>, so the file must exist on disk for the copy to succeed.
    /// </summary>
    /// <param name="contents">Bytes to write to the backing file.</param>
    /// <param name="fileName">Name of the backing file, unique per test.</param>
    /// <returns>The payload, owning a descriptor on the backing file.</returns>
    internal static Payload FilePayload(byte[] contents, string fileName)
    {
        var path = Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, fileName);
        File.WriteAllBytes(path, contents);

        var file = new Java.IO.File(path);

        return Payload.FromFile(file) ?? throw new InvalidOperationException("GMS built no file payload.");
    }

    /// <summary>
    /// A connection resolution carrying <paramref name="statusCode"/>. Google marks this ctor
    /// deprecated but still ships it at the pinned binding version, so the suppression is scoped to
    /// this one line: a binding bump that removes the ctor fails the build here.
    /// </summary>
    /// <param name="statusCode">One of the <see cref="ConnectionsStatusCodes"/> values.</param>
    /// <returns>The resolution.</returns>
    internal static ConnectionResolution Resolution(int statusCode = ConnectionsStatusCodes.StatusOk)
#pragma warning disable CS0618 // Deprecated but shipped at the pinned binding; see the summary.
        => new(new Statuses(statusCode));
#pragma warning restore CS0618

    /// <summary>
    /// The incoming-connection info GMS hands to <c>OnConnectionInitiated</c>. Same
    /// deprecated-but-shipped ctor situation as <see cref="Resolution"/>.
    /// </summary>
    /// <param name="displayName">The remote device's display name, as GMS reports it.</param>
    /// <returns>The connection info.</returns>
    internal static ConnectionInfo ConnectionInfo(string displayName = "Alice")
#pragma warning disable CS0618 // Deprecated but shipped at the pinned binding; see the summary.
        => new(displayName, "auth-token", isIncomingConnection: true);
#pragma warning restore CS0618

    /// <summary>
    /// Connection info whose <c>GetEndpointInfo()</c> bytes carry a connect-request frame, as a
    /// new-version initiator sends. The only shipped ctor takes the endpoint name as a string, and
    /// GMS derives the info bytes as that string's UTF-8 encoding — so the frame is smuggled
    /// through as a string whose UTF-8 bytes are the frame.
    /// </summary>
    /// <remarks>
    /// <b>Every byte of the frame must be a valid one-byte UTF-8 code point (0x00–0x7F)</b> or the
    /// string round-trip corrupts it. The signature ("CNMP") and type byte qualify; the caller
    /// must pick a window whose four little-endian millisecond bytes also qualify — e.g. 100 ms
    /// (0x64 00 00 00) or 0x7F7F7F7F ms — and an ASCII name. The helper throws when the frame
    /// would not survive, so a bad choice fails at arrange rather than as a silent
    /// default-window fallback.
    /// </remarks>
    /// <param name="offerWindow">The declared offer window. See the byte-safety rule above.</param>
    /// <param name="displayName">The initiator's display name carried in the frame.</param>
    /// <param name="info">The initiator's presence info carried in the frame. ASCII only.</param>
    /// <returns>The connection info.</returns>
    internal static ConnectionInfo ConnectionInfoWithFrame(
        TimeSpan offerWindow,
        string displayName = "Alice",
        ReadOnlyMemory<byte> info = default)
    {
        var frame = ControlMessage.EncodeConnectRequest(
            offerWindow,
            new() { DisplayName = displayName, Info = info });

        if (frame.Any(static b => b > 0x7F))
        {
            throw new ArgumentException(
                "The frame contains bytes above 0x7F and would not survive the string round-trip. " +
                "Pick a window whose little-endian millisecond bytes are all ≤ 0x7F.",
                nameof(offerWindow));
        }

        var smuggled = System.Text.Encoding.UTF8.GetString(frame);

#pragma warning disable CS0618 // Deprecated but shipped at the pinned binding; see the summary.
        return new(smuggled, "auth-token", isIncomingConnection: true);
#pragma warning restore CS0618
    }

    /// <summary>
    /// The discovery info GMS hands to <c>OnEndpointFound</c>. Same deprecated-but-shipped ctor
    /// situation as <see cref="Resolution"/>.
    /// </summary>
    /// <param name="displayName">The remote device's display name, as GMS reports it.</param>
    /// <returns>The discovered endpoint info.</returns>
    internal static DiscoveredEndpointInfo DiscoveredEndpointInfo(string displayName = "Alice")
#pragma warning disable CS0618 // Deprecated but shipped at the pinned binding; see the summary.
        => new(ServiceId, displayName);
#pragma warning restore CS0618

    /// <summary>
    /// The discovery info a peer that sets presence info advertises: an advertisement frame in
    /// place of the plain name.
    /// </summary>
    /// <remarks>
    /// Subject to the same ASCII rule as <see cref="ConnectionInfoWithFrame"/> — the binding takes
    /// only the string constructor, so every frame byte must be 0x00–0x7F.
    /// </remarks>
    /// <param name="displayName">The remote device's display name, carried in the frame.</param>
    /// <param name="info">The remote device's presence info, carried in the frame. ASCII only.</param>
    /// <returns>The discovered endpoint info.</returns>
    internal static DiscoveredEndpointInfo DiscoveredEndpointInfoWithFrame(
        string displayName = "Alice",
        ReadOnlyMemory<byte> info = default)
    {
        var frame = ControlMessage.EncodeAdvertisement(
            new() { DisplayName = displayName, Info = info });

        if (frame.Any(static b => b > 0x7F))
        {
            throw new ArgumentException(
                "The frame contains bytes above 0x7F and would not survive the string round-trip.",
                nameof(info));
        }

#pragma warning disable CS0618 // Deprecated but shipped at the pinned binding; see the summary.
        return new(ServiceId, System.Text.Encoding.UTF8.GetString(frame));
#pragma warning restore CS0618
    }

    /// <summary>
    /// A handshake pending on the advertise channel, registered the way the platform registers one:
    /// keyed by the device id minted for <paramref name="endpointId"/>, not by the endpoint id.
    /// </summary>
    /// <param name="platform">The platform to register the pending handshake on.</param>
    /// <param name="endpointId">The endpoint id GMS would report the peer under.</param>
    /// <param name="displayName">The remote device's display name, as GMS reports it.</param>
    /// <returns>
    /// The source the platform will resolve or fault, and the device id it is registered under —
    /// the value every assertion should use, since the endpoint id no longer keys anything above
    /// the adapter.
    /// </returns>
    internal static (TaskCompletionSource<NearbyConnection> Tcs, string DeviceId) PendingHandshake(
        NearbyTransport platform, string endpointId = "endpoint-1", string displayName = "Alice")
    {
        var deviceId = platform.PeerLookup.DeviceIdFor(endpointId);

        platform.PeerLookup.Record(deviceId, displayName);
        var tcs = platform.Handshakes.Register(deviceId, out _, deadline: null, attributedTo: CancellationToken.None);

        return (tcs, deviceId);
    }

    /// <summary>
    /// A live connection, established by driving the real platform success callback rather than by
    /// reaching into the connection's own state.
    /// </summary>
    /// <param name="platform">The platform whose callback establishes the connection.</param>
    /// <param name="displayName">The remote device's display name, as GMS reports it.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The connection, the endpoint id GMS reports it under, and the device id this library minted
    /// for it.
    /// <para>
    /// <b>Both ids are returned because they are not interchangeable.</b> Pass
    /// <c>EndpointId</c> to anything that stands in for a GMS callback — <c>OnPayloadReceived</c>,
    /// <c>OnPayloadTransferUpdate</c>, <c>OnDisconnected</c>, <c>OnEndpointLost</c> — because that
    /// is what the SDK really hands over. Use <c>DeviceId</c> for assertions against the registry,
    /// <c>_activeConnections</c>, or <c>_connectionTcs</c>. Passing a device id to a callback mints
    /// a second id for it, and the payload then routes to a connection nobody holds.
    /// </para>
    /// </returns>
    internal static async Task<(NearbyConnection Connection, string EndpointId, string DeviceId)> ConnectedAsync(
        NearbyTransport platform,
        string displayName,
        CancellationToken cancellationToken)
    {
        const string endpointId = "endpoint-1";

        // Registered under the minted device id, matching what the platform does: the endpoint id
        // reaches only the callback, and everything downstream keys on the device id.
        var deviceId = platform.PeerLookup.DeviceIdFor(endpointId);

        platform.PeerLookup.Record(deviceId, displayName);
        var tcs = platform.Handshakes.Register(deviceId, out _, deadline: null, attributedTo: CancellationToken.None);
        platform.Android().OnConnectionResult(endpointId, Resolution());

        return (await tcs.Task.WaitAsync(cancellationToken), endpointId, deviceId);
    }
}
