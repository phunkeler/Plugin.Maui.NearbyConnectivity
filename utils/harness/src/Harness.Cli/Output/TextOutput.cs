using System.Text;

namespace Harness.Cli;

/// <summary>The default output: the scope report as GitHub-flavoured Markdown.</summary>
static class TextOutput
{
    /// <summary>Formats the scope report as GitHub-flavoured Markdown, for a pull request comment.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The lines, each ending in a newline, the last one the <c>harness-scope</c> marker comment.</returns>
    public static string Format(ScopeReport report)
    {
        var markdown = new StringBuilder();
        markdown.Append("### ").Append(ScopeText.Heading).Append("\n\n");
        markdown.Append("| | |\n|---|---|\n");
        markdown.Append(Cell("Pull request", [ScopeText.PullRequest(report)]));
        markdown.Append(Cell("Work item link", [ScopeText.WorkItemLink(report)]));
        markdown.Append(Cell("Platform reach", [ScopeText.PlatformReach(report)]));
        markdown.Append(Cell("Test Cases", [ScopeText.TestCases(report)]));
        markdown.Append(Cell("Chain", ScopeText.Chain(report)));
        return markdown.Append('\n').Append(ScopeText.Marker).Append('\n').ToString();
    }

    // One row; several lines become one cell joined by <br>. The only other markup a cell holds is a Test Case link,
    // whose address the harness built.
    static string Cell(string label, IEnumerable<string> lines) =>
        $"| {label} | {string.Join("<br>", lines.Select(Escape))} |\n";

    // A pipe would end the cell, a line break the row, and `<` could open an HTML comment that swallows the rows after
    // it; with `<` gone, `>` and `&` need no escape. Paths and work item titles carry whatever their authors wrote.
    static string Escape(string value) => value
        .Replace("\r\n", " ", StringComparison.Ordinal)
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal);
}
