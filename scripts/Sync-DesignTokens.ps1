#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Regenerates the NearbyChat sample's Colors.xaml from its tokens.json.

.DESCRIPTION
    tokens.json is a byte-identical copy of project/tokens.json in the NearbyChat design-system
    artifact. Colors.xaml is a projection of it and is never edited by hand: change the tokens, run
    this, and review the diff.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/NearbyBuild.psm1" -Force

$styles = Join-Path $PSScriptRoot '../samples/NearbyChat/Resources/Styles'
$xaml = ConvertTo-ColorsXaml -Path (Join-Path $styles 'tokens.json')
[System.IO.File]::WriteAllText((Join-Path (Resolve-Path $styles) 'Colors.xaml'), $xaml, [System.Text.UTF8Encoding]::new($false))
