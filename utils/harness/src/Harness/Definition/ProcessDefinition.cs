using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Harness;

/// <summary>
/// The parsed and validated <c>harness.json</c>: the repository's overrides. Every key but <c>version</c> is
/// optional, and an absent key takes its default from code.
/// </summary>
/// <param name="TestImpact">
/// The <c>testImpact</c> block, with the default of every absent key; <see cref="TestImpactDefinition.Default"/>
/// when the block is absent.
/// </param>
/// <param name="Boards">The <c>boards</c> block; <see langword="null"/> when it is absent.</param>
sealed partial record ProcessDefinition(TestImpactDefinition TestImpact, BoardsDefinition? Boards)
{
    /// <summary>The definition's file name at the repository root.</summary>
    internal const string FileName = "harness.json";

    static readonly JsonSerializerOptions s_options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Parses and validates a <c>harness.json</c> document.</summary>
    /// <param name="json">The document text. Comments and trailing commas are allowed.</param>
    /// <returns>The validated definition.</returns>
    /// <exception cref="UsageException">The document is not valid; the message starts with the JSON path.</exception>
    public static ProcessDefinition Parse(string json)
    {
        Dto dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json, s_options)
                ?? throw new UsageException($"{FileName}: must be an object");
        }
        catch (JsonException exception)
        {
            var path = PathSeparators().Replace(exception.Path ?? "$", ".$1").TrimStart('$', '.');
            throw new UsageException(
                $"{(path.Length == 0 ? FileName : path)}: {exception.Message.Split(" Path:")[0]}", exception);
        }

        if (dto.Version is null) { throw Invalid("version", "required"); }
        if (dto.Version != 1) { throw Invalid("version", "must be 1"); }

        return new(BuildTestImpact(dto.TestImpact), BuildBoards(dto.Boards));
    }

    static TestImpactDefinition BuildTestImpact(TestImpactDto? dto)
    {
        if (dto is null) { return TestImpactDefinition.Default; }

        var platforms = TestImpactDefinition.DefaultPlatforms;
        if (dto.Platforms is not null)
        {
            platforms = platforms.Clear();
            foreach (var (name, globs) in dto.Platforms)
            {
                var platformPath = $"testImpact.platforms.{name}";
                if (!PlatformNamePattern().IsMatch(name))
                {
                    throw Invalid(platformPath, "name must match ^[a-z][a-z0-9]*$");
                }

                if (globs is not { Count: > 0 }) { throw Invalid(platformPath, "must be a non-empty list of globs"); }

                platforms = platforms.Add(name, ValidateGlobs(globs, platformPath));
            }
        }

        return new TestImpactDefinition(
            UiTests: dto.UiTests is null
                ? TestImpactDefinition.DefaultUiTests
                : ValidateGlobs(dto.UiTests, "testImpact.uiTests"),
            UnitTests: dto.UnitTests is null
                ? TestImpactDefinition.DefaultUnitTests
                : ValidateGlobs(dto.UnitTests, "testImpact.unitTests"),
            TestCaseIdPattern: BuildPattern(dto.TestCaseIdPattern),
            Platforms: platforms,
            SourceExtensions: dto.SourceExtensions is null
                ? TestImpactDefinition.DefaultSourceExtensions
                : ValidateExtensions(dto.SourceExtensions, "testImpact.sourceExtensions"),
            SearchLimit: BuildSearchLimit(dto.SearchLimit));
    }

    static ImmutableArray<string> ValidateExtensions(List<string?> extensions, string extensionsPath)
    {
        if (extensions.Count == 0) { throw Invalid(extensionsPath, "must be a non-empty list of extensions"); }

        return ValidateEach(extensions, extensionsPath, extension => extension is ['.', _, ..], "must start with a dot");
    }

    static SearchLimit BuildSearchLimit(SearchLimitDto? dto)
    {
        if (dto is null) { return SearchLimit.Default; }

        if (dto.Files is < 1) { throw Invalid("testImpact.searchLimit.files", "must be at least 1"); }
        if (dto.Megabytes is < 1) { throw Invalid("testImpact.searchLimit.megabytes", "must be at least 1"); }

        return new(dto.Files ?? SearchLimit.Default.Files, dto.Megabytes ?? SearchLimit.Default.Megabytes);
    }

    static Regex BuildPattern(string? pattern)
    {
        Regex compiled;
        try
        {
            compiled = TestImpactDefinition.CompilePattern(pattern ?? TestImpactDefinition.DefaultTestCaseIdPattern);
        }
        catch (ArgumentException exception)
        {
            throw new UsageException($"{TestImpactDefinition.PatternKey}: {exception.Message}", exception);
        }

        // Group 0 is the whole match; the id is group 1.
        if (compiled.GetGroupNumbers().Length < 2)
        {
            throw Invalid(TestImpactDefinition.PatternKey, "must have a capture group for the Test Case id");
        }

        return compiled;
    }

    static BoardsDefinition? BuildBoards(BoardsDto? dto)
    {
        if (dto is null) { return null; }

        // The organization is not checked as a URL here: a placeholder left in harness.json must not fail the whole
        // report, so the Chain row says what is wrong with it instead.
        if (dto.Organization is not null && string.IsNullOrWhiteSpace(dto.Organization))
        {
            throw Invalid("boards.organization", "must be a non-empty string");
        }

        if (dto.Project is not null && string.IsNullOrWhiteSpace(dto.Project))
        {
            throw Invalid("boards.project", "must be a non-empty string");
        }

        return new(dto.Organization, dto.Project);
    }

    static ImmutableArray<string> ValidateGlobs(List<string?> globs, string globsPath) =>
        ValidateEach(globs, globsPath, IsRelative, "must be a non-empty relative glob");

    static ImmutableArray<string> ValidateEach(
        List<string?> values, string listPath, Func<string?, bool> isValid, string problem)
    {
        foreach (var (index, value) in values.Index())
        {
            if (!isValid(value)) { throw Invalid($"{listPath}[{index}]", problem); }
        }

        // A null entry never passes isValid: a pattern on null is false, and IsRelative rejects it.
        return [.. values!];
    }

    static bool IsRelative([NotNullWhen(true)] string? path) => !string.IsNullOrWhiteSpace(path)
        && path[0] is not ('/' or '\\') && !Path.IsPathRooted(path) && !(path.Length > 1 && path[1] == ':');

    static UsageException Invalid(string path, string problem) => new($"{path}: {problem}");

    [GeneratedRegex(@"^[a-z][a-z0-9]*\z")]
    private static partial Regex PlatformNamePattern();

    // JsonException.Path spells a key like characterization-tests as ['characterization-tests'].
    [GeneratedRegex(@"\['([^']*)'\]")]
    private static partial Regex PathSeparators();

    sealed record Dto(int? Version, TestImpactDto? TestImpact, BoardsDto? Boards);

    sealed record TestImpactDto(
        List<string?>? UiTests,
        List<string?>? UnitTests,
        string? TestCaseIdPattern,
        Dictionary<string, List<string?>?>? Platforms,
        List<string?>? SourceExtensions,
        SearchLimitDto? SearchLimit);

    sealed record SearchLimitDto(int? Files, int? Megabytes);

    sealed record BoardsDto(string? Organization, string? Project);
}
