namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Specifies the status of a data transfer.
/// </summary>
public enum NearbyTransferStatus
{
    /// <summary>The transfer has started and has not yet reached a terminal state.</summary>
    InProgress,

    /// <summary>The transfer completed successfully.</summary>
    Completed,

    /// <summary>The transfer failed because of an error.</summary>
    Failed,

    /// <summary>The transfer was canceled before it completed.</summary>
    Canceled,
}
