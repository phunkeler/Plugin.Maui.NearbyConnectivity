namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Represents a payload that contains a file.
/// </summary>
/// <remarks>
/// <para>
/// A received file is staged in app-private, operating-system-purgeable storage, and it is yours
/// from the moment this payload reaches your receive loop. Call <see cref="MoveTo(string)"/> to
/// keep it, or dispose the payload to delete it now.
/// </para>
/// <para>
/// A file you neither move nor dispose is deleted when the session is disposed, or the next time
/// the application creates the session. The operating system may reclaim the staging directory
/// before then. Move what you want to keep.
/// </para>
/// <para>
/// A payload you construct yourself does not own its file: disposing it leaves the file alone.
/// </para>
/// <para>
/// <c>FileName</c> and <c>ContentType</c> derive from what the sending device supplied. Treat both
/// as untrusted input: validate the content before acting on the declared type.
/// </para>
/// </remarks>
/// <seealso cref="NearbyBytesPayload"/>
public sealed class NearbyFilePayload : NearbyPayload
{
    bool _staged;

    /// <summary>
    /// Initializes a new instance of the <see cref="NearbyFilePayload"/> class.
    /// </summary>
    /// <param name="file">The file that was sent or received.</param>
    public NearbyFilePayload(FileResult file)
    {
        ArgumentNullException.ThrowIfNull(file);
        File = file;
    }

    internal static NearbyFilePayload Staged(string path) => new(new FileResult(path)) { _staged = true };

    /// <summary>
    /// Gets the file that was sent or received.
    /// </summary>
    /// <value>
    /// The staged file. After <see cref="MoveTo(string)"/> this points at the new location.
    /// </value>
    public FileResult File { get; private set; }

    /// <summary>
    /// Moves the received file out of staging to a location this application owns.
    /// </summary>
    /// <param name="destinationPath">
    /// The full path to move the file to, file name included. Missing parent directories are
    /// created.
    /// </param>
    /// <returns>A <see cref="FileResult"/> for the file at its new location.</returns>
    /// <remarks>
    /// <para>
    /// The move consumes the staged file, so disposing the payload afterwards leaves the moved file
    /// alone. Copying the stream instead keeps the staged original until you dispose the payload or
    /// the session is disposed. <see cref="File"/> is updated to point at the new location, so this
    /// payload stays usable afterwards.
    /// </para>
    /// <para>
    /// Within the application sandbox this is a rename and returns immediately, which is why it is
    /// synchronous. Call it once per payload. Concurrent calls for the same payload are not
    /// supported.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="destinationPath"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="IOException">
    /// A file already exists at <paramref name="destinationPath"/>, or the file has already been
    /// moved, disposed, or purged from staging.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The application cannot write to the destination.</exception>
    public FileResult MoveTo(string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(destinationPath);

        if (Path.GetDirectoryName(destinationPath) is { Length: > 0 } parent)
        {
            Directory.CreateDirectory(parent);
        }

        System.IO.File.Move(File.FullPath, destinationPath);

        File = new FileResult(destinationPath);
        _staged = false;

        return File;
    }

    private protected override void DisposeCore()
    {
        if (!_staged)
        {
            return;
        }

        try
        {
            System.IO.File.Delete(File.FullPath);
            _staged = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The staging sweep deletes the file when the session stops.
        }
    }
}
