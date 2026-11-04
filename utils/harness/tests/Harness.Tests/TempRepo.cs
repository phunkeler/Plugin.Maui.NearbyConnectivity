using System.Diagnostics;

namespace Harness.Tests;

sealed class TempRepo : IDisposable
{
    TempRepo(string root) => Root = root;

    public string Root { get; }

    readonly List<string> _remotes = [];

    public static TempRepo Create()
    {
        var repo = new TempRepo(Directory.CreateTempSubdirectory("harness-repo-").FullName);
        repo.Git("init -q -b main");
        repo.Git("config user.name Test");
        repo.Git("config user.email test@example.com");
        repo.Git("config commit.gpgsign false");
        return repo;
    }

    public void Write(string path, string text, bool executable = false)
    {
        var full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        if (executable && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                full,
                File.GetUnixFileMode(full)
                    | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
    }

    public string Commit(string message)
    {
        Git("add -A");
        Git($"commit -q -m \"{message}\"");
        return Git("rev-parse HEAD").Trim();
    }

    public string Git(string arguments)
    {
        var start = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        var standardError = process.StandardError.ReadToEndAsync();
        var standardOutput = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0
            ? standardOutput
            : throw new InvalidOperationException($"git {arguments}: {standardError.Result}");
    }

    // Adds a bare repository as origin and pushes every branch, so origin/<branch> exists.
    public void AddOrigin() => AddRemote("origin");

    // Adds a bare repository under the name and pushes every branch, so <name>/<branch> exists.
    public void AddRemote(string name)
    {
        var remote = Directory.CreateTempSubdirectory("harness-remote-").FullName;
        _remotes.Add(remote);
        Git($"init -q --bare \"{remote}\"");
        Git($"remote add {name} \"{remote}\"");
        Git($"push -q {name} --all");
    }

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
        foreach (var remote in _remotes)
        {
            Directory.Delete(remote, recursive: true);
        }
    }
}
