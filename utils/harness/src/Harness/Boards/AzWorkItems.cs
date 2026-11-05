using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Harness;

/// <summary>
/// The work items of one Azure Boards project through <c>az rest</c>, which takes its token from <c>az login</c>: no
/// extension and no personal access token. Works on Entra-backed and personal organizations alike.
/// </summary>
sealed partial class AzWorkItems : IWorkItems
{
    /// <summary>The link type of Tested By, from the story to its Test Case.</summary>
    public const string TestedBy = "Microsoft.VSTS.Common.TestedBy-Forward";

    /// <summary>The work item type of a Test Case.</summary>
    public const string TestCaseType = "Test Case";

    /// <summary>How long one <c>az rest</c> call may take before Boards counts as unreachable.</summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(60);

    // The Azure DevOps application id: the audience of the token az rest gets.
    const string AzureDevOpsResource = "499b84ac-1321-427f-aa17-267ca6975798";

    // What az writes when nothing is signed in. It names a command a CI job cannot run, so the reason replaces it.
    const string AzNotSignedIn = "Please run 'az login'";

    /// <summary>The reason on the Chain row when az is not signed in.</summary>
    public const string NotSignedInReason =
        "az is not signed in. Locally, run az login. In CI, set up the sign-in in utils/harness/docs/azure-devops.md";

    // A reason on the Chain row is cut here.
    const int MaxReasonLength = 200;

    readonly IProcessRunner _processRunner;
    readonly TimeProvider _timeProvider;
    readonly string _workItemsUrl;

    // Each work item, read once for as long as this instance lives; null when it does not exist.
    readonly Dictionary<int, WorkItem?> _read = [];

    /// <summary>Opens a project's work items.</summary>
    /// <param name="processRunner">Runs az.</param>
    /// <param name="timeProvider">Times each call against <see cref="CallTimeout"/>.</param>
    /// <param name="boards">The organization and project; both must be set.</param>
    /// <exception cref="ArgumentException"><paramref name="boards"/> is not complete.</exception>
    public AzWorkItems(IProcessRunner processRunner, TimeProvider timeProvider, BoardsDefinition boards)
    {
        ArgumentNullException.ThrowIfNull(boards);
        if (!boards.IsComplete)
        {
            throw new ArgumentException("the organization and the project must both be set", nameof(boards));
        }

        _processRunner = processRunner;
        _timeProvider = timeProvider;
        _workItemsUrl =
            $"{boards.Organization.TrimEnd('/')}/{Uri.EscapeDataString(boards.Project)}/_apis/wit/workitems/";
    }

    public async Task<WorkItem?> ReadWorkItemAsync(int id, CancellationToken cancellationToken)
    {
        if (!_read.TryGetValue(id, out var workItem))
        {
            _read[id] = workItem = await GetAsync(id, cancellationToken);
        }

        return workItem;
    }

    public async Task<ImmutableArray<WorkItem>> ReadLinkedTestCasesAsync(
        int storyId, CancellationToken cancellationToken)
    {
        var story = await ReadWorkItemAsync(storyId, cancellationToken);
        if (story is null)
        {
            return [];
        }

        var testCases = new List<WorkItem>();
        foreach (var relation in story.Relations.Where(relation => relation.Type == TestedBy))
        {
            var target = TargetId(relation.Url) is { } id
                ? await ReadWorkItemAsync(id, cancellationToken)
                : null;
            if (target is { Type: TestCaseType })
            {
                testCases.Add(target);
            }
        }

        return [.. testCases.DistinctBy(testCase => testCase.Id).OrderBy(testCase => testCase.Id)];
    }

    async Task<WorkItem?> GetAsync(int id, CancellationToken cancellationToken)
    {
        // The query goes in --url-parameters, so no '&' reaches cmd.exe, which runs az.cmd on Windows.
        string[] arguments =
        [
            "rest", "--resource", AzureDevOpsResource, "--only-show-errors", "-o", "json",
            "--url", _workItemsUrl + id.ToString(CultureInfo.InvariantCulture),
            "--url-parameters", "$expand=relations", "api-version=7.1",
        ];
        using var timeout = new CancellationTokenSource(CallTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        ProcessResult result;
        try
        {
            result = await _processRunner
                .RunAsync("az", arguments, workingDirectory: null, standardInput: null, linked.Token);
        }
        catch (OperationCanceledException exception)
            when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new RecordUnavailableException(
                $"az rest timed out after {CallTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s",
                exception);
        }
        catch (Exception exception) when (exception is UsageException or TimeoutException)
        {
            throw new RecordUnavailableException(exception.Message, exception);
        }

        var (exitCode, stdout, stderr) = result;
        if (exitCode == 0)
        {
            return Parse(stdout);
        }

        // TF401232: the work item does not exist, or the caller may not read it. Boards does not say which.
        return stderr.Contains("TF401232", StringComparison.Ordinal)
            ? null
            : throw new RecordUnavailableException(ReasonFrom(stderr, exitCode));
    }

    /// <summary>Picks the line of az's error output that says what went wrong, for the Chain row.</summary>
    /// <param name="stderr">What az wrote to standard error.</param>
    /// <param name="exitCode">az's exit code.</param>
    /// <returns>
    /// <see cref="NotSignedInReason"/> when az is not signed in; otherwise the last <c>ERROR:</c> line that names an
    /// Azure DevOps error code (<c>TF</c> and digits) or a connection failure, else the last <c>ERROR:</c> line, else
    /// the first line; without <c>ERROR:</c>, reduced to the message of a JSON error or the title of an HTML page,
    /// without markup, at most 200 characters.
    /// </returns>
    public static string ReasonFrom(string stderr, int exitCode)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        if (stderr.Contains(AzNotSignedIn, StringComparison.Ordinal))
        {
            return NotSignedInReason;
        }

        List<string> lines = [.. stderr.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];
        if (lines.Count == 0)
        {
            return $"az rest exited with code {exitCode.ToString(CultureInfo.InvariantCulture)} and wrote no error";
        }

        var line = lines.LastOrDefault(line => IsError(line) && IsTelling(line))
            ?? lines.LastOrDefault(IsTelling)
            ?? lines.LastOrDefault(IsError)
            ?? lines[0];
        var reason = IsError(line) ? line["ERROR:".Length..].Trim() : line;
        if (JsonMessage().Match(reason) is { Success: true } message)
        {
            reason = message.Groups[1].Value;
        }
        else if (reason.Contains('<', StringComparison.Ordinal) && HtmlTitle().Match(stderr) is { Success: true } title)
        {
            reason = $"{reason.Split('(')[0].Trim()}: {title.Groups[1].Value.Trim()}";
        }

        reason = Whitespace().Replace(Markup().Replace(reason, " "), " ").Trim();
        return reason.Length <= MaxReasonLength ? reason : string.Concat(reason.AsSpan(0, MaxReasonLength - 1), "…");

        static bool IsError(string line) => line.StartsWith("ERROR:", StringComparison.Ordinal);

        static bool IsTelling(string line) =>
            line.Contains("HTTPSConnectionPool", StringComparison.Ordinal) || ErrorCode().IsMatch(line);
    }

    static WorkItem Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var fields = root.GetProperty("fields");
            return new(
                Id: root.GetProperty("id").GetInt32(),
                Type: Required(fields, "System.WorkItemType"),
                Title: Required(fields, "System.Title"),
                State: Required(fields, "System.State"),
                Tags:
                [
                    .. (fields.TryGetProperty("System.Tags", out var tags) ? tags.GetString() ?? "" : "")
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                ],
                Relations: root.TryGetProperty("relations", out var relations)
                    ? [.. relations.EnumerateArray().Select(relation => new WorkItemRelation(
                        Required(relation, "rel"), Required(relation, "url")))]
                    : []);
        }
        catch (Exception exception)
            when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new RecordUnavailableException(
                "Azure Boards sent a response the harness cannot read: " + exception.Message, exception);
        }
    }

    static string Required(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new InvalidOperationException($"{name} is null");

    // ".../_apis/wit/workItems/2" is work item 2; any other target is not a work item.
    static int? TargetId(string url)
    {
        const string Marker = "/workItems/";
        var at = url.LastIndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        return at >= 0 && int.TryParse(
            url.AsSpan(at + Marker.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    [GeneratedRegex(@"\bTF[0-9]+\b")]
    private static partial Regex ErrorCode();

    [GeneratedRegex(@"""message""\s*:\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex JsonMessage();

    [GeneratedRegex(@"<title>([^<]*)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTitle();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Markup();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
