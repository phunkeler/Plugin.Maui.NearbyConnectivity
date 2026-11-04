namespace Harness.Tests;

public sealed class GitTests : IDisposable
{
    // Larger than any file these tests commit.
    const long NoLimit = 1 << 20;

    readonly TempRepo _repo = TempRepo.Create();

    Git Git => new(new ProcessRunner(), _repo.Root);

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void ParseNameStatus_RenameAmongChanges_ReadsOldAndNewPaths()
    {
        // Arrange
        const string nameStatusOutput = "M\0a.cs\0R087\0old name.cs\0new name.cs\0D\0gone.cs\0";

        // Act
        var changed = Harness.Git.ParseNameStatus(nameStatusOutput);

        // Assert
        Assert.Equal(
            [
                new ChangedPath(ChangeKind.Modified, "a.cs"),
                new ChangedPath(ChangeKind.Renamed, "new name.cs"),
                new ChangedPath(ChangeKind.Deleted, "gone.cs"),
            ],
            changed);
    }

    [Fact]
    public async Task ListChangedPathsAsync_UnusualPathName_ReturnsTheExactPath()
    {
        // Arrange
        const string unusual = "tests/with space/Ä \"quoted\".cs";
        _repo.Write("base.txt", "base");
        _repo.Commit("base");
        _repo.Git("checkout -q -b work");
        _repo.Write(unusual, "class T;");
        _repo.Commit("add");
        var mergeBase = await Git.FindMergeBaseAsync("main", "HEAD", TestContext.Current.CancellationToken);

        // Act
        var changed = await Git.ListChangedPathsAsync(mergeBase, "HEAD", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([new ChangedPath(ChangeKind.Added, unusual)], changed);
    }

    [Fact]
    public async Task ReadFileAsync_PathMissingOnTheRevision_ReturnsNull()
    {
        // Arrange
        _repo.Write("a.txt", "one");
        var head = _repo.Commit("base");

        // Act
        var text = await Git.ReadFileAsync(head, "missing.txt", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(text);
    }

    [Fact]
    public async Task FindRemoteBranchAsync_BranchOnOrigin_ReturnsTheOriginRef()
    {
        // Arrange
        _repo.Write("a.txt", "one");
        _repo.Commit("base");
        _repo.AddOrigin();

        // Act
        var remoteRef = await Git.FindRemoteBranchAsync("main", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("origin/main", remoteRef);
    }

    [Fact]
    public async Task FindRemoteBranchAsync_BranchOnOneRemoteNotNamedOrigin_ReturnsThatRemotesRef()
    {
        // Arrange
        _repo.Write("a.txt", "one");
        _repo.Commit("base");
        _repo.AddRemote("upstream");

        // Act
        var remoteRef = await Git.FindRemoteBranchAsync("main", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("upstream/main", remoteRef);
    }

    [Fact]
    public async Task FindRemoteBranchAsync_BranchOnOriginAndAnotherRemote_PrefersOrigin()
    {
        // Arrange
        _repo.Write("a.txt", "one");
        _repo.Commit("base");
        _repo.AddRemote("fork");
        _repo.AddOrigin();

        // Act
        var remoteRef = await Git.FindRemoteBranchAsync("main", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("origin/main", remoteRef);
    }

    [Fact]
    public async Task FindRemoteBranchAsync_BranchOnNoRemote_ThrowsUsageExceptionNamingTheBranch()
    {
        // Arrange
        _repo.Write("a.txt", "one");
        _repo.Commit("base");
        _repo.AddOrigin();

        // Act
        var exception = await Record.ExceptionAsync(
            () => Git.FindRemoteBranchAsync("release", TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("release", Assert.IsType<UsageException>(exception).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFileAsync_UnknownRevision_ThrowsUsageException()
    {
        // Arrange
        _repo.Write("a.txt", "one");
        _repo.Commit("base");

        // Act
        var exception = await Record.ExceptionAsync(
            () => Git.ReadFileAsync("nope", "a.txt", TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<UsageException>(exception);
    }

    [Fact]
    public async Task ListFilesAsync_Revision_ListsEveryFileWithItsSize()
    {
        // Arrange
        _repo.Write("a.txt", "one");
        _repo.Write("dir/with space/b.cs", "class B { }");
        var head = _repo.Commit("files");

        // Act
        var files = await Git.ListFilesAsync(head, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([new TreeFile("a.txt", 3), new TreeFile("dir/with space/b.cs", 11)], files);
    }

    [Fact]
    public async Task FindFilesContainingAsync_WordsUnderTheGlobs_FindsOnlyThoseFiles()
    {
        // Arrange
        _repo.Write("tests/ATests.cs", "// TC1201\n");
        _repo.Write("tests/BTests.cs", "// TC1202\n");
        _repo.Write("tests/CTests.cs", "// nothing\n");
        _repo.Write("src/A.cs", "// TC1201\n");
        var head = _repo.Commit("files");

        // Act
        var files = await Git.FindFilesContainingAsync(
            head, ["1201", "1202"], ["tests/**/*.cs"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["tests/ATests.cs", "tests/BTests.cs"], files);
    }

    [Fact]
    public async Task ReadFilesAsync_SeveralFiles_ReadsEachInOneBatch()
    {
        // Arrange
        _repo.Write("a.txt", "eins\nzwei\n");
        _repo.Write("dir/with space/ü.cs", "// Grüße, TC1201\n");
        var head = _repo.Commit("files");

        // Act
        var files = await Git.ReadFilesAsync(
            head, ["a.txt", "missing.txt", "dir/with space/ü.cs"], NoLimit, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, files.Count);
        Assert.Equal("eins\nzwei\n", files["a.txt"]);
        Assert.Equal("// Grüße, TC1201\n", files["dir/with space/ü.cs"]);
    }

    [Fact]
    public async Task ReadFilesAsync_TextThatIsNotUtf8_ReadsTheFilesAfterItToo()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_repo.Root, "a.txt"), [0x47, 0x72, 0xFC, 0xDF, 0x65, 0x0A]);
        _repo.Write("b.txt", "after\n");
        var head = _repo.Commit("files");

        // Act
        var files = await Git.ReadFilesAsync(head, ["a.txt", "b.txt"], NoLimit, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("after\n", files["b.txt"]);
        Assert.True(files.ContainsKey("a.txt"));
    }

    // A byte git cannot decode becomes U+FFFD, three bytes where the file had one, so the batch walk drifts. Here the
    // drift lands on one of the file's own newlines, which looks like the end of the entry.
    [Fact]
    public async Task ReadFilesAsync_InvalidByteFollowedByNewlines_ReadsTheWholeFile()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_repo.Root, "a.txt"), [0xFC, 0x0A, 0x0A, 0x0A]);
        var head = _repo.Commit("files");

        // Act
        var files = await Git.ReadFilesAsync(head, ["a.txt"], NoLimit, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("�\n\n\n", files["a.txt"]);
    }

    [Fact]
    public async Task ReadFilesAsync_FileLargerThanTheLimit_IsLeftOutAndTheOthersAreRead()
    {
        // Arrange
        _repo.Write("large.txt", "12345");
        _repo.Write("small.txt", "ab");
        var head = _repo.Commit("files");

        // Act
        var files = await Git.ReadFilesAsync(
            head, ["large.txt", "small.txt"], maxBytes: 3, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["small.txt"], files.Keys);
    }
}
