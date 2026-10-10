using Microsoft.Maui;

namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Provides the application-wide <see cref="INearbyConnectivity"/> instance for code that does not
/// use dependency injection.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>Geolocation.Default</c>, <c>Connectivity.Default</c> and the rest of .NET MAUI
/// Essentials. Applications that do use dependency injection should call
/// <see cref="NearbyConnectivityServiceCollectionExtensions.AddNearbyConnectivity"/>
/// and inject <see cref="INearbyConnectivity"/> instead. It is the same instance either way, so
/// the two never disagree.
/// </para>
/// <para>
/// <b>Configure before first use.</b> <see cref="NearbyOptions.ServiceId"/> is required and has no
/// default, so reading <see cref="Default"/> without having registered the service through the
/// container throws. Call
/// <see cref="NearbyConnectivityServiceCollectionExtensions.AddNearbyConnectivity"/>
/// in <c>MauiProgram</c>, which every MAUI application has, and this property resolves from it.
/// </para>
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "The root type is named after the plugin, the same way Plugin.Maui.* packages name " +
        "theirs.")]
public static class NearbyConnectivity
{
    /// <summary>
    /// Gets the application-wide instance.
    /// </summary>
    /// <value>The same singleton the dependency injection container hands out.</value>
    /// <exception cref="InvalidOperationException">
    /// Nearby connectivity has not been registered. Call
    /// <see cref="NearbyConnectivityServiceCollectionExtensions.AddNearbyConnectivity"/>
    /// in <c>MauiProgram</c> first.
    /// </exception>
    public static INearbyConnectivity Default =>
        IPlatformApplication.Current?.Services?.GetService<INearbyConnectivity>()
            ?? throw new InvalidOperationException(
                $"Nearby connectivity has not been registered. Call " +
                $"{nameof(NearbyConnectivityAppBuilderExtensions.AddNearbyConnectivity)} in MauiProgram, " +
                $"or inject {nameof(INearbyConnectivity)} instead of using this property.");
}
