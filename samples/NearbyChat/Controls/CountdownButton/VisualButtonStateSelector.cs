namespace NearbyChat.Controls;

/// <summary>
/// How little of the countdown is left, as one of three bands. The button resolves this from
/// <see cref="CountdownButton.Progress"/> against its two thresholds; the selector turns it into
/// a <see cref="VisualButtonState"/>.
/// </summary>
public enum CountdownUrgency
{
    /// <summary>Above the caution threshold.</summary>
    None,

    /// <summary>At or below the caution threshold, but above the warning threshold.</summary>
    Caution,

    /// <summary>At or below the warning threshold.</summary>
    Warning,
}

/// <summary>
/// Selects a <see cref="VisualButtonState"/> from the button's flags using a priority chain:
/// Expired &gt; Disabled &gt; Warning &gt; Caution &gt; Counting &gt; Default. Override <see cref="Resolve"/> and assign the
/// instance to <see cref="CountdownButton.StateSelector"/> to insert a state without subclassing the button.
/// </summary>
public class VisualButtonStateSelector
{
    public static readonly VisualButtonStateSelector Default = new();

    /// <remarks>
    /// Expired outranks Disabled: the button is normally disabled by the time its window has run
    /// out, and "this offer expired" is the more useful of the two things to show.
    /// </remarks>
    public virtual VisualButtonState Resolve(bool isEnabled, bool isCounting, CountdownUrgency urgency, bool isExpired)
        => (isEnabled, isCounting, urgency, isExpired) switch
        {
            { isExpired: true } => VisualButtonState.Expired,
            { isEnabled: false } => VisualButtonState.Disabled,
            { isCounting: true, urgency: CountdownUrgency.Warning } => VisualButtonState.Warning,
            { isCounting: true, urgency: CountdownUrgency.Caution } => VisualButtonState.Caution,
            { isCounting: true } => VisualButtonState.Counting,
            _ => VisualButtonState.Default,
        };
}
