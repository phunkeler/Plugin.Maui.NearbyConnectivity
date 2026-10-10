using Android.Graphics;
using Android.Media;
using Android.Provider;

namespace NearbyChat.Services;

public static class ThumbnailService
{
    static readonly Bitmap.CompressFormat s_png = Bitmap.CompressFormat.Png
        ?? throw new InvalidOperationException("Android reported no PNG format.");
    static readonly Bitmap.CompressFormat s_jpeg = Bitmap.CompressFormat.Jpeg
        ?? throw new InvalidOperationException("Android reported no JPEG format.");

    /// <summary>
    /// Returns a thumbnail for the given video file path, or <see langword="null"/> if the
    /// thumbnail could not be generated.
    /// </summary>
    /// <remarks>
    /// <b>Never throws except on cancellation.</b> The caller is the inbound payload loop, and an
    /// exception escaping here ends that loop for the whole connection — every later payload from
    /// that peer would be lost. A thumbnail is decoration, so every failure degrades to
    /// <see langword="null"/> instead.
    /// </remarks>
    public static Task<ImageSource?> GetVideoThumbnailAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var bitmap = CreateThumbnail(filePath);

            if (bitmap is null)
            {
                return Task.FromResult<ImageSource?>(null);
            }

            var tempFilePath = SaveBitmapToCache(bitmap);

            if (string.IsNullOrWhiteSpace(tempFilePath))
            {
                return Task.FromResult<ImageSource?>(null);
            }

            return Task.FromResult<ImageSource?>(ImageSource.FromFile(tempFilePath));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort, and deliberately broad. Android media failures arrive as
            // Java.Lang.Exception (corrupt file, unsupported codec), but SaveBitmapToCache does
            // managed file I/O and throws IOException or UnauthorizedAccessException — a full
            // cache directory must not escape this catch and kill the receive loop.
            System.Diagnostics.Debug.WriteLine($"Video thumbnail failed for '{filePath}': {ex}");

            return Task.FromResult<ImageSource?>(null);
        }
    }

    /// <summary>
    /// Returns how long the given video runs, or <see langword="null"/> if the file cannot be read.
    /// </summary>
    /// <remarks>
    /// <b>Never throws.</b> The receive loop calls this too, and a duration is decoration.
    /// </remarks>
    public static TimeSpan? GetVideoDuration(string filePath)
    {
        var retriever = new MediaMetadataRetriever();

        try
        {
            retriever.SetDataSource(filePath);

            return long.TryParse(retriever.ExtractMetadata(MetadataKey.Duration), out var milliseconds)
                ? TimeSpan.FromMilliseconds(milliseconds)
                : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Video duration failed for '{filePath}': {ex}");

            return null;
        }
        finally
        {
            retriever.Release();
            retriever.Dispose();
        }
    }

    /// <summary>
    /// Returns a bubble-sized thumbnail for the given photo file, held in memory, or
    /// <see langword="null"/> if the photo could not be decoded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The thumbnail is decoded here, once, rather than handing the bubble the original file to
    /// decode whenever it renders. A sent photo's bubble then shows the picture the moment it is
    /// added, independent of how long the picker's copy lives, and a 12 MP photo is never decoded
    /// for a 160 dp box.
    /// </para>
    /// <para>
    /// <b>Never throws except on cancellation</b>, for the same reason as
    /// <see cref="GetVideoThumbnailAsync"/>: the receive loop calls this too.
    /// </para>
    /// </remarks>
    public static Task<ImageSource?> GetPhotoThumbnailAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var bitmap = CreatePhotoThumbnail(filePath);

            if (bitmap is null)
            {
                return Task.FromResult<ImageSource?>(null);
            }

            byte[] bytes;

            using (var buffer = new MemoryStream())
            {
                // PNG keeps a transparent image transparent; JPEG would turn it black.
                if (bitmap.HasAlpha)
                {
                    bitmap.Compress(s_png, 100, buffer);
                }
                else
                {
                    bitmap.Compress(s_jpeg, 90, buffer);
                }

                bytes = buffer.ToArray();
            }

            return Task.FromResult<ImageSource?>(ImageSource.FromStream(() => new MemoryStream(bytes)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Photo thumbnail failed for '{filePath}': {ex}");

            return Task.FromResult<ImageSource?>(null);
        }
    }

    // The bubble is 160 dp; 600 px covers it up to 3.75x without decoding the full photo.
    const int PhotoThumbnailEdge = 600;

    static Bitmap? CreatePhotoThumbnail(string filePath)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            // Applies the EXIF orientation, so a portrait photo is not shown on its side.
            return ThumbnailUtils.CreateImageThumbnail(
                new Java.IO.File(filePath),
                new Android.Util.Size(PhotoThumbnailEdge, PhotoThumbnailEdge),
                null);
        }

        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(filePath, bounds);

        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
        {
            return null;
        }

        var sampleSize = 1;

        while (bounds.OutWidth / (sampleSize * 2) >= PhotoThumbnailEdge
            && bounds.OutHeight / (sampleSize * 2) >= PhotoThumbnailEdge)
        {
            sampleSize *= 2;
        }

        var decoded = BitmapFactory.DecodeFile(filePath, new BitmapFactory.Options { InSampleSize = sampleSize });

        if (decoded is null)
        {
            return null;
        }

        using var exif = new ExifInterface(filePath);

        var degrees = exif.GetAttributeInt(ExifInterface.TagOrientation, 1) switch
        {
            6 => 90,
            3 => 180,
            8 => 270,
            _ => 0
        };

        if (degrees == 0)
        {
            return decoded;
        }

        using var matrix = new Android.Graphics.Matrix();
        matrix.PostRotate(degrees);

        var rotated = Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true);
        decoded.Dispose();

        return rotated;
    }

    static Bitmap? CreateThumbnail(string filePath)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            return ThumbnailUtils.CreateVideoThumbnail(new Java.IO.File(filePath), new Android.Util.Size(200, 200), null);
        }
        else
        {
            return ThumbnailUtils.CreateVideoThumbnail(filePath, ThumbnailKind.MiniKind);
        }
    }

    static string SaveBitmapToCache(Bitmap bitmap, string extension = ".png")
    {
        var fileName = $"thumb_{Guid.NewGuid():N}{extension}";
        var filePath = System.IO.Path.Combine(FileSystem.CacheDirectory, fileName);

        using (var fileStream = new FileStream(filePath, FileMode.Create))
        {
            bitmap.Compress(s_png, 90, fileStream);
        }

        return filePath;
    }
}
