namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// The exception that is thrown when a nearby connectivity operation fails.
/// </summary>
/// <remarks>
/// Every exception this plugin raises derives from this type, including the ones callers are
/// expected to catch by name (listed below). It is deliberately not sealed, so that callers can
/// derive their own exception types from it. Catch <see cref="NearbyException"/> to handle any
/// failure that originates in the plugin without enumerating every derived type.
/// </remarks>
/// <seealso cref="NearbyConnectException"/>
/// <seealso cref="NearbyTransferException"/>
/// <seealso cref="NearbyTransferTimeoutException"/>
public class NearbyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NearbyException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public NearbyException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NearbyException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public NearbyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
