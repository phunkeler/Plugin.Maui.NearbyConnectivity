using Microsoft.Extensions.DependencyInjection;

namespace Harness.Cli;

/// <summary>Resolves what a command runs on.</summary>
static class ServiceProviderExtensions
{
    /// <summary>
    /// Resolves the <see cref="Change"/> for the working directory, after finding the repository root.
    /// </summary>
    /// <param name="services">The command's service scope.</param>
    /// <param name="cancellationToken">Cancels the git call.</param>
    /// <returns>The <see cref="Change"/>.</returns>
    /// <exception cref="UsageException">
    /// The working directory is not in a git repository. The command fails this way before any GitHub call.
    /// </exception>
    public static async Task<Change> ResolveChangeAsync(
        this IServiceProvider services, CancellationToken cancellationToken)
    {
        var change = services.GetRequiredService<Change>();
        await services.GetRequiredService<IGit>().FindRootAsync(cancellationToken);
        return change;
    }
}
