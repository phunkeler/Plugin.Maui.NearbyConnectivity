using Microsoft.Extensions.Logging.Abstractions;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers the staging sweep that runs at session disposal, so a file the consumer never moved out
/// does not outlive the session that received it.
/// </summary>
/// <remarks>
/// Disposal now drains the per-peer work queue before sweeping, so in the ordinary
/// case no copy is still running here. The sweep stays best-effort for the one residual case the
/// drain cannot cover: a copy that outlives the drain timeout. These tests pin that it never
/// throws and never stops early.
/// </remarks>
[Trait("Category", "Connections")]
public class SweepTests
{
    public sealed class WhenFilesAreStaged : SweepTests
    {
        [Fact]
        public void DeletesThemAll()
        {
            // Arrange
            using var temp = new TempDirectory();
            var staging = new InboundStaging(NullLogger.Instance);
            temp.Touch("photo.jpg");
            temp.Touch("clip.mp4");

            // Act
            staging.Sweep(temp.Path);

            // Assert
            Assert.Empty(Directory.GetFiles(temp.Path));
        }
    }

    public sealed class WhenTheDirectoryIsMissing : SweepTests
    {
        [Fact]
        public void ReturnsQuietly()
        {
            // A session that never received a file never creates the directory, and disposal must
            // not fault on that.

            // Arrange
            using var temp = new TempDirectory();
            var staging = new InboundStaging(NullLogger.Instance);
            var missing = Path.Combine(temp.Path, "never-created");

            // Act
            staging.Sweep(missing);

            // Assert
            Assert.False(Directory.Exists(missing));
        }
    }

    public sealed class WhenOneFileCannotBeDeleted : SweepTests
    {
        [Fact]
        public void StillDeletesTheRest()
        {
            // Arrange
            using var temp = new TempDirectory();
            var staging = new InboundStaging(NullLogger.Instance);
            var locked = temp.Touch("locked.bin");
            temp.Touch("free.bin");
            using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

            // Act
            staging.Sweep(temp.Path);

            // Assert
            Assert.False(File.Exists(Path.Combine(temp.Path, "free.bin")));
        }
    }
}
