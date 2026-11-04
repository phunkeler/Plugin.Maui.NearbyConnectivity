using System.Collections.Immutable;

namespace Harness;

/// <summary>Which platforms a change reaches, so a reviewer knows which device runs it needs.</summary>
/// <param name="Platforms">The platform names the change touches, by path or by <c>#if</c>, sorted.</param>
/// <param name="Shared">
/// Whether a changed source file is platform-neutral: under no platform glob and without a platform <c>#if</c>.
/// </param>
sealed record PlatformReach(ImmutableArray<string> Platforms, bool Shared)
{
    /// <summary>The reach of a change that touches no source.</summary>
    public static PlatformReach None { get; } = new([], false);
}
