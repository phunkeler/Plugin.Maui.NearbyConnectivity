namespace Harness;

/// <summary>What <see cref="TestImpactReader"/> reads for a change: its test impact and its platform reach.</summary>
/// <param name="TestImpact">The tests the change reaches.</param>
/// <param name="PlatformReach">The platforms the change reaches.</param>
sealed record ScopeImpact(TestImpact TestImpact, PlatformReach PlatformReach);
