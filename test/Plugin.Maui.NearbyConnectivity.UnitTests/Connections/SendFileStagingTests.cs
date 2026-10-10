using System.Text;
using Microsoft.Maui.Storage;
using NSubstitute;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers sending a <see cref="FileResult"/> whose path is not readable, which is what a picker
/// returns on iOS.
/// </summary>
/// <remarks>
/// The library stages such a file itself. Before this existed, every consumer had to reconstruct
/// the copy by hand, and the sample application did exactly that.
/// </remarks>
[Trait("Category", "Connections")]
public class SendFileStagingTests
{
    static string DirectoryOf(string path)
        => Path.GetDirectoryName(path) ?? throw new InvalidOperationException($"'{path}' has no directory.");

    public sealed class WhenThePathIsReadable : SendFileStagingTests
    {
        [Fact]
        public async Task SendsItDirectly()
        {
            // Arrange
            using var temp = new TempDirectory();
            var real = temp.Touch("photo.jpg");
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);

            // Act
            await connection.SendFileAsync(
                new FileResult(real),
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            await platform.Received(1).SendFileAsync(
                real,
                "photo.jpg",
                Arg.Any<IProgress<NearbyTransferProgress>?>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SendsThePickerNameNotTheTemporaryOne()
        {
            // MAUI's iOS picker returns a temporary copy under a generated name and keeps the real
            // name in FileName. The receiver must see the real one.

            // Arrange
            using var temp = new TempDirectory();
            var real = temp.Touch("3f2a9c.jpg");
            var platform = Substitute.For<IPlatformConnection>();
            var connection = Create.Connection(platform: platform);

            // Act
            await connection.SendFileAsync(
                new FileResult(real) { FileName = "IMG_0001.jpeg" },
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            await platform.Received(1).SendFileAsync(
                real,
                "IMG_0001.jpeg",
                Arg.Any<IProgress<NearbyTransferProgress>?>(),
                Arg.Any<CancellationToken>());
        }
    }

    public sealed class WhenStagingIsNeeded : SendFileStagingTests
    {
        [Fact]
        public async Task CopiesTheContent()
        {
            // Arrange
            var expected = "payload bytes";

            // Act
            var path = await NearbyConnection.StageToTempAsync(
                () => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(expected))),
                "photo.jpg",
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(expected, File.ReadAllText(path));
            Directory.Delete(DirectoryOf(path), recursive: true);
        }

        [Fact]
        public async Task KeepsTheOriginalFileName()
        {
            // iOS sends the last path component as the resource name, so a random temporary name
            // would reach the receiver in place of the real one.

            // Arrange
            var expected = "photo.jpg";

            // Act
            var path = await NearbyConnection.StageToTempAsync(
                () => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])),
                expected,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(expected, Path.GetFileName(path));
            Directory.Delete(DirectoryOf(path), recursive: true);
        }

        [Fact]
        public async Task GivesConcurrentSendsOfTheSameNameSeparateDirectories()
        {
            // Arrange
            var name = "photo.jpg";

            // Act
            var firstPath = await NearbyConnection.StageToTempAsync(
                () => Task.FromResult<Stream>(new MemoryStream([1])),
                name,
                TestContext.Current.CancellationToken);
            var secondPath = await NearbyConnection.StageToTempAsync(
                () => Task.FromResult<Stream>(new MemoryStream([2])),
                name,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.NotEqual(firstPath, secondPath);
            Directory.Delete(DirectoryOf(firstPath), recursive: true);
            Directory.Delete(DirectoryOf(secondPath), recursive: true);
        }

        [Fact]
        public async Task StripsADirectoryComponentFromTheName()
        {
            // Arrange
            var expected = "evil.jpg";

            // Act
            var path = await NearbyConnection.StageToTempAsync(
                () => Task.FromResult<Stream>(new MemoryStream([1])),
                "../evil.jpg",
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(Path.Combine(DirectoryOf(path), expected), path);
            Directory.Delete(DirectoryOf(path), recursive: true);
        }
    }
}
