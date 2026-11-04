using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Harness;

/// <summary>Runs programs as child processes, each within a time bound.</summary>
/// <param name="timeout">How long one program may run before its process tree is killed.</param>
sealed class ProcessRunner(TimeSpan timeout) : IProcessRunner
{
    /// <summary>The bound the harness gives git and gh: long enough for a large repository, short of a hung job.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // PATH does not change while the harness runs, so each tool is looked up once.
    readonly ConcurrentDictionary<string, string> _resolved = new(StringComparer.Ordinal);

    /// <summary>Creates a runner with <see cref="DefaultTimeout"/>.</summary>
    public ProcessRunner()
        : this(DefaultTimeout)
    {
    }

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(timeout);
        try
        {
            return await RunBoundAsync(fileName, arguments, workingDirectory, standardInput, bound.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{fileName} {(arguments.Count > 0 ? arguments[0] : "")} did not finish within {timeout}", exception);
        }
    }

    async Task<ProcessResult> RunBoundAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        using var process = Start(fileName, arguments, workingDirectory);
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            try
            {
                if (standardInput is not null)
                {
                    await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
                }

                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The child exited before it read stdin, and its exit code and stderr say why. Only the base stream
                // closes, because flushing the writer would throw again.
                await process.StandardInput.BaseStream.DisposeAsync();
            }

            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    Process Start(string fileName, IEnumerable<string> arguments, string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo(_resolved.GetOrAdd(fileName, Resolve), arguments)
        {
            WorkingDirectory = workingDirectory ?? "",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = s_utf8,
            StandardOutputEncoding = s_utf8,
            StandardErrorEncoding = s_utf8,
        };

        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            return process;
        }
        catch (Win32Exception exception)
        {
            process.Dispose();
            throw new UsageException($"{fileName} cannot be started: {exception.Message}", exception);
        }
    }

    // Process.Start looks a bare name up in the harness's own current directory before PATH, and in CI that directory
    // is the checkout of the pull request, whose author may have added a file named git. Only PATH is searched here.
    // Windows also tries each PATHEXT extension, as the shell does, so gh finds gh.exe and az finds az.cmd. PATH is
    // read here rather than injected: the resolver must see the same variable the runtime would.
    static string Resolve(string fileName)
    {
        if (Path.IsPathRooted(fileName) || fileName.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return fileName;
        }

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        string[] extensions = OperatingSystem.IsWindows()
            ? ["", .. (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';')]
            : [""];
        var found = directories
            .SelectMany(directory => extensions.Select(extension => Path.Combine(directory, fileName + extension)))
            .FirstOrDefault(File.Exists);
        return found ?? throw new UsageException($"{fileName} is not installed or not on PATH");
    }
}
