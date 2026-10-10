# Contributing

Thanks for helping. This page covers the mechanics: setup, build, test, and land a change.
Design and behavior are documented on the [docs site](https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/).

## Branching

GitHub Flow, trunk-based. `main` is the only long-lived branch and is always releasable.

- Branch from `main`, named `feat/…`, `fix/…`, `docs/…`, or `chore/…`.
- Every change lands by pull request. Nothing is pushed to `main` directly.
- Pull requests are squash-merged. The PR title becomes the commit subject and the PR body becomes
  the commit body, so write both as if they were the commit message.
- The branch is deleted on merge. There is no `develop`, no `release/*`, no hotfix branch. A hotfix
  is a normal PR followed by a release PR.

## Prerequisites

| Tool | Version | Notes |
| --- | --- | --- |
| .NET SDK | Exactly the version in `global.json` | `rollForward` is disabled, so a newer SDK does not satisfy it |
| MAUI workloads | Matching the SDK | `dotnet workload restore src/Plugin.Maui.NearbyConnectivity/Plugin.Maui.NearbyConnectivity.csproj` |
| Local tools | Pinned in `.config/dotnet-tools.json` | `dotnet tool restore` after cloning |
| PowerShell 7 | `pwsh` on `PATH` | Every script in `scripts/` is PowerShell |
| Android SDK / Xcode | For device and UI tests only | Unit tests need neither |
| Node.js | For plugin UI tests only | The script installs Appium on first use |

```bash
dotnet tool restore
dotnet workload restore src/Plugin.Maui.NearbyConnectivity/Plugin.Maui.NearbyConnectivity.csproj
```

## Build and test

```bash
pwsh scripts/Invoke-Tests.ps1              # build the library and run the unit suite
pwsh scripts/Invoke-Tests.ps1 -Coverage    # also write artifacts/coverage.xml
```

This is the same script CI runs. The unit suite is xunit.v3 on Microsoft.Testing.Platform, so
`dotnet test` runs zero tests against it and still exits 0. Use the script, which executes the
runner directly and fails on an empty run.

The repository has four test suites. Each suite uses the same command locally and in CI, so you
can reproduce a failed CI run on your machine.

| Suite | Project | Runs on | Answers |
| --- | --- | --- | --- |
| Unit | `test/Plugin.Maui.NearbyConnectivity.UnitTests` | Desktop `net10.0` | Does the shared logic work when the native transport is a fake? |
| Build scripts | `scripts/NearbyBuild.Tests.ps1` | Any host with `pwsh` and Pester 6.2.0 | Do the script helpers read a TRX file and fail on an empty run? |
| Device | `test/Plugin.Maui.NearbyConnectivity.DeviceTests` | Android emulator, iOS simulator | Does the real native transport start, report availability, and stop cleanly? |
| Plugin UI | `test/Plugin.Maui.NearbyConnectivity.UiTests` | Android emulator, iOS simulator, through Appium | Does a MAUI app built on the plugin show what the API promises? |

The device script creates and starts the emulator or simulator itself. It collects no coverage on
the device.

```bash
pwsh scripts/device-tests.ps1 -Platform android                           # on-device xunit, latest API level from .config/android-api-levels.json
pwsh scripts/device-tests.ps1 -Platform android -AndroidApiLevel minimum
pwsh scripts/device-tests.ps1 -Platform ios                               # macOS with Xcode only
pwsh scripts/device-tests.ps1                                             # both platforms, except those that the host cannot run
pwsh scripts/device-tests.ps1 -Platform android -Suite UiTests            # Appium against the host app
pwsh scripts/device-tests.ps1 -Platform ios -Suite UiTests
```

The build scripts have their own tests. They need [Pester](https://pester.dev) 6.2.0:

```powershell
Install-Module Pester -RequiredVersion 6.2.0 -Scope CurrentUser
Invoke-Pester scripts/NearbyBuild.Tests.ps1
```

### Tests that need a second device

A few device tests and plugin UI tests need a real peer in radio range, and one emulator or
simulator cannot provide a peer. These tests have `[Trait("Category", "RequiresRealPeer")]`. The
scripts exclude them with `--filter 'Category!=RequiresRealPeer'`, so no CI run executes them. To
run them:

1. Connect two devices.
2. For the plugin UI tests, set the `PEER_DEVICE_ID` environment variable to the endpoint id of the
   other device.
3. Run the `dotnet test` command from `scripts/device-tests.ps1` without the filter.

The `RequiresRealPeer` device tests run on Android only. On iOS, test two-device behaviour by hand
before a release: discover, connect, send bytes and a file, and cancel an inbound file.

### Sample icon font

`samples/NearbyChat/Scripts/makefont.sh` builds `Resources/Fonts/NearbyChatIcons.ttf` from the SVGs
in `Resources/FontIcons`. To rebuild the font, install
[fantasticon](https://github.com/tancredi/fantasticon) and run the script from its folder.

### Documentation

Prose lives in `docs/`, built with DocFX. Preview it at <http://localhost:8080>:

```bash
dotnet docfx docs/docfx.json --serve
```

The `Docs` workflow builds the site and checks every link on the pull request, so a renamed page
fails there rather than after the merge.

### Build rules

- `TreatWarningsAsErrors` is on for every project. `AnalysisLevel` is `latest-all` for the library
  and `latest-recommended` for tests and samples. A warning is a build failure.
- Code style (`.editorconfig`) is enforced in the build: naming, formatting, unused usings, and
  `readonly` fields.
- Formatting is checked in CI. Run `dotnet format whitespace --folder --exclude '**/obj/**' '**/bin/**'`
  before you push.
- CA2007 is an error: every `await` in the library takes `.ConfigureAwait(false)`.
- Every public and protected member has an XML doc comment. Missing docs fail the build.
- Package versions live in `Directory.Packages.props` (central package management). Do not put a
  `Version` on a `PackageReference`.

### Public API tracking

The library uses `Microsoft.CodeAnalysis.PublicApiAnalyzers`. Any public surface change fails the
build with RS0016/RS0017 until it is recorded in
`src/Plugin.Maui.NearbyConnectivity/PublicAPI/PublicAPI.Unshipped.txt`.

1. Build. Read the RS0016 errors.
2. Add the listed lines to `PublicAPI.Unshipped.txt` (removals go under `*REMOVED*`).
3. Commit the text file with the code change.

An API marked `[Experimental("NEARBY001")]` has its lines prefixed with `[NEARBY001]`, and code in
the library that uses it suppresses `NEARBY001` with a `#pragma` around the one statement. To make
the API stable, remove the attribute, the prefixes, and the pragmas in the same commit.

While iterating you can skip the analyzer with `-p:PublicApiType=Skip`. Never commit with it
skipped, and never edit `PublicAPI.Shipped.txt`: that file is empty until 1.0 and is only updated
at a release by moving the Unshipped contents into it.

### `Directory.Build.local.props`

The root `Directory.Build.props` imports an optional, gitignored `Directory.Build.local.props`
last, so its values win. Use it for machine-only settings that must not reach CI. The one known
case is an Intel Mac pinned to an Xcode the iOS workload manifest rejects:

```xml
<Project>
  <PropertyGroup>
    <ValidateXcodeVersion>false</ValidateXcodeVersion>
  </PropertyGroup>
</Project>
```

If a fresh clone needs this file to build, that is a bug. Open an issue.

## Pull requests

Fill in the template. The checklist is short because CI enforces the rest. Contributions are
licensed under the [MIT License](LICENSE), with no CLA or sign-off, and everyone taking part follows
the [Code of Conduct](CODE_OF_CONDUCT.md).

- Every user-facing change adds a line under `## [Unreleased]` in `CHANGELOG.md`, in one of
  `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, or `Security`. That file is the single
  source of release notes.
- Label the PR: `breaking`, `enhancement`, `bug`, `documentation`, or `dependencies`. The label
  puts the PR in the right section of the auto-generated "What's Changed" list on the release.
- Any XAML element a UI test touches gets an `AutomationId`.

CI on a PR runs the unit suite with a line-coverage gate, SonarQube Cloud analysis, a vulnerable
dependency scan, dependency review, and the device-test matrix when plugin or test sources change.

## Releasing

Versions come from `version.json` ([Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning)),
never from a tag. The release creates the tag.

1. Open a release PR that bumps `version` in `version.json` and moves the CHANGELOG
   `## [Unreleased]` section to `## [x.y.z] - YYYY-MM-DD`, adding the compare link at the bottom.
   Merge it.
2. Dispatch the release:

   ```bash
   gh workflow run release.yml --ref main && gh run watch
   ```

   The `verify` job refuses to run unless the ref is `main`, the version is a public release, no
   `v<version>` tag exists, and the CHANGELOG has a `## [<version>]` section.
3. Approve the `nuget` environment deployment in the Actions UI. That is the only manual step.
   The `publish` job pushes to nuget.org through Trusted Publishing, attests build provenance, and
   creates an immutable GitHub Release whose body is the CHANGELOG section followed by the
   label-categorised PR list.

If `publish` fails after the push to nuget.org, re-run only the failed job. The push skips a
version that already exists, and the attestation and the GitHub Release run again.

At 1.0, the release PR also moves the contents of `PublicAPI.Unshipped.txt` into
`PublicAPI.Shipped.txt`.

## Dependencies

All package versions are in `Directory.Packages.props`. The library itself references
`Microsoft.Maui.Core`, `Microsoft.Extensions.DependencyInjection.Abstractions`,
`Microsoft.Extensions.Logging.Abstractions`, and, on Android only,
`Xamarin.GooglePlayServices.Nearby`. Dependabot groups updates by family; a transport SDK bump
always arrives as its own PR.
