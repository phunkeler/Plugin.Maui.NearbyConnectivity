using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Hosting;

namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Provides extension methods for adding nearby connectivity to an
/// <see cref="IServiceCollection"/>.
/// </summary>
public static partial class NearbyConnectivityServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="INearbyConnectivity"/> and its configuration with the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="configure">
    /// A delegate that configures <see cref="NearbyOptions"/>. It must set
    /// <see cref="NearbyOptions.ServiceId"/>, which has no default.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance, so that multiple calls can be chained.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <see cref="INearbyConnectivity"/> is registered as a singleton, because the underlying
    /// resources are singular: one radio and one native session per device. It is constructed
    /// lazily, on first resolution.
    /// </para>
    /// <para>
    /// <paramref name="configure"/> is applied and validated synchronously, before this method
    /// returns, so an unusable <see cref="NearbyOptions.ServiceId"/> fails at the call site.
    /// The validated snapshot is registered as a singleton <see cref="NearbyOptions"/>. The
    /// <paramref name="configure"/> delegate is the only way to supply options: configuration
    /// binding and <c>IOptions&lt;T&gt;</c> do not apply.
    /// </para>
    /// <para>
    /// The first registration wins. A second call still validates its options, then registers
    /// nothing, so its <see cref="NearbyOptions.ServiceId"/> and every other option are ignored.
    /// </para>
    /// <para>
    /// A MAUI application calls
    /// <see cref="NearbyConnectivityAppBuilderExtensions.AddNearbyConnectivity(MauiAppBuilder, Action{NearbyOptions})"/>
    /// on the builder instead, which forwards to this method. This method is for a host that has no
    /// <see cref="MauiAppBuilder"/>, such as a unit test.
    /// </para>
    /// <para>
    /// Logging is optional. A host that never called <c>AddLogging()</c> still works, with log
    /// output discarded. A <see cref="TimeProvider"/> is registered here if the host has not
    /// registered one.
    /// </para>
    /// <para>
    /// Neither advertising nor discovery starts automatically. Call
    /// <see cref="INearbyActivity.StartAsync(CancellationToken)"/> on
    /// <see cref="INearbyConnectivity.Advertiser"/> or <see cref="INearbyConnectivity.Discoverer"/>
    /// once the application is ready and <see cref="NearbyPermission"/> has been granted.
    /// </para>
    /// <para>
    /// An application that resolves this service late still sees every connection already open and
    /// every request still outstanding, in <see cref="INearbyConnectivity.Devices"/>.
    /// </para>
    /// </remarks>
    /// <seealso href="https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/getting-started.html">Get started</seealso>
    /// <example>
    /// The following example registers the plugin on a bare service collection and sets the
    /// service identifier.
    /// <code language="csharp">
    /// services.AddNearbyConnectivity(options =>
    /// {
    ///     options.ServiceId = "nearbychat";
    /// });
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The configured <see cref="NearbyOptions"/> is unusable. Leaving
    /// <see cref="NearbyOptions.ServiceId"/> unset throws, and the message suggests a valid
    /// identifier derived from the application's own name. A malformed value throws too: empty,
    /// longer than 15 characters, containing anything but ASCII lowercase letters, digits and
    /// hyphens, or written in the <c>_name._tcp</c> Bonjour form.
    /// </exception>
    public static IServiceCollection AddNearbyConnectivity(
        this IServiceCollection services,
        Action<NearbyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new NearbyOptions();
        configure(options);
        NearbyOptionsValidator.Validate(options);

        options = options.Snapshot();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(options);
        services.TryAddSingleton<INearbyConnectivity>(sp =>
        {
            var registered = sp.GetRequiredService<NearbyOptions>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            var logger = sp.GetService<ILogger<INearbyConnectivity>>()
                ?? (ILogger)NullLogger<INearbyConnectivity>.Instance;
            var nearbyTransport = new NearbyTransport(
                timeProvider,
                registered,
                logger,
                new PeerLookup { Logger = logger },
                CreateAdapter);

            return new NearbySession(
                nearbyTransport,
                logger,
                registered,
                timeProvider);
        });

        return services;
    }

    private static partial IPlatformAdapter CreateAdapter(NearbyTransport transport);
}
