namespace NearbyChat.Controls;

/// <summary>
/// The iOS side of the Android status bar fix for an open bottom sheet. It does nothing: a sheet
/// on iOS is a view controller, and the system picks the status bar style for it.
/// </summary>
public static class SheetScrim
{
    /// <summary>
    /// Does nothing on iOS.
    /// </summary>
    public static void Opening()
    {
    }
}
