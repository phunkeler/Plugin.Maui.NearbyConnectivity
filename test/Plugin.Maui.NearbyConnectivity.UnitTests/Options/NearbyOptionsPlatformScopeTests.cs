using System.Text;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers the platform-scoped option objects on <see cref="NearbyOptions"/>.
/// </summary>
/// <remarks>
/// These settings are present on every target framework and named by platform at the call site,
/// so shared MAUI code sets them without <c>#if</c>. This suite runs on <c>net10.0</c> — the target
/// that consumes none of them — so it fails if the scope is ever made conditional.
/// </remarks>
[Trait("Category", "Options")]
public class NearbyOptionsPlatformScopeTests
{
    public sealed class PlatformScopes : NearbyOptionsPlatformScopeTests
    {
        [Fact]
        public void Scopes_AreAvailableOnEveryTargetFramework()
        {
            // Arrange
            var options = new NearbyOptions();

            // Act
            var android = options.Android;

            // Assert
            // Android options must exist on every TFM, not just android.
            Assert.NotNull(android);
        }

        [Fact]
        public void Scopes_AreIndependentBetweenInstances()
        {
            // Arrange
            var first = new NearbyOptions();
            var second = new NearbyOptions();

            // Act
            first.Android.Topology = NearbyTopology.Star;

            // Assert
            // The scope objects must not be shared between options instances.
            Assert.Equal(NearbyTopology.Cluster, second.Android.Topology);
        }
    }

    public sealed class Defaults : NearbyOptionsPlatformScopeTests
    {
        [Fact]
        public void PlatformScopes_CarryTheDocumentedDefaults()
        {
            // Arrange
            var options = new NearbyOptions();

            // Act
            var actual = (
                options.Android.Topology,
                options.Android.ConnectionType,
                options.Android.UseLowPower);

            // Assert
            Assert.Equal(
                (NearbyTopology.Cluster,
                 NearbyConnectionType.Balanced,
                 false),
                actual);
        }

        [Fact]
        public void Presence_PassesValidationUnset()
        {
            // Arrange
            // The default has to survive the validator on its own: a consumer who sets only
            // ServiceId must not be failed by a name they never chose. This ran per-target
            // before, so only net10.0's default was ever exercised here.
            var options = new NearbyOptions { ServiceId = "test-service" };

            // Act & Assert
            NearbyOptionsValidator.Validate(options);
            Assert.NotEmpty(options.Presence.DisplayName);
            Assert.True(options.Presence.Info.IsEmpty);
        }

        [Fact]
        public void DefaultDisplayName_LongerThanTheByteCap_IsCutRatherThanRejected()
        {
            // Arrange
            // The device name is the user's, not the developer's: a phone named past the cap must
            // not throw out of AddNearbyConnectivity. Cutting is what the default does with
            // whatever DeviceInfo.Name returns, so pin the rule the default relies on.
            var overCap = new string('A', NearbyText.MaxDisplayNameBytes + 20);

            // Act
            var cut = NearbyText.Truncate(overCap);

            // Assert
            // The cut value is one the presence accepts.
            Assert.Equal(cut, new NearbyPresence { DisplayName = cut }.DisplayName);
            Assert.Equal(NearbyText.MaxDisplayNameBytes, Encoding.UTF8.GetByteCount(cut));
        }

        [Fact]
        public void OfferWindow_IsTwoMinutes()
        {
            // Arrange
            var expected = TimeSpan.FromMinutes(2);

            // Act
            var actual = new NearbyOptions().OfferWindow;

            // Assert
            Assert.Equal(expected, actual);
            Assert.Equal(OfferWindowPolicy.s_default, actual);
        }

        [Theory]
        [InlineData("zero")]
        [InlineData("negative")]
        [InlineData("infinite")]
        [InlineData("above the bound")]
        public void OfferWindow_OutsideTheBounds_FailsValidation(string window)
        {
            // Arrange
            var options = new NearbyOptions
            {
                ServiceId = "test-service",
                OfferWindow = window switch
                {
                    "zero" => TimeSpan.Zero,
                    "negative" => TimeSpan.FromSeconds(-1),
                    "infinite" => Timeout.InfiniteTimeSpan,
                    _ => OfferWindowPolicy.s_max + TimeSpan.FromSeconds(1),
                },
            };

            // Act
            var ex = Assert.Throws<ArgumentException>(() => NearbyOptionsValidator.Validate(options));

            // Assert
            Assert.Contains(nameof(NearbyOptions.OfferWindow), ex.Message);
        }

        [Fact]
        public void OfferWindow_AtTheBound_PassesValidation()
        {
            // Arrange
            var options = new NearbyOptions
            {
                ServiceId = "test-service",
                OfferWindow = OfferWindowPolicy.s_max,
            };

            // Assert
            NearbyOptionsValidator.Validate(options);
        }
    }
}
