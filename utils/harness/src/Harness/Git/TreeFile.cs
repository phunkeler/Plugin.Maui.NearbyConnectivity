namespace Harness;

/// <summary>A file in a commit's tree.</summary>
/// <param name="Path">The path from the top level, with <c>/</c>.</param>
/// <param name="Size">The size in bytes.</param>
readonly record struct TreeFile(string Path, long Size);
