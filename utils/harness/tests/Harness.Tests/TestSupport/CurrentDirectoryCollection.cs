namespace Harness.Tests;

// Tests that move the process's current directory run while no other test runs: a bare `git` in another test
// would otherwise be looked up in the moved directory.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CurrentDirectoryCollection
{
    public const string Name = "process current directory";
}
