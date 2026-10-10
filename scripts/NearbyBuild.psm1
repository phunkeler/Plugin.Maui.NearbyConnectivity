#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Pure helper functions shared by the repo's build/test scripts.

.DESCRIPTION
    Everything here is input -> output with no process launches and no filesystem writes, so it is
    covered by scripts/NearbyBuild.Tests.ps1. The scripts that dot-source this module keep the
    `dotnet`/emulator invocations to themselves: mocking those would assert the mock, not the build.
#>

Set-StrictMode -Version Latest

function Get-TrxSummary {
    <#
    .SYNOPSIS
        Reduces a TRX document to counters, per-test rows, and failure detail.

    .PARAMETER Trx
        The parsed TRX ([xml]). Taking the document rather than a path keeps this testable
        without touching disk.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][xml]$Trx)

    $counters = $Trx.TestRun.ResultSummary.Counters

    # TRX stores per-test times, not a run total; sum them so the summary reports real test time.
    $duration = [TimeSpan]::Zero
    # An empty run emits <Results /> with no children; under StrictMode reading UnitTestResult off
    # it throws, and Invoke-Tests.ps1 turns that empty run into its own failure, so it must not blow up here.
    $results = if ($Trx.TestRun.Results -and $Trx.TestRun.Results.PSObject.Properties['UnitTestResult']) {
        @($Trx.TestRun.Results.UnitTestResult)
    }
    else { @() }
    foreach ($r in $results) {
        # A crashed or skipped test has no duration attribute at all; under StrictMode reading a
        # missing XML attribute throws, so probe the property before touching it.
        $value = if ($r -and $r.PSObject.Properties['duration']) { $r.duration } else { $null }
        if ($value) { $duration += [TimeSpan]::Parse($value) }
    }

    [pscustomobject]@{
        Total    = [int]$counters.total
        Passed   = [int]$counters.passed
        Failed   = [int]$counters.failed
        Duration = $duration
        Results  = $results
    }
}

function Select-NoisyLogLine {
    <#
    .SYNOPSIS
        Picks warning-and-above lines out of a test's StdOut.

    .DESCRIPTION
        TestOutputLogger writes one JSON object per line, so level/eventName/message are queryable
        rather than a wall of prose. Non-JSON lines are dropped. Only warn/fail/crit survive -- a
        passing-level dump would bury the failure.
    #>
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$StdOut)

    if ([string]::IsNullOrWhiteSpace($StdOut)) { return @() }

    @($StdOut -split "`n" | Where-Object { $_.Trim() } | ForEach-Object {
        try { $_ | ConvertFrom-Json -ErrorAction Stop } catch { $null }
    } | Where-Object { $_ -and $_.PSObject.Properties['level'] -and $_.level -in @('warn', 'fail', 'crit') })
}

function ConvertTo-TokenKey {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name)

    ($Name -split '-' | ForEach-Object { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) }) -join ''
}

function ConvertTo-XamlColorValue {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Value, [Parameter(Mandatory)][string]$Owner)

    # tokens.json writes alpha CSS-style (#rrggbbaa); MAUI reads #aarrggbb.
    if ($Value -match '^#[0-9a-fA-F]{8}$') { return '#' + $Value.Substring(7, 2) + $Value.Substring(1, 6) }
    if ($Value -match '^#[0-9a-fA-F]{6}$') { return $Value }
    throw "Token '$Owner' has value '$Value', which is neither an alias nor a hex colour."
}

function Resolve-TokenSide {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Value, [Parameter(Mandatory)][string[]]$Primitives, [Parameter(Mandatory)][string]$Owner)

    if ($Value -match '^\{([^}]+)\}$') {
        # StaticResource resolves at parse time, so an alias may only point at a primitive, which is
        # always emitted first.
        if ($Primitives -notcontains $Matches[1]) {
            throw "Token '$Owner' references '$($Matches[1])', which is not a primitive."
        }
        return '{StaticResource ' + (ConvertTo-TokenKey $Matches[1]) + '}'
    }
    ConvertTo-XamlColorValue -Value $Value -Owner $Owner
}

function ConvertTo-ColorsXaml {
    <#
    .SYNOPSIS
        Projects the colour tokens of a tokens.json into Colors.xaml text.

    .DESCRIPTION
        Reads color.tokens only. A hex token becomes a <Color> (alpha bytes reordered), an alias
        becomes a <StaticResource>, and a {light, dark} token becomes a toolkit:AppThemeColor.
        A side or alias that names anything but a primitive fails the run rather than emitting a key
        that cannot resolve. Output order is primitives, then the rest in file order.

    .PARAMETER Path
        Path to tokens.json.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $tokens = @((Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json).color.tokens)
    $isPrimitive = { param($t) $t.value -is [string] -and $t.value -match '^#[0-9a-fA-F]{6}$' -and $t.usage -like 'Primitive*' }
    $primitives = @($tokens | Where-Object { & $isPrimitive $_ })
    $semantic = @($tokens | Where-Object { -not (& $isPrimitive $_) })
    $primitiveNames = @($primitives.name)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('<?xml version="1.0" encoding="utf-8" ?>')
    $lines.Add('<ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui"')
    $lines.Add('                    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"')
    $lines.Add('                    xmlns:toolkit="http://schemas.microsoft.com/dotnet/2022/maui/toolkit"')
    $lines.Add('                    x:Class="NearbyChat.Resources.Styles.Colors">')
    $lines.Add('')
    $lines.Add("    <!-- Generated from $(Split-Path $Path -Leaf) by pwsh scripts/Sync-DesignTokens.ps1. -->")
    $lines.Add('    <!-- Do not edit this file: change tokens.json and run the script again.              -->')

    foreach ($section in @(
            @{ Title = 'PRIMITIVES'; Tokens = $primitives },
            @{ Title = 'SEMANTIC'; Tokens = $semantic })) {
        $lines.Add('')
        $lines.Add("    <!-- $($section.Title) -->")
        $group = $null
        foreach ($t in $section.Tokens) {
            $firstWord = ($t.name -split '-')[0]
            if ($firstWord -ne $group) {
                $lines.Add('')
                $group = $firstWord
            }

            $key = ConvertTo-TokenKey $t.name
            $lines.Add('    <!-- ' + ($t.usage -replace '--', '-') + ' -->')
            if ($t.value -isnot [string]) {
                $light = Resolve-TokenSide -Value $t.value.light -Primitives $primitiveNames -Owner $t.name
                $dark = Resolve-TokenSide -Value $t.value.dark -Primitives $primitiveNames -Owner $t.name
                $lines.Add("    <toolkit:AppThemeColor x:Key=`"$key`" Light=`"$light`" Dark=`"$dark`"/>")
            }
            elseif ($t.value -match '^\{') {
                $null = Resolve-TokenSide -Value $t.value -Primitives $primitiveNames -Owner $t.name
                $target = ConvertTo-TokenKey $t.value.Trim('{', '}')
                $lines.Add("    <StaticResource x:Key=`"$key`" Key=`"$target`"/>")
            }
            else {
                $lines.Add("    <Color x:Key=`"$key`">" + (ConvertTo-XamlColorValue -Value $t.value -Owner $t.name) + '</Color>')
            }
        }
    }

    $lines.Add('')
    $lines.Add('</ResourceDictionary>')
    ($lines -join "`n") + "`n"
}

Export-ModuleMember -Function Get-TrxSummary, Select-NoisyLogLine, ConvertTo-ColorsXaml
