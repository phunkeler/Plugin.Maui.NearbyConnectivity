namespace Harness;

/// <summary>
/// The GitHub records of the current repository: its pull requests. A missing pull request is a
/// <see cref="RecordNotFoundException"/>; any other failure is a <see cref="RecordUnavailableException"/>.
/// </summary>
interface IGitHubRecords
{
    /// <summary>Reads the repository's name.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><c>owner/name</c>, read once and then reused.</returns>
    Task<string> ReadRepositoryNameAsync(CancellationToken cancellationToken);

    /// <summary>Reads a pull request.</summary>
    /// <param name="record">The pull request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The pull request.</returns>
    /// <exception cref="UsageException">The reference names another repository than the current one.</exception>
    Task<PullRequest> ReadPullRequestAsync(RecordRef record, CancellationToken cancellationToken);
}
