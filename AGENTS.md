# AGENTS.md

Instructions for coding agents working in this repository. Humans: see `CONTRIBUTING.md`.

Plugin.Maui.NearbyConnectivity is a .NET MAUI library that discovers, connects to, and exchanges
data with nearby devices running the same app, with no server or internet connection. Android uses
Nearby Connections; iOS uses Multipeer Connectivity.

## Branching

Branch from `main` with a `feat/`, `fix/`, `docs/`, or `chore/` prefix. Open a PR. Never push to
`main`. PRs are squash-merged: the PR title becomes the commit subject and the PR body becomes the
commit body. Title format is `type(scope): imperative summary`, for example
`fix(sample): re-measure a chat bubble when its send fails`. Scope is optional.

## Repo map

| Path | What |
| --- | --- |
| `src/Plugin.Maui.NearbyConnectivity/` | The library. TFMs `net10.0-android`, `net10.0-ios`, `net10.0`. Platform code is in `*.android.cs` / `*.ios.cs` / `*.net.cs` files. |
| `src/Plugin.Maui.NearbyConnectivity/PublicAPI/` | Tracked public surface (`PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt`). |
| `samples/NearbyChat/` | Sample app. Mobile TFMs only. |
| `samples/Minimal/` | The tutorial's app. `docs/` includes its snippets, and CI builds it for Android. |
| `test/Plugin.Maui.NearbyConnectivity.UnitTests/` | xunit.v3 on `net10.0`, native transport faked. Runs on a desktop. |
| `test/Plugin.Maui.NearbyConnectivity.DeviceTests/` + `.Runner/` | On-device xunit through DeviceRunners: does the real transport start, report availability, and tear down. |
| `test/Plugin.Maui.NearbyConnectivity.UiTests/` + `.Host/` | Appium page-object tests against a MAUI host app built on the plugin. Single device, emulator or simulator. |
| `scripts/` | PowerShell build and test scripts. `NearbyBuild.psm1` has its own Pester tests. |
| `docs/` | DocFX site: conceptual pages plus generated API reference. The only place for prose docs. |
| `.github/workflows/` | `ci.yml` (unit + Sonar + coverage gate), `device-tests.yml`, `docs.yml`, `release.yml` (dispatch only). |
| `.claude/` | Shared Claude Code config: `settings.json` (permission allowlist, MCP approval) and `launch.json` (docs preview). Personal overrides go in the gitignored `settings.local.json`. |

## Commands

```bash
dotnet tool restore                                                # once per clone
dotnet workload restore src/Plugin.Maui.NearbyConnectivity/Plugin.Maui.NearbyConnectivity.csproj

pwsh scripts/Invoke-Tests.ps1                                      # build library + run unit tests (what CI runs)
pwsh scripts/Invoke-Tests.ps1 -Coverage                            # + artifacts/coverage.xml
pwsh -c 'Invoke-Pester scripts/NearbyBuild.Tests.ps1'              # test the build scripts (Pester 6.2.0)
pwsh scripts/device-tests.ps1 -Platform android -Suite DeviceTests # on-device tests, emulator
pwsh scripts/device-tests.ps1 -Platform ios -Suite UiTests         # Appium plugin UI tests, simulator

dotnet build src/Plugin.Maui.NearbyConnectivity/Plugin.Maui.NearbyConnectivity.csproj -c Release   # library only
dotnet build src/Plugin.Maui.NearbyConnectivity/Plugin.Maui.NearbyConnectivity.csproj -p:PublicApiType=Skip   # iterate without the API analyzer
dotnet restore src/Plugin.Maui.NearbyConnectivity/Plugin.Maui.NearbyConnectivity.csproj -p:TargetFrameworks=net10.0   # once, docfx does not restore
dotnet docfx docs/docfx.json --serve                                      # docs site locally
```

`dotnet test` on the unit project runs zero tests and exits 0. Do not use it as evidence.

## Hard rules

- `TreatWarningsAsErrors` is on. A warning is a failure. Fix it; do not suppress it. If the fix
  is the defect, ask first, then suppress at the narrowest scope with a `Justification`. No new
  `NoWarn`.
- CA2007 is an error: `.ConfigureAwait(false)` on every `await` in `src/`.
- Every public or protected member has an XML doc comment.
- A `//` comment in a `.cs` file records what the code cannot say: an invariant that a refactor
  would silently break (ordering, atomicity, threading, "do not weaken this to X"), or a platform
  SDK behavior that the code works around. A comment that narrates what the next line does is not
  allowed; if code needs one, change the code. Tests use `// Arrange`, `// Act`, `// Assert` with no
  text after them.
- The public API never exposes bytes from a platform format. Expose a derived value or a
  derivation method, so that the platform's byte layout stays internal.
- Any public API change updates `PublicAPI.Unshipped.txt` in the same commit, following
  `CONTRIBUTING.md` › Public API tracking.
- Every user-facing change adds a line under `## [Unreleased]` in `CHANGELOG.md`.
- Any view a UI test touches has an `AutomationId`.
- No prose documentation outside `docs/` and the root Markdown files. No `README.md` in
  subfolders, no design notes in the tree.
- Package versions go in `Directory.Packages.props`, never on a `PackageReference`. Use the
  latest stable release. Never lower a version floor.
- Never `dotnet pack` the solution. Pack only the library project, and only `release.yml` does that
  for real.
- Do not rewrite history on `main`.
- Never bypass commit signing or hooks (`--no-gpg-sign`, `--no-verify`).

## Done means

- `pwsh scripts/Invoke-Tests.ps1` passes, and the library builds without `-p:PublicApiType=Skip`.
- `dotnet format whitespace --folder --exclude '**/obj/**' '**/bin/**' --verify-no-changes` exits 0.
- A change to transport or platform code is not verified until the device tests run. If you did
  not run them, say so.
- A green build is not evidence that a refactor works. Refactors here fail at run time. Re-read
  every call site you rewrote.
- A change under `scripts/` runs `pwsh -c 'Invoke-Pester scripts/NearbyBuild.Tests.ps1'`.
- Tests tagged `RequiresRealPeer` need two devices and never run in CI. If a change touches what
  they cover and you did not run them, say so.

## Gotchas

- Ask before committing. A plan that names a branch is not permission to commit to it.
- Other agent sessions edit this tree at the same time. Run `git status` and re-read a file
  before you edit it.
- A two-device bug report is not traced until both devices are confirmed to run the current build.
  Stale peers have produced false bugs.
- A claim that an API member does not exist is checked against `PublicAPI.Unshipped.txt` before
  anything else.
- Naming: the bindable types are `NearbyDeviceCollection` and `NearbySessionState`, never
  "adapter". Never name a type `Identity`. Prefer one small record over parallel primitives, even
  when that is a breaking rename.
- `samples/NearbyChat/Resources/Styles/tokens.json` is synced from the design system; do not hand
  edit it. Audits list changes to the sample UI and the token pipeline; they do not apply them.
- Side-loaded Debug APKs need `EmbedAssembliesIntoApk=true`. On an Intel Mac, launch the iOS
  simulator with `xcrun simctl install` and `launch`; `-t:Run` rejects every simulator.
- `docs/` is short technical English: each warning stated once, no design-defence pages, headings
  kept stable. `README.md` uses developer terms and API names.
- In `docs/`, link the first mention of each API member on a page with
  ``[`NearbyDevice.Id`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.Id)``. Methods take the
  overload UID (`...NearbyDevice.ConnectAsync*`). The strict docs build fails on an unknown UID.
  .NET, MAUI, Android, and iOS types link to Microsoft Learn: by xref when the build resolves the
  UID (only types the library references), otherwise by a plain `learn.microsoft.com/dotnet/api/`
  URL. No xref service works: `xref.docs.microsoft.com` has a bad certificate.

## Do not touch

- `PublicAPI.Shipped.txt`. It is empty until 1.0 and only changes in a release PR.
- `version.json` outside a release PR.
- The `Check for vulnerable dependencies` step in `ci.yml` and `release.yml` are identical on
  purpose. Change both or neither.

## Release

Two commands, one click. See `CONTRIBUTING.md` › Releasing. Agents may open the release PR and
dispatch `release.yml`; the `nuget` environment approval is a human's.
