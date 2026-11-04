using System.CommandLine;
using System.CommandLine.Help;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Harness.Cli;

/// <summary>The <c>harness</c> command line: the process, the grammar and the exit code of one invocation.</summary>
static class Program
{
    static Task<int> Main(string[] args)
    {
        // The report carries arrows and work item titles. On Windows the console's default code page cannot encode
        // them and writes '?' instead; Unix consoles are UTF-8 already.
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        return RunAsync(args, Console.Out, Console.Error, CreateServices, CancellationToken.None);
    }

    // The console logger writes to stdout unless every level is sent to stderr. AddConsole here only sets options on
    // the provider AddSimpleConsole registered. Disposing the provider flushes the logs.
    static ServiceProvider CreateServices(LogLevel minimumLevel) =>
        new ServiceCollection()
            .AddLogging(builder =>
            {
                builder.SetMinimumLevel(minimumLevel);
                builder.AddSimpleConsole(options => options.SingleLine = true);
                builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            })
            .AddHarness()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    /// <summary>Runs one invocation.</summary>
    /// <param name="args">The command line, without the program name.</param>
    /// <param name="stdout">Receives the result only.</param>
    /// <param name="stderr">Receives errors. Logs go to the process's stderr.</param>
    /// <param name="createServices">
    /// Builds the harness services for the working directory, at the minimum log level the command line asks for:
    /// <see cref="LogLevel.Debug"/> with <c>--verbose</c>, else <see cref="LogLevel.Information"/>. A command calls
    /// it when it runs, and disposes the services when it ends; <c>--help</c> and <c>--version</c> never call it.
    /// </param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        Func<LogLevel, ServiceProvider> createServices,
        CancellationToken cancellationToken)
    {
        var json = new Option<bool>("--json") { Description = "Print one line of compact JSON.", Recursive = true };
        var verbose = new Option<bool>("--verbose", "-v") { Description = "Write debug logs to stderr.", Recursive = true };
        var context = new CliContext(createServices, stdout, stderr, json, verbose);

        // RootCommand takes its name from the executable, Harness.Cli. The assembly cannot be named harness, because
        // it collides with Harness.dll beside it on a case-insensitive file system.
        var root = new Command("harness", "Reports what a pull request touches and what it is linked to.")
        {
            new HelpOption(),
            new VersionOption(),
            new ScopeCommand(context),
        };
        root.Options.Add(json);
        root.Options.Add(verbose);

        var parsed = root.Parse(args);
        if (parsed.Errors.Count > 0)
        {
            foreach (var error in parsed.Errors)
            {
                await stderr.WriteLineAsync(error.Message);
            }

            return ExitCodes.UsageError;
        }

        // System.CommandLine owns the signals: on SIGINT or SIGTERM it cancels the token, and exits 130 or 143 itself
        // only when the command has not stopped two seconds later. Its default exception handler would exit 1 for
        // everything; without it, what the command throws reaches the catch and gets its own code.
        var config = new InvocationConfiguration
        {
            Output = stdout,
            Error = stderr,
            EnableDefaultExceptionHandler = false,
        };
        try
        {
            return await parsed.InvokeAsync(config, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await stderr.WriteLineAsync("Cancelled.");
            return ExitCodes.Cancelled;
        }
        catch (Exception exception)
        {
            // Anything that is not a known failure is a defect, so its stack goes with it.
            await stderr.WriteLineAsync(
                ExitCodes.KnownCodeFor(exception) is not null
                    ? exception.Message
                    : $"harness bug: {exception}");
            return ExitCodes.FromException(exception);
        }
    }
}
