# Harness

A small .NET tool that reads a pull request and reports what it touches and what it is linked to. It runs in GitHub
Actions on every pull request, runs none of the pull request's code, and needs no secret.

## What the report says

Every pull request gets one "Change scope" comment, edited in place on every push. It is advice only: it never
fails the pull request.

| Row | What it says |
|---|---|
| Pull request | The number, the head commit, the base branch and the author. |
| Work item link | The `AB#` work items the pull request *description* names, or `none: no AB# in the description`. The title does not count: Azure Boards links a work item from the description or a commit only. The row reads the text only: Boards makes the link itself, and only for a repository on the project's GitHub connection (**Project settings > GitHub connections**). When the link works, the story shows the pull request under **Development**. |
| Platform reach | Android, iOS or shared, from the paths (`Platforms/Android`, `*.iOS.cs` and so on) and from `#if ANDROID` / `#if IOS` in the changed files, with the device runs that follow: "needs an Android device run", "shared code only". |
| Test Cases | Each Test Case id the changed test files carry, and the test methods that carry it, such as `TC 1201: SignOffTests.TC1201_SignsOff`. With the Boards organization and project set, each id links to its page in Azure Boards. The link is built from the id, not read from Boards, so it does not show that the Test Case exists; the Chain row does. A method carries an id on its declaration line or on an attribute line up to five lines above it. When no method does, a file whose path carries the id is listed as `<file> (file name)`, else the file whose text does. |
| Chain | One line per work item in the description: the story, its Test Cases in Azure Boards (Tested By links) with their state and tags, the pull request, and the tests that carry each Test Case id, marked `(changed)` when the pull request changes them and `(existing)` when they are already on its head. `Boards not read:` and the reason when Boards cannot be read. |

For a Test Case no changed test carries, the Chain row searches the test files on the pull request's head (the
`uiTests` and `unitTests` globs) that contain the id, reading them in path order up to `searchLimit` (2,000 files and
20 MB unless `harness.json` says otherwise). When it stops there, the row ends with `searched first <n> test files`.

`harness scope <pr> --json` prints the same facts as one line of JSON; the workflow keeps it as a run artifact for
30 days. Its `run` object adds what the run cost: `durationMs` and the `gitCalls`, `ghCalls` and `azCalls` it made.
The comment leaves them out, because a comment that differs on every push would be edited for nothing.

## Install (one pull request)

1. Copy this folder to `utils/harness` in the repository.
2. Copy `harness.json` to the repository root, replacing any older one, and set it (see below).
3. Copy `.github/workflows/harness-scope.yml` into the repository's `.github/workflows`.
4. Copy `.github/pull_request_template.md` into the repository's `.github` folder, or add its Work items prompt to
   the template already there.
5. Open the pull request and merge it. The workflow builds the harness from each pull request's *base* commit, so
   the pull request that adds it gets no comment; every pull request after the merge does.

Requirements: GitHub Actions, with `git`, `gh` and the .NET SDK on the runner. The workflow names `ubuntu-latest`,
and its steps are bash; the harness itself runs nothing but `git`, `gh` and `az`, so a macOS runner works as is and
a Windows runner works with `shell: bash` on the steps. The workflow declares its own permissions
(`pull-requests: write` for the comment), so the repository's default `GITHUB_TOKEN` setting can stay read-only.

## harness.json

The harness reads `harness.json` from the pull request's base branch, never from the pull request, so a change
cannot rewrite the rules that describe it. The file holds only what differs from the defaults below; the template
holds `version` and `boards`. Add a `testImpact` key only where the repository differs, for example the NUnit
projects' globs as `uiTests`. Without the file every default applies. An unknown key is an error, so a typo does not
pass silently, and an older `harness.json` with keys such as `robot` or `workflows` must be replaced.

| Key | Default | What it is |
|---|---|---|
| `version` | required | Always `1`. |
| `testImpact.uiTests` | `["uitest/**/*.cs"]` | Globs of the UI test sources. |
| `testImpact.unitTests` | `["**/*Tests/**/*.cs", "test/**/*.cs", "tests/**/*.cs"]` | Globs of the unit test sources. `utils/harness/` is never searched, because its tests name made-up Test Case ids. |
| `testImpact.testCaseIdPattern` | `(?<![0-9A-Za-z])TC[_-]?([0-9]{3,7})(?![0-9])` | How a test names its Azure Boards Test Case; group 1 is the id. The default finds `TC1201`, `TC_1201`, `TC-1201` and `Opens_TC1201`, but not `ATC1201`, and needs three to seven digits. |
| `testImpact.platforms` | `android`: `**/Platforms/Android/**`, `**/*.Android.cs`, `**/*.android.cs`; `ios`: `**/Platforms/iOS/**`, `**/*.iOS.cs`, `**/*.ios.cs` | The globs of each platform's code, by lower-case name. Setting it replaces both defaults. `#if <NAME>` in a changed file counts too. |
| `testImpact.sourceExtensions` | `[".cs", ".xaml"]` | The files, by extension, whose content is read for `#if <PLATFORM>` and which count as shared code when no platform glob matches. Setting it replaces both defaults. Each entry starts with a dot; case does not matter. |
| `testImpact.searchLimit.files` | `2000` | The most test files the Chain row reads on the head when it looks for a Test Case id no changed test carries. |
| `testImpact.searchLimit.megabytes` | `20` | The most megabytes that search reads over all its files. |
| `boards.organization` | none | The Azure DevOps organization URL, as `az devops` takes it for `--org`: `https://dev.azure.com/<org>` or `https://<org>.visualstudio.com`. The `ADO_ORG` environment variable wins. Any other host, or a value that is not an https URL such as the template's placeholder, makes the Chain row say so and nothing is sent: the Boards token goes to whatever host this names. |
| `boards.project` | none | The project name. The `ADO_PROJECT` environment variable wins. |

Globs are relative to the repository root. A pattern that does not compile, or has no capture group, is an error
that names the key. A pattern that takes longer than one second on a file is an error that names the key and the
file.

Fixed limits, the same for every repository, so a pull request cannot make the report slow or misleading: the first
300 changed source files are read for `#if` directives, the rest are classified by path; a file on the pull request's
head larger than 4 MB is classified by its path only; a line longer than 4 KB is skipped when looking for the method
that carries a Test Case id; the Chain row's search of the head stops at `searchLimit`. Platform names print as
written in `harness.json`, except `android` and `ios`, which print as Android and iOS.

### What you can set, what is fixed, what is detected

Every value the harness uses is one of three kinds, and the kind decides where it lives. The question that sorts a
value: would two teams adopting the harness legitimately want it different?

| Kind | Where it lives | Examples |
|---|---|---|
| **Policy**: yes, teams differ | `harness.json`, as an override with the default in code | Test globs, the Test Case id pattern, platform globs, source extensions, the search limit, the Boards organization and project |
| **Contract**: no, readers depend on it | Fixed in code and written down here | The `Change scope` heading and its comment marker, the `AB#` work item syntax, the Tested By relation, the `Test Case` type, the exit codes, the `ADO_ORG` and `ADO_PROJECT` names, the Azure DevOps hosts, the work item page address (`<organization>/<project>/_workitems/edit/<id>`), the fixed limits above |
| **Environment**: the machine already knows | Detected at run time, never configured | The remote the base branch is fetched from, the operating system (`az` or `az.cmd`), the runner, the CI host |

So a new team setting changes `harness.json` and this table only. A contract value changes only with a `version`
bump. Nothing is read from a second configuration channel: no flags that shadow `harness.json`, and the only
environment variables are `ADO_ORG` and `ADO_PROJECT`, which exist so a developer's shell can point at Boards without
editing the committed file.

### How this fits GitHub and Azure DevOps

The harness adds no control surface of its own. It reads the platforms' records and leaves enforcement to the
platforms' own knobs, so each later step is a setting the team already knows, not a harness feature.

| Platform control | Day one | Later, if the team wants it |
|---|---|---|
| GitHub ruleset on `main` | "Require review from Code Owners", with `uitest/**` in CODEOWNERS. The report is a comment and never blocks. | "Require status checks to pass" naming a `harness` check run, once the report also posts one. The name is contract, so the ruleset entry is written once. |
| GitHub Actions | `harness-scope.yml` on `pull_request`. Runner, SDK and timeout are the workflow's own keys. | Workload identity federation to Entra for Boards reads in CI ([docs/azure-devops.md](docs/azure-devops.md)). |
| Azure Boards | `AB#<id>` in the description is Boards' own GitHub link syntax; Tested By is Boards' own relation. The harness reads both and writes neither. | Tag and state rules (`needs-qa`, `AI-Drafted`, Ready) stay Boards process settings. The harness reports on them. |
| Azure Pipelines | Not used. | The CLI takes the pull request and reads `git` and `gh`, so it runs in any CI host. A pipeline would call it the same way and gate with a branch policy status check named `harness`. |

## How the Chain row reads Azure Boards

The harness reads each work item with `az rest` (GET only, `api-version=7.1`, with relations); on Windows it runs
`az.cmd`. `az rest` takes its token from `az login`: no extension and no personal access token. A call that takes
longer than 60 seconds counts as Boards being unreachable.

- **Locally:** `az login`, then `harness scope "#57"`. Set `ADO_ORG` and `ADO_PROJECT` in your shell if
  `harness.json` does not name them.
- **In CI:** not yet. The workflow does not sign in to Azure, so the Chain row says `Boards not read`. Signing in
  with workload identity federation is stage 2: see [docs/azure-devops.md](docs/azure-devops.md).

A work item that does not exist, or that the caller may not read, is listed as `not found in Boards`. A Boards that
cannot be reached (no `az`, not logged in, no organization and project, a timeout) gives `Boards not read:` and the
line of az's output that says why; the rest of the report is unchanged.

## Command line

From inside a clone of the repository (`gh auth login` first). The pull request is `#57`, `57`, or its address as
GitHub shows it in the browser (ending in `/pull/57`, with or without a tab such as `/files`); an address in
another repository is a usage error.

```
dotnet run --project utils/harness/src/Harness.Cli -- scope "#57"          # the report as Markdown
dotnet run --project utils/harness/src/Harness.Cli -- scope "#57" --json   # one line of JSON
dotnet run --project utils/harness/src/Harness.Cli -- scope "#57" -v       # debug logs on stderr
```

Logs go to stderr, the report to stdout. Every run logs its duration and call counts, a failed one included.
`--verbose` (`-v`) also logs each `git`, `gh` and `az` call: the tool, its first argument, the exit code and the time.
No log carries a body, a diff or a token.

The base branch must be fetched from a remote (`origin` wins when several have it), and the pull request's head
commit must be in the clone. In a fork clone, run it where `origin` is the repository the pull request belongs to:
`gh` reads the pull request from the repository it resolves, and the diff is taken against the base as fetched from
`origin`, so a stale fork `main` would report commits already merged upstream as changed. Exit codes: 0 success,
2 usage (bad arguments, not a git repository, invalid `harness.json`, a base on no remote or on several, a ref not
fetched, an address in another repository), 3 pull request not found, 5 GitHub unreachable, unreadable, or a git or
gh call still running after 10 minutes, 130 cancelled by Ctrl+C or SIGTERM (143 when a SIGTERM was not honoured within
two seconds).

`git`, `gh` and `az` are found on `PATH` only, never in the current directory, so a file of that name in the
repository is never run.

## Layout

| Path | What |
|---|---|
| `src/Harness` | The library: `harness.json`, git and GitHub reads, test impact, the Boards provider and the Chain row |
| `src/Harness.Cli` | The `harness` command line |
| `tests/Harness.Tests` | Unit tests. `dotnet test` from this folder |
| `docs/` | Azure Boards sign-in for CI (stage 2) |

The folder has its own `global.json`, `Directory.Build.props` and `Directory.Packages.props`, so it builds the same
way inside the monorepo as on its own, and it does not change how the app builds.
