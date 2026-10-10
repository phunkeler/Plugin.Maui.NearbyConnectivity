#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Pester coverage for scripts/NearbyBuild.psm1.

.DESCRIPTION
    Run: pwsh -c 'Invoke-Pester scripts/NearbyBuild.Tests.ps1'
    Only pure logic is covered -- the dotnet/emulator invocations in the calling scripts are
    verified by CI actually running them, not by mocks.
#>

BeforeAll {
    Import-Module "$PSScriptRoot/NearbyBuild.psm1" -Force
}

Describe 'Get-TrxSummary' {
    It 'reads counters and sums per-test durations' {
        [xml]$trx = @'
<TestRun><ResultSummary><Counters total="3" passed="2" failed="1" /></ResultSummary>
<Results>
<UnitTestResult testName="N.C.A" outcome="Passed" duration="00:00:01.5000000" />
<UnitTestResult testName="N.C.B" outcome="Passed" duration="00:00:00.5000000" />
<UnitTestResult testName="N.C.D" outcome="Failed" duration="00:00:02.0000000" />
</Results></TestRun>
'@
        $s = Get-TrxSummary -Trx $trx
        $s.Total | Should -Be 3
        $s.Passed | Should -Be 2
        $s.Failed | Should -Be 1
        $s.Duration.TotalSeconds | Should -Be 4
        $s.Results.Count | Should -Be 3
    }

    It 'handles a run with no results' {
        [xml]$trx = '<TestRun><ResultSummary><Counters total="0" passed="0" failed="0" /></ResultSummary><Results /></TestRun>'
        $s = Get-TrxSummary -Trx $trx
        $s.Total | Should -Be 0
        $s.Duration | Should -Be ([TimeSpan]::Zero)
    }

    It 'tolerates a result with no duration attribute' {
        [xml]$trx = @'
<TestRun><ResultSummary><Counters total="1" passed="0" failed="1" /></ResultSummary>
<Results><UnitTestResult testName="N.C.A" outcome="Failed" /></Results></TestRun>
'@
        { Get-TrxSummary -Trx $trx } | Should -Not -Throw
        (Get-TrxSummary -Trx $trx).Duration | Should -Be ([TimeSpan]::Zero)
    }
}

Describe 'Select-NoisyLogLine' {
    It 'keeps warn and above, drops info' {
        $stdout = @(
            '{"level":"info","eventName":"Started","message":"go"}'
            '{"level":"warn","eventName":"Retry","message":"retrying"}'
            '{"level":"crit","eventName":"Died","message":"boom"}'
        ) -join "`n"
        $r = Select-NoisyLogLine -StdOut $stdout
        $r.Count | Should -Be 2
        $r[0].level | Should -Be 'warn'
        $r[1].eventName | Should -Be 'Died'
    }

    It 'drops non-JSON lines without throwing' {
        $stdout = "not json at all`n{`"level`":`"fail`",`"eventName`":`"E`",`"message`":`"m`"}`n   "
        $r = Select-NoisyLogLine -StdOut $stdout
        $r.Count | Should -Be 1
        $r[0].level | Should -Be 'fail'
    }

    It 'drops JSON objects with no level field' {
        $r = Select-NoisyLogLine -StdOut '{"eventName":"NoLevel"}'
        $r.Count | Should -Be 0
    }

    It 'returns empty for empty input' {
        (Select-NoisyLogLine -StdOut '').Count | Should -Be 0
        (Select-NoisyLogLine -StdOut $null).Count | Should -Be 0
    }
}

Describe 'ConvertTo-ColorsXaml' {
    BeforeAll {
        function New-Tokens($Tokens) {
            $path = Join-Path $TestDrive 'tokens.json'
            @{ color = @{ tokens = $Tokens } } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $path
            $path
        }
        $script:primitives = @(
            @{ name = 'slate-50'; value = '#f8fafc'; usage = 'Primitive.' },
            @{ name = 'slate-900'; value = '#0f172a'; usage = 'Primitive.' },
            @{ name = 'blue-600'; value = '#2563eb'; usage = 'Primitive.' }
        )
    }

    It 'emits a primitive as a Color' {
        $xaml = ConvertTo-ColorsXaml -Path (New-Tokens $primitives)
        $xaml | Should -Match '<Color x:Key="Slate900">#0f172a</Color>'
    }

    It 'emits an alias as a StaticResource' {
        $path = New-Tokens ($primitives + @{ name = 'accent-advertising'; value = '{blue-600}'; usage = 'Fill.' })
        ConvertTo-ColorsXaml -Path $path | Should -Match '<StaticResource x:Key="AccentAdvertising" Key="Blue600"/>'
    }

    It 'emits a themed token as one AppThemeColor' {
        $path = New-Tokens ($primitives + @{ name = 'background'; value = @{ light = '{slate-50}'; dark = '{slate-900}' }; usage = 'Ground.' })
        ConvertTo-ColorsXaml -Path $path | Should -Match '<toolkit:AppThemeColor x:Key="Background" Light="\{StaticResource Slate50\}" Dark="\{StaticResource Slate900\}"/>'
    }

    It 'reorders an alpha literal from rrggbbaa to aarrggbb' {
        $path = New-Tokens ($primitives + @{ name = 'status-success-surface'; value = '#22c55e14'; usage = 'Ground.' })
        ConvertTo-ColorsXaml -Path $path | Should -Match '<Color x:Key="StatusSuccessSurface">#1422c55e</Color>'
    }

    It 'reorders an alpha literal on one side of a themed token' {
        $path = New-Tokens ($primitives + @{ name = 'status-expired-text'; value = @{ light = '#ef444466'; dark = '{slate-900}' }; usage = 'Text.' })
        ConvertTo-ColorsXaml -Path $path | Should -Match 'Light="#66ef4444" Dark="\{StaticResource Slate900\}"'
    }

    It 'names keys by PascalCasing each dash-separated word' {
        $path = New-Tokens ($primitives + @{ name = 'chat-bubble-media-text'; value = '#555555'; usage = 'Caption.' })
        $xaml = ConvertTo-ColorsXaml -Path $path
        $xaml | Should -Match 'x:Key="ChatBubbleMediaText"'
        $xaml | Should -Match 'x:Key="Slate50"'
    }

    It 'writes the usage note above its key' {
        $path = New-Tokens ($primitives + @{ name = 'status-info'; value = '{blue-600}'; usage = 'Informational ink.' })
        ConvertTo-ColorsXaml -Path $path | Should -Match '<!-- Informational ink\. -->\n    <StaticResource x:Key="StatusInfo"'
    }

    It 'fails a themed side that names an unknown primitive' {
        $path = New-Tokens ($primitives + @{ name = 'background'; value = @{ light = '{slate-50}'; dark = '{slate-950}' }; usage = 'Ground.' })
        { ConvertTo-ColorsXaml -Path $path } | Should -Throw "*'slate-950'*not a primitive*"
    }

    It 'fails an alias that names a non-primitive' {
        $path = New-Tokens ($primitives + @{ name = 'a'; value = '{slate-50}'; usage = 'x' } + @{ name = 'b'; value = '{a}'; usage = 'x' })
        { ConvertTo-ColorsXaml -Path $path } | Should -Throw "*'a'*not a primitive*"
    }

    It 'is stable across two runs on the same input' {
        $path = New-Tokens ($primitives + @{ name = 'background'; value = @{ light = '{slate-50}'; dark = '{slate-900}' }; usage = 'Ground.' })
        (ConvertTo-ColorsXaml -Path $path) | Should -BeExactly (ConvertTo-ColorsXaml -Path $path)
    }
}
