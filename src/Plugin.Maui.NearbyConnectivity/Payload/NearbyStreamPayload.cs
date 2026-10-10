namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Represents an inbound named byte stream, for live or unknown-length data.
/// </summary>
/// <remarks>
/// <para>
/// <b>Experimental.</b> Streams have not been tested between two devices, and their behavior and
/// API can change in any release. Code that uses this type gets build error <c>NEARBY001</c>.
/// To opt in, add <c>NEARBY001</c> to <c>NoWarn</c> in the project file, or suppress it with
/// <c>#pragma warning disable NEARBY001</c>.
/// </para>
/// <para>
/// Arrives through the same <see cref="INearbyConnectivity.PayloadsAsync(CancellationToken)"/>
/// stream as every other payload.
/// The remote device opened it with
/// <see cref="NearbyConnection.OpenStreamAsync(string, CancellationToken)"/>.
/// </para>
/// </remarks>
/// <seealso cref="NearbyBytesPayload"/>
/// <seealso cref="NearbyFilePayload"/>
[Experimental("NEARBY001", UrlFormat = "https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/payloads.html#open-a-stream")]
public sealed class NearbyStreamPayload : NearbyPayload
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NearbyStreamPayload"/> class.
    /// </summary>
    /// <param name="name">The name the remote device opened the stream with.</param>
    /// <param name="stream">The readable stream.</param>
    public NearbyStreamPayload(string name, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(stream);

        Name = name;
        Stream = stream;
    }

    /// <summary>
    /// Gets the name the remote device opened the stream with.
    /// </summary>
    /// <value>
    /// Remote-chosen input. Display it with the same care as a display name.
    /// </value>
    public string Name { get; }

    /// <summary>
    /// Gets the readable stream.
    /// </summary>
    /// <value>
    /// The stream's readable half. It ends when the remote writer disposes its own half, or when
    /// the connection drops.
    /// </value>
    /// <remarks>
    /// <b>You own it.</b> Read it to the end or dispose the payload. A stream left unread holds the
    /// connection's inbound capacity; the plugin disposes any that are still open when the
    /// connection closes, but that is a backstop rather than the plan.
    /// </remarks>
    public Stream Stream { get; }

    private protected override void DisposeCore() => Stream.Dispose();
}
