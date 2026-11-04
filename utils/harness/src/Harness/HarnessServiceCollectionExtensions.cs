using Harness;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the harness on an <see cref="IServiceCollection"/>.</summary>
static class HarnessServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="Change"/> and the GitHub, git, Azure Boards and process services it uses.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the <see cref="HarnessOptions"/>; <see langword="null"/> keeps every default.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks>
    /// Registers logging; the host chooses its providers. A service is registered only if the host has not registered
    /// one. <see cref="Change"/> and its readers are scoped: a host creates one scope per run, and a definition or a
    /// work item read in that run is cached for the run. Nothing runs until a service is resolved and used: the
    /// current directory and the environment variables are read on resolution, and the repository's top level is
    /// found on the first git call.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddHarness(
        this IServiceCollection services, Action<HarnessOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<HarnessOptions>()
            .Validate(options => options.ProcessTimeout > TimeSpan.Zero, "ProcessTimeout must be positive");
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ToolCalls>();
        services.TryAddSingleton<IProcessRunner>(provider => new ObservedProcessRunner(
            new ProcessRunner(provider.GetRequiredService<IOptions<HarnessOptions>>().Value.ProcessTimeout),
            provider.GetRequiredService<ToolCalls>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ObservedProcessRunner>>()));
        services.TryAddSingleton<IGitHubRecords, GitHubRecords>();
        services.TryAddSingleton<IGit>(provider => new Git(
            provider.GetRequiredService<IProcessRunner>(),
            provider.GetRequiredService<IOptions<HarnessOptions>>().Value.WorkingDirectory
                ?? Directory.GetCurrentDirectory()));
        services.TryAddSingleton(_ => new BoardsEnvironment(
            Organization: Environment.GetEnvironmentVariable("ADO_ORG"),
            Project: Environment.GetEnvironmentVariable("ADO_PROJECT")));
        services.TryAddSingleton<IWorkItemsFactory, AzWorkItemsFactory>();

        services.TryAddScoped<ChainBuilder>();
        services.TryAddScoped<ProcessDefinitionReader>();
        services.TryAddScoped<TestImpactReader>();
        services.TryAddScoped<Change>();
        return services;
    }
}
