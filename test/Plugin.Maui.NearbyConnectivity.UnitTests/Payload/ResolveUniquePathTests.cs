namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers inbound file naming: two peers sending the same filename must not overwrite each other.
/// </summary>
/// <remarks>
/// The bug this guards against was silent — the second <c>photo.jpg</c> replaced the first, the app
/// saw one file where it expected two, and nothing logged an error. Both platforms route inbound
/// files through this method.
/// </remarks>
[Trait("Category", "Connections")]
public class ResolveUniquePathTests
{
    public sealed class WhenNameIsFree : ResolveUniquePathTests
    {
        [Fact]
        public void ReturnsTheNameUnchanged()
        {
            // Arrange
            using var temp = new TempDirectory();
            var dir = temp.Path;

            // Act
            var result = InboundStaging.ResolveUniquePath(dir, "photo.jpg");

            // Assert
            Assert.Equal(Path.Combine(dir, "photo.jpg"), result);
        }
    }

    public sealed class WhenNameIsTaken : ResolveUniquePathTests
    {
        [Fact]
        public void AppendsACounterBeforeTheExtension()
        {
            // " (1)" goes before ".jpg", not after — otherwise the file loses its extension and
            // the OS no longer knows how to open it.

            // Arrange
            using var temp = new TempDirectory();
            var dir = temp.Path;
            temp.Touch("photo.jpg");

            // Act
            var result = InboundStaging.ResolveUniquePath(dir, "photo.jpg");

            // Assert
            Assert.Equal(Path.Combine(dir, "photo (1).jpg"), result);
        }

        [Fact]
        public void CountsUpPastMultipleCollisions()
        {
            // Arrange
            using var temp = new TempDirectory();
            var dir = temp.Path;
            temp.Touch("photo.jpg");
            temp.Touch(Path.Combine(dir, "photo (1).jpg"));
            temp.Touch(Path.Combine(dir, "photo (2).jpg"));

            // Act
            var result = InboundStaging.ResolveUniquePath(dir, "photo.jpg");

            // Assert
            Assert.Equal(Path.Combine(dir, "photo (3).jpg"), result);
        }

        [Fact]
        public void StopsCountingOnceTheNumberedNamesAreTaken()
        {
            // Arrange
            using var temp = new TempDirectory();
            var dir = temp.Path;
            temp.Touch("photo.jpg");

            for (var i = 1; i <= InboundStaging.MaxNumberedNames; i++)
            {
                temp.Touch($"photo ({i}).jpg");
            }

            // Act
            var result = InboundStaging.ResolveUniquePath(dir, "photo.jpg");

            // Assert
            var name = Path.GetFileName(result);

            Assert.Equal(dir, Path.GetDirectoryName(result));
            Assert.False(File.Exists(result));
            Assert.NotEqual($"photo ({InboundStaging.MaxNumberedNames + 1}).jpg", name);
            Assert.StartsWith("photo (", name, StringComparison.Ordinal);
            Assert.EndsWith(").jpg", name, StringComparison.Ordinal);
        }

        [Fact]
        public void HandlesNamesWithNoExtension()
        {
            // Arrange
            using var temp = new TempDirectory();
            var dir = temp.Path;
            temp.Touch("README");

            // Act
            var result = InboundStaging.ResolveUniquePath(dir, "README");

            // Assert
            Assert.Equal(Path.Combine(dir, "README (1)"), result);
        }

        [Fact]
        public void HandlesNamesWithMultipleDots()
        {
            // Only the final segment is the extension: "archive.tar.gz" must become
            // "archive.tar (1).gz", matching Path.GetExtension semantics.

            // Arrange
            using var temp = new TempDirectory();
            var dir = temp.Path;
            temp.Touch("archive.tar.gz");

            // Act
            var result = InboundStaging.ResolveUniquePath(dir, "archive.tar.gz");

            // Assert
            Assert.Equal(Path.Combine(dir, "archive.tar (1).gz"), result);
        }

        [Fact]
        public void ResultIsAlwaysAFreePath()
        {
            // The property that actually matters: whatever is returned must not already exist, or
            // the caller silently overwrites a file it did not create. Ten consecutive transfers is
            // simulated in a loop because the property under test — freshness relative to the
            // previous call's result — only shows up across repeated calls, not a single one.
            const int TransferCount = 10;

            // Arrange
            using var temp = new TempDirectory();
            var dir = temp.Path;

            // Act
            // Each iteration simulates the caller writing the file it was handed, so the next
            // call must pick a new name.
            for (var i = 0; i < TransferCount; i++)
            {
                var next = InboundStaging.ResolveUniquePath(dir, "photo.jpg");
                Assert.False(File.Exists(next), $"Iteration {i} returned an existing path: {next}");
                temp.Touch(next);
            }

            // Assert
            // Ten transfers should have produced ten distinct files.
            Assert.Equal(TransferCount, Directory.GetFiles(dir).Length);
        }
    }
}
