using System.CommandLine;
using System.CommandLine.Parsing;

namespace Harness.Cli;

/// <summary>Reads a command's tokens in its validator, before the parser has checked them.</summary>
/// <remarks>
/// A validator runs before the parser checks arity, so a rule reads the tokens rather than the parsed value.
/// </remarks>
static class CommandResultExtensions
{
    /// <summary>Tells whether the command line gave the argument a value.</summary>
    /// <param name="command">The command's result.</param>
    /// <param name="argument">The argument.</param>
    /// <returns><see langword="true"/> when the argument has a token; otherwise, <see langword="false"/>.</returns>
    public static bool HasValue(this CommandResult command, Argument argument) =>
        command.GetResult(argument) is { Tokens.Count: > 0 };
}
