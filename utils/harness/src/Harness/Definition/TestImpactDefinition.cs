using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Harness;

/// <summary>
/// The <c>testImpact</c> block of <c>harness.json</c>: where the tests live, how a test names its Azure Boards Test
/// Case, and which paths are platform code. Every key has a default here, so <c>harness.json</c> carries only what
/// differs.
/// </summary>
/// <param name="UiTests">Globs of the UI automation test sources.</param>
/// <param name="UnitTests">Globs of the unit test sources.</param>
/// <param name="TestCaseIdPattern">
/// The pattern that finds a Test Case id in a test's text; group 1 is the id. Culture-invariant, with a one second
/// timeout.
/// </param>
/// <param name="Platforms">The globs of each platform's code, by the platform's lower-case name, sorted.</param>
/// <param name="SourceExtensions">
/// The extensions, with their dot, of the files whose content can name a platform with <c>#if</c>, and which count as
/// shared code when no platform glob matches them.
/// </param>
/// <param name="SearchLimit">How many test files and megabytes a search of the head for a Test Case id may read.</param>
sealed record TestImpactDefinition(
    ImmutableArray<string> UiTests,
    ImmutableArray<string> UnitTests,
    Regex TestCaseIdPattern,
    ImmutableSortedDictionary<string, ImmutableArray<string>> Platforms,
    ImmutableArray<string> SourceExtensions,
    SearchLimit SearchLimit)
{
    /// <summary>
    /// The pattern a definition without <c>testCaseIdPattern</c> uses: <c>TC1234</c>, <c>TC_1234</c> or
    /// <c>TC-1234</c>, with three to seven digits, not after a letter or digit but allowed after an underscore, as in
    /// <c>Opens_TC1234</c>.
    /// </summary>
    public const string DefaultTestCaseIdPattern = @"(?<![0-9A-Za-z])TC[_-]?([0-9]{3,7})(?![0-9])";

    /// <summary>The JSON path of <see cref="TestCaseIdPattern"/> in <c>harness.json</c>, for messages.</summary>
    public const string PatternKey = "testImpact.testCaseIdPattern";

    /// <summary>How long a match of <see cref="TestCaseIdPattern"/> may take.</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>The globs of the UI tests when <c>uiTests</c> is absent.</summary>
    public static ImmutableArray<string> DefaultUiTests { get; } = ["uitest/**/*.cs"];

    /// <summary>The globs of the unit tests when <c>unitTests</c> is absent.</summary>
    public static ImmutableArray<string> DefaultUnitTests { get; } =
        ["**/*Tests/**/*.cs", "test/**/*.cs", "tests/**/*.cs"];

    /// <summary>The platforms when <c>platforms</c> is absent: <c>android</c> and <c>ios</c>.</summary>
    public static ImmutableSortedDictionary<string, ImmutableArray<string>> DefaultPlatforms { get; } =
        ImmutableSortedDictionary<string, ImmutableArray<string>>.Empty.WithComparers(StringComparer.Ordinal)
            .Add("android", ["**/Platforms/Android/**", "**/*.Android.cs", "**/*.android.cs"])
            .Add("ios", ["**/Platforms/iOS/**", "**/*.iOS.cs", "**/*.ios.cs"]);

    /// <summary>The source extensions when <c>sourceExtensions</c> is absent: <c>.cs</c> and <c>.xaml</c>.</summary>
    public static ImmutableArray<string> DefaultSourceExtensions { get; } = [".cs", ".xaml"];

    /// <summary>
    /// The definition a <c>harness.json</c> without the block gets, and the one a base without <c>harness.json</c>
    /// gets.
    /// </summary>
    public static TestImpactDefinition Default { get; } = new(
        DefaultUiTests,
        DefaultUnitTests,
        CompilePattern(DefaultTestCaseIdPattern),
        DefaultPlatforms,
        DefaultSourceExtensions,
        SearchLimit.Default);

    /// <summary>The UI and unit test globs together.</summary>
    public ImmutableArray<string> TestGlobs => [.. UiTests, .. UnitTests];

    /// <summary>Compiles a Test Case id pattern with the options every definition uses.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The regular expression, culture-invariant, with a one second timeout.</returns>
    /// <exception cref="ArgumentException">The pattern does not parse.</exception>
    public static Regex CompilePattern(string pattern) => new(pattern, RegexOptions.CultureInvariant, MatchTimeout);
}
