using System.Globalization;
using System.Text.RegularExpressions;

namespace Harness;

/// <summary>A reference to a GitHub pull request.</summary>
/// <param name="Number">The pull request number, 1 or more.</param>
/// <param name="Repository">
/// The repository the reference names, as <c>owner/name</c>; <see langword="null"/> for the current repository.
/// </param>
readonly partial record struct RecordRef(int Number, string? Repository = null)
{
    /// <summary>
    /// Parses a pull request reference: <c>#57</c>, <c>57</c>, or the pull request's address on GitHub, with or
    /// without a trailing tab such as <c>/files</c>.
    /// </summary>
    /// <param name="text">The reference as the user wrote it.</param>
    /// <returns>The reference; from an address, with its <c>owner/name</c>.</returns>
    /// <exception cref="UsageException">
    /// <paramref name="text"/> is a work item reference such as <c>AB#4711</c>, or is not a positive number or a pull
    /// request address.
    /// </exception>
    public static RecordRef Parse(string text)
    {
        if (text.StartsWith("AB#", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException($"{text}: a work item is not a pull request; pass the pull request, such as #57");
        }

        if (PullRequestAddress().Match(text) is { Success: true } address)
        {
            return int.TryParse(
                address.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var fromAddress)
                && fromAddress > 0
                ? new(fromAddress, $"{address.Groups["owner"].Value}/{address.Groups["name"].Value}")
                : throw new UsageException($"{text}: expected a pull request number such as #57");
        }

        var digits = text.StartsWith('#') ? text[1..] : text;
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? new(number)
            : throw new UsageException($"{text}: expected a pull request number such as #57");
    }

    /// <summary>Formats the reference as GitHub writes it.</summary>
    /// <returns><c>#</c> and the number, such as <c>#57</c>, after <c>owner/name</c> when the reference names one.</returns>
    public override string ToString() =>
        Repository + "#" + Number.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(
        @"^https?://github\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<name>[A-Za-z0-9_.-]+)/pull/(?<number>[0-9]+)(?:[/?#][^\s]*)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PullRequestAddress();
}
