#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the library and runs the unit suite, optionally with coverage.

.DESCRIPTION
    The one test entry point for CI and local runs, so the build/test/coverage commands live in one
    place instead of being duplicated across workflows.

    The unit suite is xunit.v3 / Microsoft.Testing.Platform with OutputType=Exe. `dotnet test` runs
    zero tests against it and still exits 0, so the runner .dll is executed directly via
    `dotnet exec` and the resulting TRX is checked for a non-zero test count.
    A green CI run on an empty suite is the failure mode this guards.

.PARAMETER Coverage
    Also collect coverage via dotnet-coverage using test/coverage.runsettings, in the `-f xml`
    format the Sonar scanner and ReportGenerator both read.

.PARAMETER CoveragePath
    Where to write the coverage XML. Defaults to artifacts/coverage.xml. CI passes an explicit path
    because `dotnet-sonarscanner begin` is told the report location before this script runs, and the
    two must agree.

.PARAMETER Configuration
    Build configuration. Release by default, matching CI.

.EXAMPLE
    ./scripts/Invoke-Tests.ps1

.EXAMPLE
    ./scripts/Invoke-Tests.ps1 -Coverage
#>
[CmdletBinding()]
param(
    [switch]$Coverage,
    [string]$CoveragePath,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module "$PSScriptRoot/NearbyBuild.psm1" -Force

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Library = Join-Path $RepoRoot 'src/Plugin.Maui.NearbyConnectivity/Plugin.Maui.NearbyConnectivity.csproj'
$UnitTests = 'Plugin.Maui.NearbyConnectivity.UnitTests'
$Artifacts = Join-Path $RepoRoot 'artifacts'
$RunSettings = Join-Path $RepoRoot 'test/coverage.runsettings'

if (-not $CoveragePath) { $CoveragePath = Join-Path $Artifacts 'coverage.xml' }

function Invoke-Native {
    <#
    .SYNOPSIS
        Runs a native command and throws on a non-zero exit code.
    #>
    param([Parameter(Mandatory)][string]$Command, [Parameter(Mandatory)][string[]]$Arguments)

    Write-Host "→ $Command $($Arguments -join ' ')" -ForegroundColor Cyan
    # Stream the child's output to the host rather than returning it: an uncaptured value here
    # would ride the pipeline out of every calling function and corrupt their return values.
    & $Command @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$Command exited $LASTEXITCODE"
    }
}

function Build-Solution {
    param([string]$Configuration)

    Invoke-Native dotnet @('build', $Library, '-c', $Configuration, '--no-incremental')
    Invoke-Native dotnet @('build', (Join-Path $RepoRoot "test/$UnitTests/$UnitTests.csproj"), '-c', $Configuration)
}

function Get-RunnerPath {
    <#
    .SYNOPSIS
        Locates the built unit-test runner .dll.
    #>
    param([string]$Project, [string]$Configuration)

    $dll = Join-Path $RepoRoot "test/$Project/bin/$Configuration/net10.0/$Project.dll"
    if (-not (Test-Path $dll)) {
        throw "Test runner not found at $dll -- did the build succeed?"
    }
    $dll
}

function Invoke-UnitTests {
    param([string]$Project, [string]$TrxName, [string]$Configuration, [switch]$WithCoverage)

    $runner = Get-RunnerPath -Project $Project -Configuration $Configuration
    $trx = Join-Path $Artifacts $TrxName

    # This is xunit.v3's own console runner, not Microsoft.Testing.Platform and not VSTest: the
    # flag is single-dash `-trx` and it takes the output path directly.
    $runnerArgs = @('-trx', $trx)

    if ($WithCoverage) {
        $inner = "dotnet exec `"$runner`" -trx `"$trx`""

        # dotnet-coverage writes the file but not its directory.
        $coverageDir = Split-Path -Parent $CoveragePath
        if ($coverageDir -and -not (Test-Path $coverageDir)) {
            New-Item -ItemType Directory -Force -Path $coverageDir | Out-Null
        }

        Invoke-Native dotnet @('dotnet-coverage', 'collect', $inner, '--settings', $RunSettings, '-f', 'xml', '-o', $CoveragePath)
        Write-Host "Coverage: $CoveragePath"
    }
    else {
        Invoke-Native dotnet (@('exec', $runner) + $runnerArgs)
    }

    $trx
}

New-Item -ItemType Directory -Force -Path $Artifacts | Out-Null

Build-Solution -Configuration $Configuration
$runs = @(
    @{ Title = 'Unit tests'; Trx = (Invoke-UnitTests -Project $UnitTests -TrxName 'unit.trx' -Configuration $Configuration -WithCoverage:$Coverage) }
)

$failed = 0
foreach ($run in $runs) {
    if (-not (Test-Path $run.Trx)) {
        throw "No TRX at $($run.Trx) -- the run produced no results."
    }

    [xml]$trxDoc = Get-Content $run.Trx
    $summary = Get-TrxSummary -Trx $trxDoc

    # Exits 0 on an empty run, so the count is the only real signal that anything executed.
    if ($summary.Total -le 0) { throw 'No tests ran. The runner exited 0 on an empty run -- treating as a failure.' }

    Write-Host "$($run.Title): $($summary.Total) tests, $($summary.Passed) passed, $($summary.Failed) failed in $([math]::Round($summary.Duration.TotalSeconds, 1))s"

    if ($env:GITHUB_STEP_SUMMARY) {
        & "$PSScriptRoot/trx-summary.ps1" -Path $run.Trx -Title $run.Title >> $env:GITHUB_STEP_SUMMARY
    }

    $failed += $summary.Failed
}

if ($failed -gt 0) { exit 1 }
