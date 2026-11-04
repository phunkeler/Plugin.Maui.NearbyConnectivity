namespace Harness;

/// <summary>
/// The command line is wrong, a ref, path or tool it needs does not exist, or the process definition is missing or
/// invalid. The CLI maps it to exit code 2.
/// </summary>
public sealed class UsageException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, starting with the JSON path for a definition error.</param>
    public UsageException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception for a lower-level failure it explains.</summary>
    /// <param name="message">What is wrong, starting with the JSON path for a definition error.</param>
    /// <param name="innerException">The failure that showed it.</param>
    public UsageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
