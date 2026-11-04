namespace Harness.Tests;

// One run a process runner substitute received, of gh or of az: its arguments and what it was given on standard input.
sealed record RunnerCall(string[] Arguments, string? StandardInput);
