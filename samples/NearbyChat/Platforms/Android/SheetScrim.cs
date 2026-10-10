using AndroidX.Core.View;

namespace NearbyChat.Controls;

/// <summary>
/// The scrim behind an open bottom sheet, which darkens the status bar on either theme.
/// </summary>
public static class SheetScrim
{
    /// <summary>
    /// Asks for light status bar icons just before a sheet opens.
    /// </summary>
    /// <remarks>
    /// The sheet's dialog copies the activity's icon style when it attaches, so the activity has to
    /// carry the light style before then. <c>MainActivity</c> puts the theme's own style back when
    /// its window regains focus, which is when the sheet has closed.
    /// </remarks>
    public static void Opening()
    {
        if (Platform.CurrentActivity?.Window is { } window
            && WindowCompat.GetInsetsController(window, window.DecorView) is { } insets)
        {
            insets.AppearanceLightStatusBars = false;
        }
    }
}
