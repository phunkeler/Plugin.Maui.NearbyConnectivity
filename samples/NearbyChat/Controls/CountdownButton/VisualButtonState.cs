using System.Runtime.CompilerServices;

namespace NearbyChat.Controls;

/// <summary>
/// The resolved visual state of a <see cref="CountdownButton"/>, selected by
/// <see cref="VisualButtonStateSelector"/>. Name-based equality so an app can add its own
/// states (<c>new VisualButtonState("Pressed")</c>) and route them through the VSM by name.
/// </summary>
public readonly struct VisualButtonState(string name) : IEquatable<VisualButtonState>
{
    public string Name { get; } = name;

    /// <summary>Idle: filled pill, no ring.</summary>
    public static readonly VisualButtonState Default = Named();

    /// <summary>An offer window is open: the ring traces the remaining fraction.</summary>
    public static readonly VisualButtonState Counting = Named();

    /// <summary>Counting, and the remaining fraction is at or below the caution threshold.</summary>
    public static readonly VisualButtonState Caution = Named();

    /// <summary>Counting, and the remaining fraction is at or below the warning threshold.</summary>
    public static readonly VisualButtonState Warning = Named();

    /// <summary>
    /// The window ran out: outlined rather than filled, held on screen for a moment so the
    /// outcome is visible before the row goes away.
    /// </summary>
    public static readonly VisualButtonState Expired = Named();

    /// <summary>Highest priority — beats every other state.</summary>
    public static readonly VisualButtonState Disabled = Named();

    public bool Equals(VisualButtonState other) => string.Equals(Name, other.Name, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is VisualButtonState v && Equals(v);
    public override int GetHashCode() => Name is null ? 0 : Name.GetHashCode(StringComparison.Ordinal);

    public static bool operator ==(VisualButtonState a, VisualButtonState b) => a.Equals(b);
    public static bool operator !=(VisualButtonState a, VisualButtonState b) => !a.Equals(b);

    public override string ToString() => Name;

    static VisualButtonState Named([CallerMemberName] string name = "") => new(name);
}
