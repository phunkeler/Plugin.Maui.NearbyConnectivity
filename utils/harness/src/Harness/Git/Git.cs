using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Harness;

/// <summary>The git repository that contains a working directory, through the git command line.</summary>
/// <param name="processRunner">Runs git.</param>
/// <param name="workingDirectory">A directory inside the repository.</param>
sealed class Git(IProcessRunner processRunner, string workingDirectory) : IGit
{
    string? _root;

    // Diff paths are relative to the top level, so git runs at the top.
    public async Task<string> FindRootAsync(CancellationToken cancellationToken)
    {
        if (_root is null)
        {
            var (exitCode, stdout, stderr) = await processRunner
                .RunAsync("git", ["rev-parse", "--show-toplevel"], workingDirectory, null, cancellationToken);
            _root = exitCode == 0
                ? Path.GetFullPath(stdout.Trim())
                : throw new UsageException($"git rev-parse: {stderr.Trim()}");
        }

        return _root;
    }

    public async Task<string?> ReadFileAsync(string revision, string path, CancellationToken cancellationToken)
    {
        var root = await FindRootAsync(cancellationToken);
        var (exitCode, stdout, _) = await processRunner
            .RunAsync("git", ["cat-file", "blob", $"{revision}:{path}"], root, null, cancellationToken);
        if (exitCode == 0)
        {
            return stdout;
        }

        await RunAsync(["rev-parse", "--verify", $"{revision}^{{tree}}"], cancellationToken);
        return null;
    }

    // `git remote` names the remotes; `git for-each-ref refs/remotes/` lists every remote-tracking ref in full, so a
    // branch with a slash in its name and a remote with one are told apart by the remote list, not by the slash.
    public async Task<string> FindRemoteBranchAsync(string branch, CancellationToken cancellationToken)
    {
        var remotes = Lines(await RunAsync(["remote"], cancellationToken));
        var refs = Lines(await RunAsync(
                ["for-each-ref", "--format=%(refname:lstrip=2)", "refs/remotes/"], cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var remotesWithBranch = remotes.Where(remote => refs.Contains($"{remote}/{branch}")).ToList();

        return remotesWithBranch switch
        {
            [] => throw new UsageException($"branch {branch} is not fetched from any remote; fetch it first"),
            _ when remotesWithBranch.Contains("origin", StringComparer.Ordinal) => $"origin/{branch}",
            [var remote] => $"{remote}/{branch}",
            _ => throw new UsageException(
                $"branch {branch} is on several remotes ({string.Join(", ", remotesWithBranch)}) and none is origin"),
        };
    }

    public async Task<string> FindMergeBaseAsync(
        string firstRevision, string secondRevision, CancellationToken cancellationToken) =>
        (await RunAsync(["merge-base", firstRevision, secondRevision], cancellationToken))
            .Trim();

    public async Task<ImmutableArray<ChangedPath>> ListChangedPathsAsync(
        string fromRevision, string toRevision, CancellationToken cancellationToken) =>
        ParseNameStatus(
            await RunAsync(["diff", "--name-status", "-M", "-z", fromRevision, toRevision, "--"], cancellationToken));

    // `git ls-tree -r -l -z --full-tree`: "<mode> <type> <object> <size>\t<path>" per entry, the size padded.
    public async Task<ImmutableArray<TreeFile>> ListFilesAsync(string revision, CancellationToken cancellationToken)
    {
        var output = await RunAsync(["ls-tree", "-r", "-l", "-z", "--full-tree", revision], cancellationToken);
        var files = ImmutableArray.CreateBuilder<TreeFile>();
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t', StringComparison.Ordinal);
            var fields = entry[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields is [_, "blob", _, var size])
            {
                files.Add(new(entry[(tab + 1)..], long.Parse(size, NumberStyles.None, CultureInfo.InvariantCulture)));
            }
        }

        return files.ToImmutable();
    }

    // `git grep -l -I -z -F -e <word>... <revision> -- :(glob)<glob>...`: one "<revision>:<path>" per file. Exit code
    // 1 is no match, not a failure.
    public async Task<ImmutableArray<string>> FindFilesContainingAsync(
        string revision,
        IReadOnlyCollection<string> words,
        IReadOnlyCollection<string> pathGlobs,
        CancellationToken cancellationToken)
    {
        if (words.Count == 0 || pathGlobs.Count == 0)
        {
            return [];
        }

        var root = await FindRootAsync(cancellationToken);
        var (exitCode, stdout, stderr) = await processRunner
            .RunAsync(
                "git",
                [
                    "grep", "-l", "-I", "-z", "-F", .. words.SelectMany(word => new[] { "-e", word }), revision, "--",
                    .. pathGlobs.Select(glob => ":(glob)" + glob),
                ],
                root,
                null,
                cancellationToken);
        return exitCode switch
        {
            0 =>
            [
                .. stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.StartsWith(revision + ":", StringComparison.Ordinal)
                        ? line[(revision.Length + 1)..]
                        : line),
            ],
            1 => [],
            _ => throw new UsageException($"git grep: {stderr.Trim()}"),
        };
    }

    // `git cat-file --batch`: for each "<revision>:<path>" line, "<object> blob <size>\n", the content and "\n", or
    // "<name> missing\n". The size counts bytes, so the output is walked as UTF-8 bytes. Text that is not UTF-8 changes
    // its byte count when decoded; from the first entry that no longer lines up, and for a path with a newline, which
    // the batch input cannot carry, each file is read on its own. Sizes come first from `--batch-check`, so a file
    // over the limit is never read; a path with a newline cannot be checked, so it is read whatever its size.
    public async Task<ImmutableDictionary<string, string>> ReadFilesAsync(
        string revision, IReadOnlyList<string> paths, long maxBytes, CancellationToken cancellationToken)
    {
        var files = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        List<string> batch = [.. paths.Where(path => !path.Contains('\n', StringComparison.Ordinal))];
        List<string> single = [.. paths.Where(path => path.Contains('\n', StringComparison.Ordinal))];
        if (batch.Count > 0)
        {
            batch = await WithinAsync(revision, batch, maxBytes, cancellationToken);
        }

        if (batch.Count > 0)
        {
            var stdout = await CatFileBatchAsync(revision, "--batch", batch, cancellationToken);
            var bytes = Encoding.UTF8.GetBytes(stdout);
            var at = 0;
            var read = 0;
            for (; read < batch.Count; read++)
            {
                var end = Array.IndexOf(bytes, (byte)'\n', at);
                if (end < 0)
                {
                    break;
                }

                var header = Encoding.UTF8.GetString(bytes, at, end - at);
                if (header.EndsWith(" missing", StringComparison.Ordinal))
                {
                    at = end + 1;
                    continue;
                }

                var fields = header.Split(' ');
                if (fields is not [_, "blob", var sizeText]
                    || !int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                    || end + 1 + size >= bytes.Length
                    || bytes[end + 1 + size] != (byte)'\n')
                {
                    break;
                }

                // A byte that was not UTF-8 came back as U+FFFD, three bytes for one, so the walk has drifted even
                // when a newline happens to sit where the entry should end.
                var text = Encoding.UTF8.GetString(bytes, end + 1, size);
                if (text.Contains('�', StringComparison.Ordinal))
                {
                    break;
                }

                files[batch[read]] = text;
                at = end + 1 + size + 1;
            }

            single.AddRange(batch.Skip(read));
        }

        foreach (var path in single)
        {
            if (await ReadFileAsync(revision, path, cancellationToken) is { } text)
            {
                files[path] = text;
            }
        }

        return files.ToImmutable();
    }

    // `git cat-file --batch-check`: one line per input line, in order: "<object> <type> <size>" or "<name> missing".
    // Only a blob within the limit is kept; a missing path, a directory and a larger file drop out here.
    async Task<List<string>> WithinAsync(
        string revision, List<string> paths, long maxBytes, CancellationToken cancellationToken)
    {
        var stdout = await CatFileBatchAsync(revision, "--batch-check", paths, cancellationToken);
        var lines = Lines(stdout);
        if (lines.Length != paths.Count)
        {
            throw new InvalidOperationException(
                $"git cat-file --batch-check: {lines.Length} lines for {paths.Count} paths");
        }

        return
        [
            .. paths.Zip(lines)
                .Where(entry => entry.Second.Split(' ') is [_, "blob", var sizeText]
                    && long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                    && size <= maxBytes)
                .Select(entry => entry.First),
        ];
    }

    async Task<string> CatFileBatchAsync(
        string revision, string mode, List<string> paths, CancellationToken cancellationToken)
    {
        var root = await FindRootAsync(cancellationToken);
        var (exitCode, stdout, stderr) = await processRunner
            .RunAsync(
                "git",
                ["cat-file", mode],
                root,
                string.Concat(paths.Select(path => $"{revision}:{path}\n")),
                cancellationToken);
        return exitCode == 0 ? stdout : throw new UsageException($"git cat-file: {stderr.Trim()}");
    }

    static string[] Lines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // `git diff --name-status -M -z`: a status token, then one path, or two (old, new) for a rename.
    public static ImmutableArray<ChangedPath> ParseNameStatus(string nameStatusOutput)
    {
        var parts = nameStatusOutput.Split('\0');
        var changed = ImmutableArray.CreateBuilder<ChangedPath>();
        for (var i = 0; i < parts.Length && parts[i].Length > 0; i += 2)
        {
            var status = parts[i];
            if (status[0] == 'R')
            {
                changed.Add(new(ChangeKind.Renamed, parts[i + 2]));
                i++;
                continue;
            }

            var kind = status switch
            {
                "A" => ChangeKind.Added,
                "M" or "T" => ChangeKind.Modified,
                "D" => ChangeKind.Deleted,
                _ => throw new InvalidOperationException($"git diff: unexpected status '{status}'"),
            };
            changed.Add(new(kind, parts[i + 1]));
        }

        return changed.ToImmutable();
    }

    async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var root = await FindRootAsync(cancellationToken);
        var (exitCode, stdout, stderr) = await processRunner
            .RunAsync("git", arguments, root, null, cancellationToken);
        return exitCode == 0 ? stdout : throw new UsageException($"git {arguments[0]}: {stderr.Trim()}");
    }
}
