using Microsoft.Extensions.FileSystemGlobbing;

namespace Harness;

// Glob matching, ordinal like git paths.
static class Globs
{
    public static Matcher Of(IEnumerable<string> globs)
    {
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddIncludePatterns(globs);
        return matcher;
    }

    // The harness's own tests name made-up Test Case ids, and the default glob `**/*Tests/**/*.cs` reaches them. They
    // test the harness, never the product, so they are left out of a report on any pull request.
    public static Matcher OfTests(TestImpactDefinition definition)
    {
        var matcher = Of(definition.TestGlobs);
        matcher.AddExclude("utils/harness/**");
        return matcher;
    }

    public static bool Matches(this Matcher matcher, string path) => matcher.Match(path).HasMatches;
}
