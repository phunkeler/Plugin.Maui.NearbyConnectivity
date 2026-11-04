using System.Collections.Concurrent;

namespace Harness;

/// <summary>Counts the calls the harness has made to each external tool since its services were built.</summary>
sealed class ToolCalls
{
    readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);

    /// <summary>Counts one call.</summary>
    /// <param name="tool">The program, such as <c>git</c>.</param>
    public void Add(string tool) => _calls.AddOrUpdate(tool, 1, (_, count) => count + 1);

    /// <summary>Reads the count for a tool.</summary>
    /// <param name="tool">The program, such as <c>git</c>.</param>
    /// <returns>The calls so far, a failed one included; 0 for a tool never called.</returns>
    public int CountOf(string tool) => _calls.GetValueOrDefault(tool);
}
