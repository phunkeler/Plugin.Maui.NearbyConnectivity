# AGENTS.md

Instructions for coding agents working in `utils/harness/`.

The harness is a .NET command-line tool, `harness`, with one command, `harness scope`. It reads a pull request and
reports what it touches and what it is linked to: the work item its description names, the platforms it reaches,
the Test Cases its tests carry, and the chain from each story through its Test Cases in Azure Boards to the test
methods. It reads only, and never fails a pull request. It is built from this folder in CI
(`.github/workflows/harness-scope.yml`), from the pull request's base commit.

This folder is the harness's only source. There is no upstream to sync from and no generated code: every change is
made here, with its tests and its README row, through an ordinary pull request.

## Commits

Follow the repository's own branch and pull request rules. Commit subjects are `type: imperative summary`, for
example `fix: the chain row reads the head, not the merge base`.

## Map

| Path | What |
| --- | --- |
| `src/Harness/` | The library: the scope verb (`Change`), GitHub records, git, test impact, the Azure Boards provider (`AzWorkItems`), the Chain row (`ChainBuilder`) and `harness.json` (`ProcessDefinition`). |
| `src/Harness.Cli/` | The `harness` command line on System.CommandLine. Assembly `Harness.Cli`, tool command `harness`. |
| `tests/Harness.Tests/` | xunit.v3 on Microsoft.Testing.Platform, `net10.0`. Recorded `az rest` answers in `Fixtures/`. |
| `docs/` | Setup guides, such as the Azure Boards sign-in for CI. |
| `README.md` | What the report says, how to install it, and every `harness.json` key. |
| `../../harness.json` | The repository's overrides, at the repository root. |

## Commands

```bash
dotnet build Harness.slnx -c Release                         # analyzers at full strength
dotnet test tests/Harness.Tests                              # every test
dotnet test tests/Harness.Tests -- --filter-class '*ScopeReportTests'   # one class
```

The test runner is Microsoft.Testing.Platform. Filter with `--filter-class`, not `--filter`. Do not pass
`-nologo` or `-v q` to `dotnet test`: they are forwarded to the test application and break the run.

## Hard rules

- `TreatWarningsAsErrors` is on. A warning is a failure. Fix it; do not suppress it. No new `NoWarn`.
- Every public async method that does I/O takes a `CancellationToken`. No `ConfigureAwait`: a console process has
  no synchronization context, so it would change nothing.
- Every public or protected member has an XML doc comment. Write them with the `/dotnet-xmldoc` skill.
- Package versions go in `Directory.Packages.props`, never on a `PackageReference`. Prefer
  `Microsoft.Extensions.*` packages. Every new dependency needs a reason in the commit body.
- A `//` comment records what the code cannot say: an invariant a refactor would break, or a platform
  behaviour the code works around. A comment that narrates the next line is not allowed. A known limit is
  written as a plain comment that names the limit and the fix, with no tool-specific tag.

## C# design

The house style is the Microsoft Framework Design Guidelines, with public members documented in XML doc comments.
Code must read like conventional .NET to a .NET developer.

- Every I/O boundary has an interface: GitHub records, Azure Boards work items, git, running a process, the clock
  (`TimeProvider`).
  Consumers take the interface through constructor injection. `AddHarness(options => ...)` on `IServiceCollection`
  registers the implementations, in the `Microsoft.Extensions.DependencyInjection` namespace as Microsoft's
  guidelines ask; the host's settings are `HarnessOptions`. `Change` and its readers are scoped: a command opens
  one scope per run through `IServiceScopeFactory` and resolves `Change` inside it, the one place application code
  touches the container. No delegate seams. No static I/O
  helpers, except for the file system: there is no file-system interface, so static helpers may read and write
  files, and tests use real temporary directories. Any other process state, such as an environment variable,
  is read in an `AddHarness` factory and injected.
- Pure domain logic with no I/O, such as `TestImpactRules`, may be a static class.
- Diagnostics go through `ILogger`, as `Log` events with stable ids, to stderr; stdout carries the result only.
  `--verbose` lowers the level to Debug. No `Console` writes outside the command line's own output and error
  streams. System.CommandLine owns the signals and the 130/143 codes; the harness owns every other exit code,
  so its default exception handler stays off.
- Accessibility follows `.editorconfig` (`omit_if_default`): leave out `internal` on types and `private` on
  members.
- Names say what they hold. No single-letter locals or parameters, except loop indexes and short lambdas.
  A cancellation token is `cancellationToken`.
- One top-level type per file, named after the type.
- A non-private method returns a named record, not a tuple.
- A literal is one of three kinds, and the kind decides where it goes (README, "What you can set, what is fixed,
  what is detected"). Would two adopting teams want it different? Yes: a `harness.json` key with the current value
  as its default, a row in the README table. No: a named constant, documented as contract. Depends on the machine
  or the CI host: detected at run time, never a key and never a flag. No second configuration channel beside
  `harness.json`.

## Tests

These rules follow Microsoft's
[Unit testing best practices for .NET](https://learn.microsoft.com/dotnet/core/testing/unit-testing-best-practices).
A test documents behaviour. It must keep passing through any refactor that does not change what a caller sees.

### What a test asserts

- Assert what a caller can observe: the return value, the exception type and message, the command line sent,
  the report built. Never assert how the code got there. The order and number of calls, and which query
  fetched the data, are implementation.
- A stub answers exactly the call the code should make, with the arguments that matter: `ReadReturns`, or
  `AzWorkItem(id)`. `Create.GitHubRecords()`, `Create.WorkItems()` and `Create.RunnerAnsweringNothing()` fail
  every call a test did not answer, so a wrong query already fails the test. Do not add `Received()` to check a
  read.
- Assert an outward write on what was sent: read the runner's calls with `GhCalls()` or `AzCalls()`, then assert
  on the list. Use `Received()` or `DidNotReceive()` only for a
  write that cannot be captured, with a `//` comment that says why.
- Test caching and idempotence through results, not call counts. Make the stub answer differently on the second
  call (`Returns(first, second)`) and assert that the second result equals the first.
- Test through the surface a caller uses. `InternalsVisibleTo` is for internal types that have no public
  caller. A private method is tested through the member that calls it. Do not widen production code for a test.
- Before you finish a test, break the code it covers and check that the test fails.

### How a test reads

- Name tests `Method_Scenario_ExpectedResult` in PascalCase. The result is an observable outcome, for example
  `BuildAsync_ProviderUnavailable_SaysWhyAndListsNoStory`, never a call such as
  `BuildAsync_ProviderUnavailable_CallsReadWorkItem`.
- Every test has `// Arrange`, `// Act` and `// Assert` sections, with no text after them, and exactly one
  act.
- Arrange the smallest input that drives the behaviour. Leave out properties and values the behaviour does not
  depend on. A value that carries meaning gets a named field or a `Create` helper.
- No `if`, `?:`, loops or string building in a test method. A set of cases is a `[Theory]`, and cases that
  assert differently are separate tests. Helper methods that build input may branch.
- The constructor holds only what every test in the class needs, such as the temporary repository and the
  clock. Scenario setup goes in Arrange, shared through helper methods. Teardown is `IDisposable`.
- Theory rows are typed: `[InlineData]`, or `TheoryData<...>` with one row per case. Add a `Label` when the
  values do not explain the case. Every row is serializable, so each row is its own test. No dictionaries of
  tuples keyed by a string.

### Test doubles

- Fake interfaces with NSubstitute. Use real objects when they are cheap: records, `FakeTimeProvider`, and the
  temporary git repository in `TempRepo`.
- Name a double by its role. A stub returns data. A mock is the double that the assertion checks.
- Tests never reach GitHub, Azure Boards or the network. `IGitHubRecords`, `IWorkItems` and every `gh` and `az`
  call are substitutes; Azure Boards responses are recorded fixtures under `tests/Harness.Tests/Fixtures/`. Real
  `git` and the built CLI may run against a temporary directory.

## Done means

- `dotnet build Harness.slnx -c Release` has no warnings, and every test passes.
- A behaviour change updates `README.md` in the same pull request.

## Gotchas

- Ask before committing. A plan that names a branch is not permission to commit to it.
- Never brief a subagent "follow the existing code's style"; point it at this file.
