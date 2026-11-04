using NSubstitute;

namespace Harness.Tests;

public sealed class ProcessDefinitionReaderTests : IDisposable
{
    const string NotJson = "{";

    readonly TempRepo _repo = TempRepo.Create();

    ProcessDefinitionReader Reader => new(new Git(new ProcessRunner(), _repo.Root));

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task FindAsync_DefinitionEditedInWorkingTree_ReadsItFromTheRef()
    {
        // Arrange
        _repo.Write("harness.json", DefinitionTests.Valid);
        _repo.Commit("definition");
        _repo.Write("harness.json", NotJson);

        // Act
        var definition = await Reader.FindAsync("main", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Mobile App", definition?.Boards?.Project);
    }

    [Fact]
    public async Task FindAsync_NoDefinitionOnTheRef_ReturnsNull()
    {
        // Arrange
        _repo.Write("README.md", "no definition");
        _repo.Commit("base");

        // Act
        var definition = await Reader.FindAsync("main", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(definition);
    }

    [Fact]
    public async Task FindAsync_SameRefTwice_ReturnsTheFirstRead()
    {
        // Arrange
        var git = Substitute.For<IGit>();
        git.ReadFileAsync("origin/main", "harness.json", Arg.Any<CancellationToken>())
            .Returns(DefinitionTests.Valid, NotJson);
        var reader = new ProcessDefinitionReader(git);
        var first = await reader.FindAsync("origin/main", TestContext.Current.CancellationToken);

        // Act
        var second = await reader.FindAsync("origin/main", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(first, second);
    }
}
