using System.Text;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Options")]
public class NearbyTextTests
{
    public sealed class Truncate : NearbyTextTests
    {
        [Fact]
        public void ValueWithinTheCap_IsUnchanged()
            => Assert.Equal("Alice", NearbyText.Truncate("Alice"));

        [Fact]
        public void NullValue_BecomesEmpty()
            => Assert.Empty(NearbyText.Truncate(null));

        [Fact]
        public void OverlongValue_IsCutToTheCap()
        {
            // Arrange
            // ASCII, so one UTF-8 byte per character.
            var value = new string('A', NearbyText.MaxDisplayNameBytes + 50);

            // Act
            var cut = NearbyText.Truncate(value);

            // Assert
            Assert.Equal(NearbyText.MaxDisplayNameBytes, cut.Length);
        }

        [Fact]
        public void OverlongMultiByteValue_IsCutByBytesNotCharacters()
        {
            // Arrange
            // U+00E9 is one char but two UTF-8 bytes.
            var value = new string('é', NearbyText.MaxDisplayNameBytes);

            // Act
            var cut = NearbyText.Truncate(value);

            // Assert
            Assert.Equal(NearbyText.MaxDisplayNameBytes / 2, cut.Length);
            Assert.True(Encoding.UTF8.GetByteCount(cut) <= NearbyText.MaxDisplayNameBytes);
        }

        [Fact]
        public void CutDoesNotSplitSurrogatePair()
        {
            // Arrange
            // Every rune is 4 UTF-8 bytes and 2 UTF-16 units.
            var value = string.Concat(Enumerable.Repeat("\U0001F600", NearbyText.MaxDisplayNameBytes));

            // Act
            var cut = NearbyText.Truncate(value);

            // Assert
            // A split pair leaves a lone high surrogate at the end.
            Assert.False(char.IsHighSurrogate(cut[^1]));
            Assert.Equal(NearbyText.MaxDisplayNameBytes / 4 * 2, cut.Length);
        }

        // Nothing but length: a cut value keeps every character that fits.
        [Fact]
        public void ControlCharactersWithinTheCap_Survive()
            => Assert.Equal("log\r\n.txt", NearbyText.Truncate("log\r\n.txt"));
    }
}
