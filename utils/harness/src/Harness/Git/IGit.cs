using System.Collections.Immutable;

namespace Harness;

/// <summary>
/// A git repository, read at its top level. A failed git command is a <see cref="UsageException"/>: a ref or path the
/// user named does not exist.
/// </summary>
interface IGit
{
    /// <summary>Finds the repository's top level. Every other member runs git there.</summary>
    /// <param name="cancellationToken">Cancels the git call.</param>
    /// <returns>The full path of the top level, found once and then reused.</returns>
    /// <exception cref="UsageException">The working directory is not inside a git repository.</exception>
    Task<string> FindRootAsync(CancellationToken cancellationToken);

    /// <summary>Reads a file as committed on a revision.</summary>
    /// <param name="revision">The revision.</param>
    /// <param name="path">The file's path from the top level.</param>
    /// <param name="cancellationToken">Cancels the git calls.</param>
    /// <returns>The file's text; <see langword="null"/> when the path does not exist at the revision.</returns>
    /// <exception cref="UsageException">The revision does not exist.</exception>
    Task<string?> ReadFileAsync(string revision, string path, CancellationToken cancellationToken);

    /// <summary>Finds a branch as fetched from a remote, so that a branch name a pull request gives can be read.</summary>
    /// <param name="branch">The branch name, such as <c>main</c>.</param>
    /// <param name="cancellationToken">Cancels the git calls.</param>
    /// <returns>
    /// The remote-tracking ref, such as <c>origin/main</c>: <c>origin</c>'s when it has the branch, else the one
    /// remote that has it.
    /// </returns>
    /// <exception cref="UsageException">
    /// No remote has the branch, or several remotes other than <c>origin</c> have it.
    /// </exception>
    Task<string> FindRemoteBranchAsync(string branch, CancellationToken cancellationToken);

    /// <summary>Finds the best common ancestor of two revisions.</summary>
    /// <param name="firstRevision">One revision.</param>
    /// <param name="secondRevision">The other revision.</param>
    /// <param name="cancellationToken">Cancels the git call.</param>
    /// <returns>The merge base's commit id.</returns>
    Task<string> FindMergeBaseAsync(string firstRevision, string secondRevision, CancellationToken cancellationToken);

    /// <summary>Lists the paths that changed between two revisions.</summary>
    /// <param name="fromRevision">The older revision.</param>
    /// <param name="toRevision">The newer revision.</param>
    /// <param name="cancellationToken">Cancels the git call.</param>
    /// <returns>The changed paths, with renames detected.</returns>
    Task<ImmutableArray<ChangedPath>> ListChangedPathsAsync(
        string fromRevision, string toRevision, CancellationToken cancellationToken);

    /// <summary>Lists every file in a revision's tree.</summary>
    /// <param name="revision">The revision.</param>
    /// <param name="cancellationToken">Cancels the git call.</param>
    /// <returns>The files with their sizes, in git's order; submodules left out.</returns>
    /// <exception cref="UsageException">The revision does not exist.</exception>
    Task<ImmutableArray<TreeFile>> ListFilesAsync(string revision, CancellationToken cancellationToken);

    /// <summary>Finds the text files on a revision, under some globs, that contain any of some words.</summary>
    /// <param name="revision">The revision.</param>
    /// <param name="words">The words, matched literally anywhere in a line; none finds nothing.</param>
    /// <param name="pathGlobs">The globs, from the top level, that limit the search; none searches nothing.</param>
    /// <param name="cancellationToken">Cancels the git call.</param>
    /// <returns>The paths from the top level, in git's order; binary files left out.</returns>
    /// <exception cref="UsageException">The revision does not exist.</exception>
    Task<ImmutableArray<string>> FindFilesContainingAsync(
        string revision,
        IReadOnlyCollection<string> words,
        IReadOnlyCollection<string> pathGlobs,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads files as committed on a revision, without a git call per file, leaving out any file over a size.
    /// </summary>
    /// <param name="revision">The revision.</param>
    /// <param name="paths">The paths from the top level.</param>
    /// <param name="maxBytes">The largest file to read; a larger one is left out, as a missing one is.</param>
    /// <param name="cancellationToken">Cancels the git calls.</param>
    /// <returns>The text of each path that exists at the revision and is no larger than the limit, by path.</returns>
    /// <exception cref="UsageException">The revision does not exist.</exception>
    Task<ImmutableDictionary<string, string>> ReadFilesAsync(
        string revision, IReadOnlyList<string> paths, long maxBytes, CancellationToken cancellationToken);
}
