namespace Harness;

/// <summary>The record does not exist. The CLI maps it to exit code 3.</summary>
public sealed class RecordNotFoundException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Which record was not found.</param>
    public RecordNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the failure that revealed it.</summary>
    /// <param name="message">Which record was not found.</param>
    /// <param name="innerException">The failure, such as the error a tool reported.</param>
    public RecordNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
