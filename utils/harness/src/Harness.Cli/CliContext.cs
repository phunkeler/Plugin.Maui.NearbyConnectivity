using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Harness.Cli;

/// <summary>
/// What every command uses: how to build the services, the two output streams and the global options.
/// </summary>
/// <param name="CreateServices">
/// Builds the harness services at a minimum log level. A command calls it once when it runs, through
/// <see cref="ServicesFor"/>, and disposes the result when it ends.
/// </param>
/// <param name="Stdout">Receives the result only.</param>
/// <param name="Stderr">Receives errors. Logs go to the process's stderr.</param>
/// <param name="Json">The global <c>--json</c> option.</param>
/// <param name="Verbose">The global <c>--verbose</c> option.</param>
sealed record CliContext(
    Func<LogLevel, ServiceProvider> CreateServices,
    TextWriter Stdout,
    TextWriter Stderr,
    Option<bool> Json,
    Option<bool> Verbose)
{
    /// <summary>Builds the services for one run, at the log level its command line asks for.</summary>
    /// <param name="parse">The parsed command line.</param>
    /// <returns>
    /// The services, logging from <see cref="LogLevel.Debug"/> with <c>--verbose</c>, else from
    /// <see cref="LogLevel.Information"/>. The caller disposes them.
    /// </returns>
    public ServiceProvider ServicesFor(ParseResult parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        return CreateServices(parse.GetValue(Verbose) ? LogLevel.Debug : LogLevel.Information);
    }
}
