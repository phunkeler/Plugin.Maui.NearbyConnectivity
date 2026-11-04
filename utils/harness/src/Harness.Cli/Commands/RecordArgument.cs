using System.CommandLine;
using System.CommandLine.Parsing;

namespace Harness.Cli;

/// <summary>
/// The <c>&lt;record&gt;</c> argument of <c>scope</c>, parsed with the command line so a bad one is a usage error like
/// any other.
/// </summary>
sealed class RecordArgument : Argument<RecordRef?>
{
    /// <summary>Creates the argument.</summary>
    public RecordArgument()
        : base("record")
    {
        Description = "The pull request, such as #57, or its address on GitHub.";
        Arity = ArgumentArity.ZeroOrOne;
        CustomParser = Parse;
    }

    /// <summary>Tells whether the argument holds a token that looks like an option.</summary>
    /// <param name="command">The result of the command that has the argument.</param>
    /// <returns>
    /// <see langword="true"/> when the token starts with <c>-</c>; otherwise, <see langword="false"/>. A command's
    /// option rules check nothing then, so the parser's "unrecognized" is the only error.
    /// </returns>
    /// <remarks>
    /// System.CommandLine puts an unknown option into an argument that accepts any token, and it parses no argument
    /// once a validator has added an error.
    /// </remarks>
    public bool HoldsOption(CommandResult command) =>
        command.GetResult(this) is { Tokens: [var token] } && IsOptionLike(token);

    static bool IsOptionLike(Token token) => token.Value.StartsWith('-');

    static RecordRef? Parse(ArgumentResult result)
    {
        var token = result.Tokens.Single();
        if (IsOptionLike(token))
        {
            result.AddError($"Unrecognized command or argument '{token.Value}'.");
            return null;
        }

        try
        {
            return RecordRef.Parse(token.Value);
        }
        catch (UsageException exception)
        {
            result.AddError(exception.Message);
            return null;
        }
    }
}
