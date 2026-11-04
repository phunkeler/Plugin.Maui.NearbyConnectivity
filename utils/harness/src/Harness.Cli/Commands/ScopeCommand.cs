using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Harness.Cli;

/// <summary>
/// <c>harness scope</c>: what a pull request touches and what it is linked to, as Markdown for a pull request comment,
/// or as JSON. A report only: it never fails the pull request.
/// </summary>
sealed class ScopeCommand : Command
{
    readonly CliContext _context;
    readonly RecordArgument _record = new();

    /// <summary>Creates the command.</summary>
    /// <param name="context">The services and streams the command uses.</param>
    public ScopeCommand(CliContext context)
        : base("scope", "Report what a pull request touches and what it is linked to.")
    {
        _context = context;
        Arguments.Add(_record);
        Validators.Add(Validate);
        SetAction(RunAsync);
    }

    void Validate(CommandResult command)
    {
        if (!_record.HoldsOption(command) && !command.HasValue(_record))
        {
            command.AddError("scope needs <record>");
        }
    }

    async Task<int> RunAsync(ParseResult parse, CancellationToken cancellationToken)
    {
        await using var services = _context.ServicesFor(parse);
        await using var scope = services.CreateAsyncScope();
        var change = await scope.ServiceProvider.ResolveChangeAsync(cancellationToken);
        var record = parse.GetValue(_record) ?? throw new UnreachableException("the validator requires <record>");
        var report = await change.ScopeAsync(record, cancellationToken);
        var text = parse.GetValue(_context.Json) ? JsonOutput.Format(report) + "\n" : TextOutput.Format(report);
        await _context.Stdout.WriteAsync(text);
        return ExitCodes.Success;
    }
}
