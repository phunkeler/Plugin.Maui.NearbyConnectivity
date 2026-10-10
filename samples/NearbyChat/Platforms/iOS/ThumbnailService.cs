using AVFoundation;
using CoreMedia;
using Foundation;

namespace NearbyChat.Services;

public static class ThumbnailService
{
    // The bubble is 160 pt; the renderer draws at the screen's scale, so this is 480 px at 3x.
    const double PhotoThumbnailEdge = 160;

    /// <summary>
    /// Returns a bubble-sized thumbnail for the given photo file, held in memory, or
    /// <see langword="null"/> if the photo could not be decoded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The thumbnail is drawn here, once, rather than handing the bubble the original file to
    /// decode whenever it renders. A sent photo's bubble then shows the picture the moment it is
    /// added, independent of how long the picker's copy lives.
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
            using var image = UIKit.UIImage.FromFile(filePath);

            if (image is null || image.Size.Width <= 0 || image.Size.Height <= 0)
            {
                return Task.FromResult<ImageSource?>(null);
            }

            var scale = Math.Min(1, Math.Min(
                PhotoThumbnailEdge / image.Size.Width,
                PhotoThumbnailEdge / image.Size.Height));
            var size = new CoreGraphics.CGSize(image.Size.Width * scale, image.Size.Height * scale);

            // Draw applies the image's orientation, so a portrait photo is not shown on its side.
            using var renderer = new UIKit.UIGraphicsImageRenderer(size);
            using var thumbnail = renderer.CreateImage(_ =>
                image.Draw(new CoreGraphics.CGRect(CoreGraphics.CGPoint.Empty, size)));
            using var png = thumbnail.AsPNG();

            if (png is null)
            {
                return Task.FromResult<ImageSource?>(null);
            }

            var bytes = png.ToArray();

            return Task.FromResult<ImageSource?>(ImageSource.FromStream(() => new MemoryStream(bytes)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Photo thumbnail failed for '{filePath}': {ex}");

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
        try
        {
            using var url = NSUrl.FromFilename(filePath);
            using var asset = AVUrlAsset.Create(url);
            var seconds = asset.Duration.Seconds;

            return double.IsFinite(seconds) && seconds >= 0 ? TimeSpan.FromSeconds(seconds) : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Video duration failed for '{filePath}': {ex}");

            return null;
        }
    }

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
    public static async Task<ImageSource?> GetVideoThumbnailAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var url = NSUrl.FromFilename(filePath);
            using var asset = AVAsset.FromUrl(url);
            using var generator = new AVAssetImageGenerator(asset)
            {
                AppliesPreferredTrackTransform = true
            };

            var time = new CMTime(1, 1);
            var tcs = new TaskCompletionSource<ImageSource?>();

            using var _ = cancellationToken.Register(() =>
            {
                generator.CancelAllCGImageGeneration();
                tcs.TrySetCanceled(cancellationToken);
            });

            generator.GenerateCGImageAsynchronously(time, (imageRef, actualTime, error) =>
            {
                // Runs on an AVFoundation thread. An exception thrown here is unobservable and
                // would leave the TCS unresolved, so the caller would await forever.
                try
                {
                    if (error is not null || imageRef is null)
                    {
                        tcs.TrySetResult(null);
                        return;
                    }

                    using var uiImage = new UIKit.UIImage(imageRef);
                    using var png = uiImage.AsPNG();

                    if (png is null)
                    {
                        tcs.TrySetResult(null);
                        return;
                    }

                    // Materialized here rather than in a deferred stream factory: the UIImage and
                    // its PNG representation are both disposed on the way out of this callback.
                    var bytes = png.ToArray();

                    tcs.TrySetResult(ImageSource.FromStream(() => new MemoryStream(bytes)));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Video thumbnail callback failed for '{filePath}': {ex}");

                    tcs.TrySetResult(null);
                }
            });

            return await tcs.Task;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // NSUrl.FromFilename, AVAsset.FromUrl, and the generator constructor all throw for an
            // unreadable or malformed file, before the callback above is ever wired up.
            System.Diagnostics.Debug.WriteLine($"Video thumbnail failed for '{filePath}': {ex}");

            return null;
        }
    }
}
