using Microsoft.Maui.Hosting;

namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Provides extension methods for adding nearby connectivity to a <see cref="MauiAppBuilder"/>.
/// </summary>
public static class NearbyConnectivityAppBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="INearbyConnectivity"/> and its configuration with the application.
    /// </summary>
    /// <param name="builder">The <see cref="MauiAppBuilder"/> to add the services to.</param>
    /// <param name="configure">
    /// A delegate that configures <see cref="NearbyOptions"/>. It must set
    /// <see cref="NearbyOptions.ServiceId"/>, which has no default.
    /// </param>
    /// <returns>The same <see cref="MauiAppBuilder"/> instance, so that calls can be chained.</returns>
    /// <remarks>
    /// This method forwards to
    /// <see cref="NearbyConnectivityServiceCollectionExtensions.AddNearbyConnectivity(IServiceCollection, Action{NearbyOptions})"/>
    /// on <see cref="MauiAppBuilder.Services"/>. See that method for the registration's lifetime,
    /// validation and logging behavior, and for what a second call does.
    /// </remarks>
    /// <example>
    /// The following example registers the plugin from <c>MauiProgram</c> and sets the service
    /// identifier.
    /// <code language="csharp">
    /// builder.AddNearbyConnectivity(options =>
    /// {
    ///     options.ServiceId = "nearbychat";
    /// });
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The configured <see cref="NearbyOptions"/> is unusable. See
    /// <see cref="NearbyConnectivityServiceCollectionExtensions.AddNearbyConnectivity(IServiceCollection, Action{NearbyOptions})"/>.
    /// </exception>
    public static MauiAppBuilder AddNearbyConnectivity(
        this MauiAppBuilder builder,
        Action<NearbyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddNearbyConnectivity(configure);
        return builder;
    }
}
