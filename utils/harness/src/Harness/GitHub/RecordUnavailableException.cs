namespace Harness;

/// <summary>
/// The record system is unreachable, refused the caller, or returned a response the harness cannot read. The CLI maps
/// it to exit code 5.
/// </summary>
public sealed class RecordUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">
    /// The record system's first line of error output, or what in its response the harness cannot read.
    /// </param>
    public RecordUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception for a response the harness cannot read.</summary>
    /// <param name="message">What in the record system's response the harness cannot read.</param>
    /// <param name="innerException">The error that reading the response raised.</param>
    public RecordUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
