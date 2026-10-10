using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Options")]
public class ServiceCollectionExtensionsTests
{
    public sealed class AddNearbyConnectivity : ServiceCollectionExtensionsTests
    {
        [Fact]
        public void AZeroRefreshInterval_FailsAtRegistration()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => services.AddNearbyConnectivity(o =>
            {
                o.ServiceId = "svc";
                o.DiscoveryRefreshInterval = TimeSpan.Zero;
            }));
        }

        [Fact]
        public void RegisteredOptions_AreResolvableAsNearbyOptions()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddNearbyConnectivity(o => o.ServiceId = "svc");

            // Act
            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<NearbyOptions>();

            // Assert
            // The values this registration validated, not a default instance.
            Assert.Equal("svc", options.ServiceId);
        }

        [Fact]
        public async Task NoLoggingRegistered_ResolvesAndDiscardsLogOutput()
        {
            // Logging is optional. A host that never called AddLogging() — a consumer's bare
            // ServiceCollection in a unit test, for instance — still resolves a working session;
            // the plugin falls back to NullLogger rather than failing on a diagnostic concern.

            // Arrange
            var services = new ServiceCollection();
            services.AddNearbyConnectivity(options => options.ServiceId = "test-service");
            await using var provider = services.BuildServiceProvider();

            // Act
            var connectivity = provider.GetRequiredService<INearbyConnectivity>();

            // Assert
            Assert.NotNull(connectivity);
        }

        [Fact]
        public async Task ResolvedTwice_ReturnsTheSameInstance()
        {
            // One radio, one native session — the singleton lifetime is platform-forced, not a
            // preference. Two instances would mean two MCSession/Nearby clients fighting over it.

            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddNearbyConnectivity(options => options.ServiceId = "test-service");

            // Act
            await using var provider = services.BuildServiceProvider();

            // Assert
            Assert.Same(
                provider.GetRequiredService<INearbyConnectivity>(),
                provider.GetRequiredService<INearbyConnectivity>());
        }

        [Fact]
        public void Always_DoesNotRegisterLoggingInfrastructure()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddNearbyConnectivity(options => options.ServiceId = "test-service");

            // Assert
            Assert.DoesNotContain(services, d => d.ServiceType == typeof(ILoggerFactory));
        }

        [Fact]
        public void EmptyServiceId_FailsValidationImmediately()
        {
            // NearbyOptionsValidator is covered in isolation elsewhere; this covers that AddNearbyConnectivity
            // actually calls it. Validation runs synchronously inside AddNearbyConnectivity itself — not through
            // the Microsoft.Extensions.Options pipeline, which MAUI apps never trigger (there is no
            // IHost.StartAsync) — so the failure must surface from this call, not from a later
            // resolution.
            //
            // Empty rather than malformed: the character rules live in ServiceIdRules, which
            // NearbyOptionsValidator applies on every target, so an empty id and a malformed one
            // fail the same way here. Empty is the shorter case to write.

            // Arrange
            var services = new ServiceCollection();

            // Act
            var failure = Assert.Throws<ArgumentException>(
                () => services.AddNearbyConnectivity(options => options.ServiceId = ""));

            // Assert
            Assert.Contains("ServiceId", failure.Message);
        }

        [Fact]
        public void NullConfigureDelegate_Throws()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => services.AddNearbyConnectivity(null!));
        }

        [Fact]
        public async Task ConsumerRegisteredItsOwn_TryAddSingletonLeavesItAlone()
        {
            // TryAddSingleton, not AddSingleton: an app that supplies its own INearbyConnectivity — a fake in a
            // UI test, say — must keep it.

            // Arrange
            var services = new ServiceCollection();
            var stub = Substitute.For<INearbyConnectivity>();
            services.AddSingleton<INearbyConnectivity>(stub);

            // Act
            services.AddNearbyConnectivity(options => options.ServiceId = "test-service");

            // Assert
            await using var provider = services.BuildServiceProvider();
            Assert.Same(stub, provider.GetRequiredService<INearbyConnectivity>());
        }

        [Fact]
        public async Task TimeProviderRegistered_TheSessionUsesItRatherThanTheSystemClock()
        {
            // The session falls back to TimeProvider.System, so a registered provider being ignored
            // would leave every timeout un-fakeable and silently wall-clock bound.

            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Registered before AddNearbyConnectivity: AddNearbyConnectivity supplies TimeProvider.System via
            // TryAddSingleton, so a consumer's own provider must already be in the collection to
            // win. Registering one afterwards is too late.
            var clock = new ConstructionWitness();
            services.AddSingleton<TimeProvider>(clock.Resolve);

            services.AddNearbyConnectivity(options => options.ServiceId = "test-service");

            await using var provider = services.BuildServiceProvider();

            // Act
            _ = provider.GetRequiredService<INearbyConnectivity>();

            // Assert
            Assert.True(clock.WasResolved, "A registered TimeProvider must win over TimeProvider.System.");
        }

        [Fact]
        public async Task OptionsRegistered_TheSessionUsesTheContainerCopy()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(new NearbyOptions
            {
                ServiceId = "mine",
                Presence = new() { DisplayName = "Mine" },
            });
            services.AddNearbyConnectivity(options => options.ServiceId = "theirs");
            await using var provider = services.BuildServiceProvider();

            // Act
            var connectivity = provider.GetRequiredService<INearbyConnectivity>();

            // Assert
            Assert.Equal("Mine", connectivity.Presence.DisplayName);
        }

        [Fact]
        public void CalledTwice_DoesNotThrow()
        {
            // AddNearbyConnectivity has no MAUI-lifecycle registration left to duplicate: it registers only
            // INearbyConnectivity itself, via TryAddSingleton, so calling it twice is a harmless no-op on the
            // second call rather than a duplicate-registration error.

            // Arrange
            var services = new ServiceCollection();

            // Act & Assert
            services.AddNearbyConnectivity(options => options.ServiceId = "test-service");
            services.AddNearbyConnectivity(options => options.ServiceId = "test-service");
        }
    }
}
