namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Transfer")]
public class NearbyTransferProgressTests
{
    public sealed class Fraction : NearbyTransferProgressTests
    {
        [Fact]
        public void KnownSize_NoProgress_ReturnsZero()
        {
            // Arrange
            var progress = new NearbyTransferProgress(1, bytesTransferred: 0, totalBytes: 1000, NearbyTransferStatus.InProgress);

            // Act
            var result = progress.Fraction;

            // Assert
            Assert.Equal(0.0, result);
        }

        [Fact]
        public void KnownSize_PartialProgress_ReturnsFraction()
        {
            // Arrange
            var progress = new NearbyTransferProgress(1, bytesTransferred: 500, totalBytes: 1000, NearbyTransferStatus.InProgress);

            // Act
            var result = progress.Fraction;

            // Assert
            Assert.Equal(0.5, result);
        }

        [Fact]
        public void KnownSize_Complete_ReturnsOne()
        {
            // Arrange
            var progress = new NearbyTransferProgress(1, bytesTransferred: 1000, totalBytes: 1000, NearbyTransferStatus.Completed);

            // Act
            var result = progress.Fraction;

            // Assert
            Assert.Equal(1.0, result);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        public void ANonPositiveTotal_IsUnknown(long reported)
        {
            // Arrange
            var progress = new NearbyTransferProgress(1, bytesTransferred: 500, totalBytes: reported, NearbyTransferStatus.InProgress);

            // Assert
            Assert.Null(progress.TotalBytes);
            Assert.Null(progress.Fraction);
        }
    }
}
