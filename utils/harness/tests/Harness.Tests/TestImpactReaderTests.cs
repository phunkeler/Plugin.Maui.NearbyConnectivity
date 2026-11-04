using System.Collections.Immutable;
using System.Text.RegularExpressions;
using NSubstitute;

namespace Harness.Tests;

// The composition of test impact and platform reach over a git substitute: which revision each file is read on, and
// what the reader makes of what it finds.
public sealed class TestImpactReaderTests
{
    const string MergeBase = "merge-base";
    const string Head = "head";
    const string ViewModel = "src/Delivery/SignOffViewModel.cs";
    const string Clock = "src/Shared/Clock.cs";
    const string UiTest = "uitest/Delivery/TC1201_SignOffTests.cs";
    const string HarnessTest = "utils/harness/tests/Harness.Tests/TestImpactRulesTests.cs";

    static readonly TestImpactDefinition s_definition = TestImpactDefinition.Default;

    readonly IGit _git = Substitute.For<IGit>();

    // What each revision holds, by path; the reader gets the files it asks for in one call per revision.
    readonly Dictionary<(string Revision, string Path), string> _committed = [];

    public TestImpactReaderTests() =>
        _git.ReadFilesAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyList<string>>(1)
                .Where(path => _committed.ContainsKey((call.ArgAt<string>(0), path)))
                .ToImmutableDictionary(path => path, path => _committed[(call.ArgAt<string>(0), path)]));

    [Fact]
    public async Task ReadAsync_ChangedTest_ReadsItsTestCasesOnTheHead()
    {
        // Arrange
        Committed(Head, UiTest, "// TC1201 and TC1205");
        Committed(MergeBase, UiTest, "// TC1201");
        ImmutableArray<ChangedPath> changed = [new(ChangeKind.Modified, UiTest)];

        // Act
        var impact = await Read(changed);

        // Assert
        Assert.Equal([$"{UiTest} [1201, 1205]"], impact.TestImpact.ChangedTests.Select(Show));
    }

    [Fact]
    public async Task ReadAsync_ChangedTest_ReadsTheMethodsThatCarryAnId()
    {
        // Arrange
        Committed(Head, UiTest, "class SignOffTests\n{\n    [Test] // TC1205\n    public void SignsOff()\n    {\n    }\n}\n");
        ImmutableArray<ChangedPath> changed = [new(ChangeKind.Added, UiTest)];

        // Act
        var impact = await Read(changed);

        // Assert
        Assert.Equal(["SignOffTests.SignsOff"], impact.TestImpact.TestsFor(1205));
    }

    [Fact]
    public async Task ReadAsync_DeletedTest_ReadsItsTestCasesOnTheMergeBase()
    {
        // Arrange
        Committed(MergeBase, UiTest, "// TC1205");
        ImmutableArray<ChangedPath> changed = [new(ChangeKind.Deleted, UiTest)];

        // Act
        var impact = await Read(changed);

        // Assert
        Assert.Equal([$"{UiTest} [1201, 1205]"], impact.TestImpact.ChangedTests.Select(Show));
    }

    [Fact]
    public async Task ReadAsync_TestCaseIdOnlyInTheFileName_ReadsIt()
    {
        // Arrange
        Committed(Head, UiTest, "class SignOffTests");
        ImmutableArray<ChangedPath> changed = [new(ChangeKind.Modified, UiTest)];

        // Act
        var impact = await Read(changed);

        // Assert
        Assert.Equal([1201], impact.TestImpact.TestCaseIds);
        Assert.Equal([UiTest + " (file name)"], impact.TestImpact.TestsFor(1201));
    }

    [Fact]
    public async Task ReadAsync_PatternTimesOutOnAChangedTest_ThrowsUsageExceptionNamingTheKeyAndTheFile()
    {
        // Arrange
        ImmutableArray<ChangedPath> changed = [new(ChangeKind.Modified, UiTest)];
        Committed(Head, UiTest, new string('a', 20_000) + "!");
        var definition = s_definition with
        {
            TestCaseIdPattern = new Regex(
                "(a+)+b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)),
        };

        // Act
        var exception = await Record.ExceptionAsync(() => new TestImpactReader(_git)
            .ReadAsync(definition, changed, MergeBase, Head, TestContext.Current.CancellationToken));

        // Assert
        var message = Assert.IsType<UsageException>(exception).Message;
        Assert.StartsWith("testImpact.testCaseIdPattern: timed out", message, StringComparison.Ordinal);
        Assert.Contains(UiTest, message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_ConfiguredSourceExtension_CountsThatFileAsSharedSource()
    {
        // Arrange
        const string kotlin = "src/Shared/Clock.kt";
        ImmutableArray<ChangedPath> changed = [new(ChangeKind.Modified, kotlin)];
        Committed(Head, kotlin, "fun now() = 0\n");
        var definition = s_definition with { SourceExtensions = [".kt"] };

        // Act
        var impact = await new TestImpactReader(_git)
            .ReadAsync(definition, changed, MergeBase, Head, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(impact.PlatformReach.Shared);
    }

    [Fact]
    public async Task ReadAsync_ChangedSourceOnly_HasNoTests()
    {
        // Arrange
        ImmutableArray<ChangedPath> changed = [new(ChangeKind.Modified, ViewModel)];

        // Act
        var impact = await Read(changed);

        // Assert
        Assert.Empty(impact.TestImpact.ChangedTests);
        Assert.Empty(impact.TestImpact.TestCaseIds);
    }

    [Fact]
    public async Task ReadAsync_DeletedSource_ReachesNothing()
    {
        // Arrange
        ImmutableArray<ChangedPath> changed = [new(ChangeKind.Deleted, Clock)];

        // Act
        var impact = await Read(changed);

        // Assert
        Assert.Empty(impact.PlatformReach.Platforms);
        Assert.False(impact.PlatformReach.Shared);
    }

    [Fact]
    public async Task ReadAsync_PlatformFileByPathAndSharedFileByContent_ReachesBoth()
    {
        // Arrange
        Committed(Head, Clock, "namespace Shared;\n");
        Committed(Head, ViewModel, "#if IOS\n#endif\n");
        ImmutableArray<ChangedPath> changed =
        [
            new(ChangeKind.Modified, "src/Platforms/Android/MainActivity.cs"),
            new(ChangeKind.Modified, ViewModel),
            new(ChangeKind.Modified, Clock),
        ];

        // Act
        var impact = await Read(changed);

        // Assert
        Assert.Equal(["android", "ios"], impact.PlatformReach.Platforms);
        Assert.True(impact.PlatformReach.Shared);
    }

    [Fact]
    public async Task ReadAsync_OnlyPlatformFiles_IsNotShared()
    {
        // Arrange
        Committed(Head, "src/Platforms/Android/MainActivity.cs", "#if ANDROID\n#endif\n");
        ImmutableArray<ChangedPath> changed =
        [
            new(ChangeKind.Modified, "src/Platforms/Android/MainActivity.cs"),
            new(ChangeKind.Modified, "src/Platforms/Android/AndroidManifest.xml"),
        ];

        // Act
        var impact = await Read(changed);

        // Assert
        Assert.Equal(["android"], impact.PlatformReach.Platforms);
        Assert.False(impact.PlatformReach.Shared);
    }

    [Fact]
    public async Task ReadAsync_ChangedTestOnAPlatformPath_DoesNotCountForReach()
    {
        // Arrange
        ImmutableArray<ChangedPath> changed = [new(ChangeKind.Modified, "tests/Platforms/Android/ATests.cs")];

        // Act
        var impact = await Read(changed);

        // Assert
        Assert.Empty(impact.PlatformReach.Platforms);
        Assert.False(impact.PlatformReach.Shared);
    }

    [Fact]
    public async Task SearchAsync_UnchangedTestCarriesTheId_FindsItsMethod()
    {
        // Arrange
        OnHead(("tests/LeaveTests.cs", "class LeaveTests\n{\n    public void Peers_leave_TC1203()\n    {\n    }\n}\n"));

        // Act
        var search = await Search([1203]);

        // Assert
        Assert.Equal(["LeaveTests.Peers_leave_TC1203"], search.Found.TestsFor(1203));
        Assert.False(search.Capped);
    }

    [Fact]
    public async Task ReadAsync_HarnessOwnTestChanged_NamesNoTestCase()
    {
        // Arrange
        Committed(Head, HarnessTest, "// TC1203");
        ImmutableArray<ChangedPath> changed = [new(ChangeKind.Modified, HarnessTest)];

        // Act
        var impact = await Read(changed);

        // Assert
        Assert.Empty(impact.TestImpact.ChangedTests);
    }

    [Fact]
    public async Task SearchAsync_HarnessOwnTestCarriesTheId_IsLeftOut()
    {
        // Arrange
        OnHead((HarnessTest, "// TC1203\n"));

        // Act
        var search = await Search([1203]);

        // Assert
        Assert.Empty(search.Found.ChangedTests);
        Assert.Equal(0, search.Candidates);
    }

    [Fact]
    public async Task SearchAsync_NonTestFileCarriesTheId_IsLeftOut()
    {
        // Arrange
        OnHead(("src/Leave.cs", "// TC1203\n"));

        // Act
        var search = await Search([1203]);

        // Assert
        Assert.Empty(search.Found.ChangedTests);
        Assert.Equal(0, search.Candidates);
    }

    [Fact]
    public async Task SearchAsync_ChangedTestSkipped_IsNotReadAgain()
    {
        // Arrange
        OnHead(("tests/LeaveTests.cs", "// TC1203\n"));

        // Act
        var search = await new TestImpactReader(_git).SearchAsync(
            s_definition, Head, [1203], ["tests/LeaveTests.cs"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(search.Found.ChangedTests);
    }

    [Fact]
    public async Task SearchAsync_MoreCandidatesThanTheFileCap_ReadsTheFirst2000ByPath()
    {
        // Arrange
        OnHead([.. Enumerable.Range(0, 2001).Select(index => ($"tests/T{index:0000}Tests.cs", "// TC1203\n"))]);

        // Act
        var search = await Search([1203]);

        // Assert
        Assert.Equal((2000, 2001, true), (search.Searched, search.Candidates, search.Capped));
        Assert.DoesNotContain("tests/T2000Tests.cs", search.Found.ChangedTests.Select(test => test.TestFile));
    }

    [Fact]
    public async Task SearchAsync_FileCapFromTheDefinition_ReadsThatManyFiles()
    {
        // Arrange
        OnHead(("tests/ATests.cs", "// TC1203\n"), ("tests/BTests.cs", "// TC1203\n"), ("tests/CTests.cs", "// TC1203\n"));
        var definition = s_definition with { SearchLimit = new(Files: 2, Megabytes: 20) };

        // Act
        var search = await new TestImpactReader(_git)
            .SearchAsync(definition, Head, [1203], [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((2, 3, true), (search.Searched, search.Candidates, search.Capped));
    }

    [Fact]
    public async Task SearchAsync_ByteCapFromTheDefinition_StopsBeforeTheFileThatCrossesIt()
    {
        // Arrange
        OnHead(("tests/ATests.cs", "// TC1203\n"), ("tests/BTests.cs", "// TC1203\n"));
        _git.ListFilesAsync(Head, Arg.Any<CancellationToken>()).Returns(
        [
            new TreeFile("tests/ATests.cs", 1024 * 1024),
            new TreeFile("tests/BTests.cs", 1024 * 1024),
        ]);
        var definition = s_definition with { SearchLimit = new(Files: 2000, Megabytes: 1) };

        // Act
        var search = await new TestImpactReader(_git)
            .SearchAsync(definition, Head, [1203], [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((1, 2), (search.Searched, search.Candidates));
    }

    [Fact]
    public async Task SearchAsync_CandidatesPastTheByteCap_StopsBeforeTheFileThatCrossesIt()
    {
        // Arrange
        OnHead(("tests/ATests.cs", "// TC1203\n"), ("tests/BTests.cs", "// TC1203\n"));
        _git.ListFilesAsync(Head, Arg.Any<CancellationToken>()).Returns(
        [
            new TreeFile("tests/ATests.cs", 15L * 1024 * 1024),
            new TreeFile("tests/BTests.cs", 6L * 1024 * 1024),
        ]);

        // Act
        var search = await Search([1203]);

        // Assert
        Assert.Equal((1, 2), (search.Searched, search.Candidates));
    }

    // The head holds these files; every one contains the sought text.
    void OnHead(params (string Path, string Text)[] files)
    {
        _git.ListFilesAsync(Head, Arg.Any<CancellationToken>())
            .Returns([.. files.Select(file => new TreeFile(file.Path, file.Text.Length))]);
        _git.FindFilesContainingAsync(
                Head, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([.. files.Select(file => file.Path)]);
        foreach (var (path, text) in files)
        {
            Committed(Head, path, text);
        }
    }

    Task<TestSearch> Search(int[] testCaseIds) => new TestImpactReader(_git)
        .SearchAsync(s_definition, Head, testCaseIds, [], TestContext.Current.CancellationToken);

    // A changed test as "file [ids]", so a whole list is one assertion.
    static string Show(TestReference reference) =>
        $"{reference.TestFile} [{string.Join(", ", reference.TestCaseIds)}]";

    Task<ScopeImpact> Read(ImmutableArray<ChangedPath> changed) => new TestImpactReader(_git)
        .ReadAsync(s_definition, changed, MergeBase, Head, TestContext.Current.CancellationToken);

    void Committed(string revision, string path, string text) => _committed[(revision, path)] = text;
}
