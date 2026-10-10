using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Options")]
public sealed class AppBuilderExtensionsTests
{
    [Fact]
    public async Task AddNearbyConnectivity_RegistersTheServiceAndReturnsTheBuilder()
    {
        // Arrange
        var builder = MauiApp.CreateBuilder(useDefaults: false);

        // Act
        var returned = builder.AddNearbyConnectivity(options => options.ServiceId = "svc");
        await using var app = builder.Build();

        // Assert
        Assert.Same(builder, returned);
        Assert.NotNull(app.Services.GetRequiredService<INearbyConnectivity>());
        Assert.Equal("svc", app.Services.GetRequiredService<NearbyOptions>().ServiceId);
    }

    [Fact]
    public void AddNearbyConnectivity_NullBuilder_Throws()
    {
        // Arrange
        MauiAppBuilder builder = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.AddNearbyConnectivity(o => o.ServiceId = "svc"));
    }
}
