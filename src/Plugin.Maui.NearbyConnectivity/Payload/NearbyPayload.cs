namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Represents data sent to, or received from, a nearby device.
/// </summary>
/// <remarks>
/// <para>
/// The abstract base for every payload shape the plugin produces or accepts:
/// <see cref="NearbyBytesPayload"/>, <see cref="NearbyFilePayload"/>, and
/// <see cref="NearbyStreamPayload"/>. Switch on the concrete type to handle one.
/// </para>
/// <para>
/// <b>A class, not a record.</b> A payload owns a resource such as a buffer, a staged file, or a
/// live stream, so reference identity is the correct equality for it. Value equality over a payload
/// would compare handles rather than contents and quietly report two different files as equal.
/// </para>
/// <para>
/// <b>Dispose what you receive.</b> Disposing a received payload releases what it holds: a stream
/// payload closes its stream, a file payload deletes its staged file unless
/// <see cref="NearbyFilePayload.MoveTo(string)"/> already moved it, and a byte payload holds
/// nothing. A <c>using</c> in the receive loop is enough for a loop that handles every shape. Read
/// or move the content first: after disposal the stream is closed and the staged file is gone.
/// </para>
/// </remarks>
/// <seealso cref="NearbyBytesPayload"/>
/// <seealso cref="NearbyFilePayload"/>
/// <seealso cref="NearbyStreamPayload"/>
[SuppressMessage(
    "Design",
    "CA1063:Implement IDisposable Correctly",
    Justification = "The payload shapes are a closed set with no finalizer; DisposeCore is the " +
        "private-protected override point.")]
public abstract class NearbyPayload : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NearbyPayload"/> class.
    /// </summary>
    /// <remarks>
    /// Private-protected: the three payload shapes are the closed set this plugin produces, and a
    /// caller switching over them can rely on that.
    /// </remarks>
    private protected NearbyPayload()
    {
    }

    /// <summary>
    /// Releases the stream or staged file this payload holds.
    /// </summary>
    /// <remarks>
    /// Safe to call more than once, and never throws. A staged file that cannot be deleted now is
    /// deleted when the session stops. The plugin disposes any payload still undelivered when its
    /// connection closes.
    /// </remarks>
    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    private protected virtual void DisposeCore()
    {
    }
}
