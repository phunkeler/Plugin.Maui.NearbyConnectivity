namespace Harness;

/// <summary>A pull request, as the scope report reads it.</summary>
/// <param name="Number">The pull request number.</param>
/// <param name="Title">The title.</param>
/// <param name="Body">The description; <see langword="null"/> when it is empty.</param>
/// <param name="Author">The author, in the canonical spelling.</param>
/// <param name="BaseRef">The base branch, without <c>origin/</c>.</param>
/// <param name="HeadSha">The head commit.</param>
sealed record PullRequest(int Number, string Title, string? Body, Identity Author, string BaseRef, string HeadSha);
