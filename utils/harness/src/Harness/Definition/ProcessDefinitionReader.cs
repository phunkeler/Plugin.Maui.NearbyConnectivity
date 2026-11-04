namespace Harness;

/// <summary>Reads <c>harness.json</c> as committed on a ref, never from the working tree.</summary>
/// <param name="git">The repository to read from.</param>
sealed class ProcessDefinitionReader(IGit git)
{
    // Each ref's definition, read once; null when the ref has no harness.json.
    readonly Dictionary<string, ProcessDefinition?> _definitions = new(StringComparer.Ordinal);

    /// <summary>Finds and validates the definition on a ref, reading the ref once.</summary>
    /// <param name="gitRef">The ref, such as <c>origin/main</c>.</param>
    /// <param name="cancellationToken">Cancels the git calls.</param>
    /// <returns>The validated definition; <see langword="null"/> when the ref has no <c>harness.json</c>.</returns>
    /// <exception cref="UsageException">The ref does not exist, or the document is not valid.</exception>
    public async Task<ProcessDefinition?> FindAsync(string gitRef, CancellationToken cancellationToken)
    {
        if (!_definitions.TryGetValue(gitRef, out var definition))
        {
            var json = await git.ReadFileAsync(gitRef, ProcessDefinition.FileName, cancellationToken);
            _definitions[gitRef] = definition = json is null ? null : ProcessDefinition.Parse(json);
        }

        return definition;
    }
}
