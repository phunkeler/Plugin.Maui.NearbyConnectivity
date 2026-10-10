namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Represents a payload that contains raw bytes.
/// </summary>
/// <remarks>
/// A byte payload is limited to <see cref="NearbyConnection.MaxBytesPayloadLength"/> bytes on every
/// platform. Send larger data as a file.
/// </remarks>
/// <seealso cref="NearbyFilePayload"/>
public sealed class NearbyBytesPayload : NearbyPayload
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NearbyBytesPayload"/> class.
    /// </summary>
    /// <param name="data">The bytes that were sent or received.</param>
    public NearbyBytesPayload(ReadOnlyMemory<byte> data) => Data = data;

    /// <summary>
    /// Gets the bytes that were sent or received.
    /// </summary>
    /// <value>
    /// The payload's data. Read it through <see cref="ReadOnlyMemory{T}.Span"/>: for example
    /// <c>Encoding.UTF8.GetString(payload.Data.Span)</c>.
    /// </value>
    /// <remarks>
    /// A <see cref="ReadOnlyMemory{T}"/> rather than an array, so the buffer cannot be mutated
    /// through this property and no defensive copy is needed to hand it out.
    /// </remarks>
    public ReadOnlyMemory<byte> Data { get; }
}
