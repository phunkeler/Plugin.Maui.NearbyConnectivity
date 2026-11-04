using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Harness;

/// <summary>
/// Reads a change's test impact and platform reach from the repository: which Test Cases the changed tests name and
/// in which methods, and which platforms the changed files reach.
/// </summary>
/// <param name="git">The repository.</param>
sealed class TestImpactReader(IGit git)
{
    /// <summary>
    /// The most changed source files whose content is read for <c>#if</c> directives; a file past the cap is classified
    /// by its path only.
    /// </summary>
    public const int MaxContentReads = 300;

    /// <summary>
    /// The largest changed file that is read; a larger one is classified by its path only. The head is the pull
    /// request's, so its files are as large as their author likes.
    /// </summary>
    public const long MaxFileBytes = 4L * 1024 * 1024;

    /// <summary>Reads the impact of a change.</summary>
    /// <param name="definition">
    /// The <c>testImpact</c> block that says where the tests and the platform code live.
    /// </param>
    /// <param name="changed">The changed paths.</param>
    /// <param name="mergeBase">The diff's older side, where a deleted test is read.</param>
    /// <param name="headRevision">The revision the changed files are read on: the diff's newer side.</param>
    /// <param name="cancellationToken">Cancels the git calls.</param>
    /// <returns>
    /// The test impact over the changed tests, a deleted one read on <paramref name="mergeBase"/>; and the platform
    /// reach over the changed files that are not tests and not deleted. The first <see cref="MaxContentReads"/>
    /// source files are also read for their <c>#if</c> directives. A file larger than <see cref="MaxFileBytes"/> is
    /// classified by its path only. The files are read in one git call per revision.
    /// </returns>
    /// <exception cref="UsageException">
    /// A revision does not exist, or the definition's Test Case id pattern timed out on a file.
    /// </exception>
    public async Task<ScopeImpact> ReadAsync(
        TestImpactDefinition definition,
        ImmutableArray<ChangedPath> changed,
        string mergeBase,
        string headRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var tests = Globs.OfTests(definition);
        var platformMatchers = TestImpactRules.CompileMatchers(definition.Platforms);

        List<string> onHead = [];
        List<string> onMergeBase = [];
        var contentReads = 0;
        foreach (var (kind, path) in changed)
        {
            if (tests.Matches(path))
            {
                (kind == ChangeKind.Deleted ? onMergeBase : onHead).Add(path);
            }
            else if (kind != ChangeKind.Deleted
                && TestImpactRules.IsSource(path, definition)
                && contentReads < MaxContentReads)
            {
                contentReads++;
                onHead.Add(path);
            }
        }

        var headTexts = await git.ReadFilesAsync(headRevision, onHead, MaxFileBytes, cancellationToken);
        var mergeBaseTexts = await git.ReadFilesAsync(mergeBase, onMergeBase, MaxFileBytes, cancellationToken);

        var changedTests = new List<TestReference>();
        var platforms = new SortedSet<string>(StringComparer.Ordinal);
        var shared = false;
        foreach (var (kind, path) in changed)
        {
            if (tests.Matches(path))
            {
                var texts = kind == ChangeKind.Deleted ? mergeBaseTexts : headTexts;
                changedTests.Add(Reference(path, texts.GetValueOrDefault(path, ""), definition));
                continue;
            }

            if (kind == ChangeKind.Deleted)
            {
                continue;
            }

            var isSource = TestImpactRules.IsSource(path, definition);
            var reached = TestImpactRules.PlatformsOf(path, headTexts.GetValueOrDefault(path), platformMatchers);
            platforms.UnionWith(reached);
            shared |= isSource && reached.IsEmpty;
        }

        return new(TestImpact.Of(changedTests), new PlatformReach([.. platforms], shared));
    }

    /// <summary>
    /// Searches a revision's test files for Test Case ids: the tests that already carry a Test Case the change does
    /// not touch.
    /// </summary>
    /// <param name="definition">The <c>testImpact</c> block that says where the tests live.</param>
    /// <param name="revision">The revision to search, such as the pull request's head.</param>
    /// <param name="testCaseIds">The ids to look for.</param>
    /// <param name="skipped">Test files not to search, such as the changed ones, which are read already.</param>
    /// <param name="cancellationToken">Cancels the git calls.</param>
    /// <returns>
    /// The test files that name an id, with their methods. The files under the test globs that contain an id as text
    /// are read in path order, up to the definition's <c>searchLimit</c>.
    /// </returns>
    /// <exception cref="UsageException">
    /// The revision does not exist, or the definition's Test Case id pattern timed out on a file.
    /// </exception>
    public async Task<TestSearch> SearchAsync(
        TestImpactDefinition definition,
        string revision,
        IReadOnlyCollection<int> testCaseIds,
        IReadOnlyCollection<string> skipped,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(testCaseIds);
        ArgumentNullException.ThrowIfNull(skipped);
        if (testCaseIds.Count == 0)
        {
            return TestSearch.None;
        }

        var tests = Globs.OfTests(definition);
        var sizes = (await git.ListFilesAsync(revision, cancellationToken))
            .Where(file => tests.Matches(file.Path))
            .ToDictionary(file => file.Path, file => file.Size, StringComparer.Ordinal);

        // Any pattern that captures an id leaves the id's digits in the text, so they are a safe prefilter.
        var candidates = (await git
                .FindFilesContainingAsync(
                    revision,
                    [.. testCaseIds.Select(id => id.ToString(CultureInfo.InvariantCulture))],
                    definition.TestGlobs,
                    cancellationToken))
            .Where(path => sizes.ContainsKey(path) && !skipped.Contains(path))
            .Order(StringComparer.Ordinal)
            .ToList();

        var read = new List<string>();
        long bytes = 0;
        foreach (var path in candidates)
        {
            if (read.Count == definition.SearchLimit.Files || bytes + sizes[path] > definition.SearchLimit.Bytes)
            {
                break;
            }

            read.Add(path);
            bytes += sizes[path];
        }

        var texts = await git.ReadFilesAsync(revision, read, definition.SearchLimit.Bytes, cancellationToken);
        return new(
            TestImpact.Of(texts.Select(text => Reference(text.Key, text.Value, definition))
                .Where(reference => reference.TestCaseIds.Any(testCaseIds.Contains))),
            read.Count,
            candidates.Count);
    }

    // The path counts too: a UI test often carries its Test Case id only in its file name.
    static TestReference Reference(string path, string text, TestImpactDefinition definition)
    {
        try
        {
            return new(
                path,
                TestImpactRules.TestCaseIdsIn(path + "\n" + text, definition.TestCaseIdPattern),
                TestImpactRules.TestMethodsIn(text, definition.TestCaseIdPattern),
                TestImpactRules.TestCaseIdsIn(path, definition.TestCaseIdPattern));
        }
        catch (RegexMatchTimeoutException exception)
        {
            // The rules' own patterns run without a timeout, bounded by line length, so a timeout here is the
            // definition's pattern.
            throw new UsageException(
                $"{TestImpactDefinition.PatternKey}: timed out after {exception.MatchTimeout.TotalSeconds:0.##} s on {path}",
                exception);
        }
    }
}
