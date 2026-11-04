namespace Harness;

// Paths are repository-relative with '/'. A rename is reported by its new path.
readonly record struct ChangedPath(ChangeKind Kind, string Path);
