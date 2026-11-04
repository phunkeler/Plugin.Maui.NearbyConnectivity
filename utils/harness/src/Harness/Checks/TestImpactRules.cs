using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Harness;

/// <summary>
/// Pure rules over a change's paths and texts: which are tests, which Test Cases a test names and in which methods,
/// and which platforms a file reaches.
/// </summary>
static partial class TestImpactRules
{
    // A Test Case id on an attribute line belongs to the next method declared within this many lines.
    const int MethodWindow = 6;

    // No declaration line is this long; a longer one is content an author made to be slow to scan. The length is the
    // bound on the fixed patterns below, which run without a timeout.
    const int MaxScannedLineLength = 4096;

    /// <summary>Tells whether a path is a source file, whose content can name a platform.</summary>
    /// <param name="path">The path from the top level.</param>
    /// <param name="definition">The <c>testImpact</c> block, whose <c>sourceExtensions</c> decide.</param>
    /// <returns>
    /// <see langword="true"/> when the path's extension is one of the definition's, in any case; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    public static bool IsSource(string path, TestImpactDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.SourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Compiles each platform's globs once, for a loop over many paths.</summary>
    /// <param name="platforms">The globs of each platform, by name.</param>
    /// <returns>A matcher per platform, by the same name and in the same order.</returns>
    public static ImmutableSortedDictionary<string, Matcher> CompileMatchers(
        ImmutableSortedDictionary<string, ImmutableArray<string>> platforms)
    {
        ArgumentNullException.ThrowIfNull(platforms);
        return platforms.ToImmutableSortedDictionary(
            platform => platform.Key, platform => Globs.Of(platform.Value), platforms.KeyComparer);
    }

    /// <summary>Finds the Test Case ids a text names.</summary>
    /// <param name="text">The text.</param>
    /// <param name="pattern">The pattern whose group 1 is the id.</param>
    /// <returns>The ids, distinct and ascending; a group that is not a 32-bit integer is skipped.</returns>
    public static ImmutableArray<int> TestCaseIdsIn(string text, Regex pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return
        [
            .. pattern.Matches(text)
                .Select(match => int.TryParse(
                    match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    ? id
                    : (int?)null)
                .OfType<int>()
                .Distinct()
                .Order(),
        ];
    }

    /// <summary>Finds the test methods that carry a Test Case id.</summary>
    /// <param name="text">The test file's C# text.</param>
    /// <param name="pattern">The pattern whose group 1 is the id.</param>
    /// <returns>
    /// The methods, in file order. A method carries the ids on its own declaration line, and on the lines above it
    /// up to five lines back, such as an attribute. Comment lines are skipped and do not count toward the five. The
    /// method is named after the nearest class declared above it. Ids with no method declaration in reach are left
    /// out.
    /// </returns>
    public static ImmutableArray<TestMethod> TestMethodsIn(string text, Regex pattern)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pattern);

        var methods = ImmutableArray.CreateBuilder<TestMethod>();
        var pending = new SortedSet<int>();
        var reach = 0;
        var className = "";
        foreach (var line in text.Split('\n'))
        {
            if (line.Length > MaxScannedLineLength || CommentLine().IsMatch(line))
            {
                continue;
            }

            if (ClassDeclaration().Match(line) is { Success: true } declaredClass)
            {
                className = declaredClass.Groups[1].Value;
            }

            if (TestCaseIdsIn(line, pattern) is { IsEmpty: false } ids)
            {
                pending.UnionWith(ids);
                reach = MethodWindow;
            }

            if (reach == 0)
            {
                continue;
            }

            if (MethodDeclaration().Match(line) is { Success: true } method)
            {
                methods.Add(new($"{className}.{method.Groups[1].Value}", [.. pending]));
                pending.Clear();
                reach = 0;
            }
            else if (--reach == 0)
            {
                pending.Clear();
            }
        }

        return methods.ToImmutable();
    }

    /// <summary>
    /// Finds the platforms a file reaches: by its path, and by the <c>#if</c> directives of its content.
    /// </summary>
    /// <param name="path">The path from the top level.</param>
    /// <param name="content">The file's text; <see langword="null"/> to classify by path only.</param>
    /// <param name="platforms">The globs of each platform, compiled by <see cref="CompileMatchers"/>, by name.</param>
    /// <returns>
    /// The names, sorted, whose globs match the path or whose upper-cased name, bare or between double underscores,
    /// appears on an <c>#if</c> or <c>#elif</c> line, such as <c>#if ANDROID || IOS</c> or <c>#if __IOS__</c>.
    /// </returns>
    public static ImmutableArray<string> PlatformsOf(
        string path, string? content, ImmutableSortedDictionary<string, Matcher> platforms)
    {
        ArgumentNullException.ThrowIfNull(platforms);
        return
        [
            .. platforms.Where(platform => platform.Value.Matches(path)
                    || (content is not null && HasDirectiveFor(content, platform.Key)))
                .Select(platform => platform.Key),
        ];
    }

    // Linear in the content: each line is scanned once from its `#if`. The reader caps the content's size.
    static bool HasDirectiveFor(string content, string platform)
    {
        var symbol = Regex.Escape(platform.ToUpperInvariant());
        return Regex.IsMatch(
            content,
            $@"^[ \t]*#(?:if|elif)\b[^\r\n]*\b(?:{symbol}|__{symbol}__)\b",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"(?:^|[^A-Za-z0-9_])class\s+([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex ClassDeclaration();

    // An access modifier, then no '=' or ';' before the parameter list: a method, not a field or a call.
    [GeneratedRegex(@"^\s*(?:public|internal|private|protected)\b[^=;(]*?\b([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]*>)?\s*\(")]
    private static partial Regex MethodDeclaration();

    [GeneratedRegex(@"^\s*(?://|/\*|\*)")]
    private static partial Regex CommentLine();
}
