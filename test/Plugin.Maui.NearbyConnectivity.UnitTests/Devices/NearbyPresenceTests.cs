using System.Globalization;
using System.Text;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Devices")]
public class NearbyPresenceTests
{
    public sealed class Defaults : NearbyPresenceTests
    {
        [Fact]
        public void DisplayName_IsThisMachinesName()
        {
            // Arrange & Act
            // DeviceInfo.Name throws off a device, so net10.0 gets the machine name.
            var presence = new NearbyPresence();

            // Assert
            Assert.Equal(Environment.MachineName, presence.DisplayName);
        }

        [Fact]
        public void Info_IsEmpty() => Assert.True(new NearbyPresence().Info.IsEmpty);
    }

    public sealed class Validation : NearbyPresenceTests
    {
        [Fact]
        public void NameOfExactlyTheCap_IsAccepted()
        {
            // Arrange
            var displayName = new string('x', NearbyPresence.MaxDisplayNameBytes);

            // Act & Assert
            Assert.Equal(displayName, new NearbyPresence { DisplayName = displayName }.DisplayName);
        }

        [Fact]
        public void InfoOfExactlyTheCap_IsAccepted()
        {
            // Arrange
            var info = new byte[NearbyPresence.MaxInfoBytes];

            // Act & Assert
            Assert.Equal(info.Length, new NearbyPresence { Info = info }.Info.Length);
        }

        [Fact]
        public void NameOneByteOverTheCap_Throws()
        {
            // Arrange
            var displayName = new string('x', NearbyPresence.MaxDisplayNameBytes + 1);

            // Act
            var ex = Assert.Throws<ArgumentException>(
                () => new NearbyPresence { DisplayName = displayName });

            // Assert
            Assert.Contains(nameof(NearbyPresence.DisplayName), ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void InfoOneByteOverTheCap_Throws()
        {
            // Act
            var ex = Assert.Throws<ArgumentException>(
                () => new NearbyPresence { Info = new byte[NearbyPresence.MaxInfoBytes + 1] });

            // Assert
            Assert.Contains(nameof(NearbyPresence.Info), ex.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(null, TestDisplayName = "a null name")]
        [InlineData("", TestDisplayName = "an empty name")]
        public void NullOrEmptyName_Throws(string? displayName)
            => Assert.Throws<ArgumentException>(() => new NearbyPresence { DisplayName = displayName! });

        [Fact]
        public void With_Revalidates()
        {
            // Arrange
            var presence = new NearbyPresence { DisplayName = "Alice" };

            // Act & Assert
            // A copy is not a way around the caps.
            Assert.Throws<ArgumentException>(
                () => presence with { DisplayName = new string('x', NearbyPresence.MaxDisplayNameBytes + 1) });
        }

        // Nothing is stripped: what the caller sets is what peers see, or the set throws.
        // Rendering policy belongs to the application.
        [Theory]
        [InlineData("photo\u202Egnp.exe", TestDisplayName = "a bidi override")]
        [InlineData("Alice\r\n\tPhone", TestDisplayName = "control characters")]
        [InlineData("می\u200Cخواهم", TestDisplayName = "a Persian name with ZWNJ")]
        [InlineData("👨\u200D👩\u200D👧", TestDisplayName = "a ZWJ emoji sequence")]
        public void NameWithUnusualCharacters_IsKeptAsSet(string displayName)
            => Assert.Equal(displayName, new NearbyPresence { DisplayName = displayName }.DisplayName);

        [Fact]
        public void MultiByteNameWithinTheCap_IsAccepted()
        {
            // Arrange
            var displayName = new string('あ', NearbyPresence.MaxDisplayNameBytes / 3);

            // Act & Assert
            Assert.Equal(NearbyPresence.MaxDisplayNameBytes, Encoding.UTF8.GetByteCount(displayName));
            Assert.Equal(displayName, new NearbyPresence { DisplayName = displayName }.DisplayName);
        }

        [Fact]
        public void RejectionMessage_NamesTheByteCountAndTheLimit()
        {
            // Arrange
            var displayName = new string('A', NearbyPresence.MaxDisplayNameBytes + 1);

            // Act
            var ex = Assert.Throws<ArgumentException>(
                () => new NearbyPresence { DisplayName = displayName });

            // Assert
            Assert.Contains(
                $"{NearbyPresence.MaxDisplayNameBytes + 1} UTF-8 bytes",
                ex.Message,
                StringComparison.Ordinal);
            Assert.Contains(
                NearbyPresence.MaxDisplayNameBytes.ToString(CultureInfo.InvariantCulture),
                ex.Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public void MultiByteNameIsCappedInBytesNotCharacters()
        {
            // Arrange
            // 'あ' is three UTF-8 bytes, so 22 of them exceed the 63-byte cap.
            var displayName = new string('あ', 22);

            // Act & Assert
            Assert.True(Encoding.UTF8.GetByteCount(displayName) > NearbyPresence.MaxDisplayNameBytes);
            Assert.Throws<ArgumentException>(() => new NearbyPresence { DisplayName = displayName });
        }
    }

    public sealed class Copying : NearbyPresenceTests
    {
        [Fact]
        public void Info_IsCopiedOnInit()
        {
            // Arrange
            var source = "route:R17"u8.ToArray();
            var presence = new NearbyPresence { DisplayName = "Alice", Info = source };

            // Act
            // A caller mutating its own array afterwards must not change what is advertised.
            source[0] = 0xFF;

            // Assert
            Assert.Equal("route:R17"u8.ToArray(), presence.Info.ToArray());
        }
    }

    public sealed class Equality : NearbyPresenceTests
    {
        [Fact]
        public void SameBytesFromDifferentArrays_AreEqual()
        {
            // Arrange
            var a = new NearbyPresence { DisplayName = "Alice", Info = "route:R17"u8.ToArray() };
            var b = new NearbyPresence { DisplayName = "Alice", Info = "route:R17"u8.ToArray() };

            // Assert
            // The generated Equals would compare the memory by reference and say no.
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void OneDifferingByte_IsNotEqual()
        {
            // Arrange
            var a = new NearbyPresence { DisplayName = "Alice", Info = "route:R17"u8.ToArray() };
            var b = new NearbyPresence { DisplayName = "Alice", Info = "route:R18"u8.ToArray() };

            // Assert
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void DifferingName_IsNotEqual()
            => Assert.NotEqual(
                new NearbyPresence { DisplayName = "Alice" },
                new NearbyPresence { DisplayName = "alice" });
    }
}
