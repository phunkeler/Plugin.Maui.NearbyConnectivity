#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Renders a TRX file as Markdown for the GitHub Actions run summary.

.DESCRIPTION
    A TRX is XML, so this needs no tooling beyond PowerShell's [xml] cast.

    Failures are the output. A green run prints its counters and stops -- a passing test has
    nothing to say, and 501 rows of green tick marks is a thing people scroll past, not read.
    Failures print the error, the stack, and (device runs) the structured log lines that test
    emitted.

    The parsing lives in NearbyBuild.psm1 and is covered by NearbyBuild.Tests.ps1; this file is the
    rendering shell around it.

    On the device path, TestOutputLogger writes one JSON object per line into StdOut, so a failure
    can show the level/eventName/fields from the phone itself -- there is no other way to see what
    the radio did on a run that already finished.

    There is no coverage section, and there cannot be one: neither dotnet-coverage nor coverlet can
    instrument the Android/iOS app runtimes, so the device runs produce a TRX and nothing else.
    ReportGenerator does not take TRX as input. Coverage stays the unit suite's job.

.PARAMETER Path
    The .trx file to render.

.PARAMETER Title
    Heading for the summary section, e.g. "iOS".
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Title
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module "$PSScriptRoot/NearbyBuild.psm1" -Force

if (-not (Test-Path $Path)) {
    "## $Title`n`nNo TRX produced -- the run failed before tests reported."
    exit 0
}

[xml]$trx = Get-Content $Path
$summary = Get-TrxSummary -Trx $trx
$icon = if ($summary.Failed -gt 0) { ':x:' } else { ':white_check_mark:' }

"## $icon $Title"
""
"| Total | Passed | Failed | Duration |"
"|---|---|---|---|"
"| $($summary.Total) | $($summary.Passed) | $($summary.Failed) | $([math]::Round($summary.Duration.TotalSeconds, 1))s |"
""

# Skipped tests are a silent hole otherwise: they are neither passed nor failed, so a run that
# quietly stopped running half the suite still looks green in the counters above.
$skipped = @($summary.Results | Where-Object { $_.outcome -ne 'Passed' -and $_.outcome -ne 'Failed' })
if ($skipped.Count -gt 0) {
    "$($skipped.Count) test(s) not run: " + (($skipped | ForEach-Object { $_.testName }) -join ', ')
    ""
}

foreach ($r in $summary.Results | Where-Object { $_.outcome -eq 'Failed' }) {
    "<details open><summary><b>$($r.testName)</b></summary>"
    ""
    '```'
    $r.Output.ErrorInfo.Message
    $r.Output.ErrorInfo.StackTrace
    '```'
    ""

    $stdout = if ($r.Output.PSObject.Properties['StdOut']) { $r.Output.StdOut } else { $null }
    $noisy = Select-NoisyLogLine -StdOut $stdout

    if ($noisy) {
        "Log (warning and above):"
        ""
        '```'
        $noisy | ForEach-Object { "$($_.level) $($_.eventName) $($_.message)" }
        '```'
    }
    "</details>"
    ""
}
