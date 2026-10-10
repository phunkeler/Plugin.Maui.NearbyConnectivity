#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the device-test suite on an Android emulator and/or iOS simulator.

.DESCRIPTION
    Wraps `dotnet test` on the DeviceRunners-hosted runner app. The same script runs locally
    (macOS/Windows/Linux) and in CI. Device setup goes through two pinned local tools (`dotnet
    tool restore` first): `AndroidSdk.Tool` (`dotnet android`) creates and boots the Android
    emulator, `Microsoft.Maui.Cli` (`dotnet maui`) creates/boots the iOS simulator and enumerates
    devices on both platforms. Local and CI run the same commands through both tools, so they
    share one setup algorithm instead of two. The target device is passed explicitly to `dotnet
    test` (DeviceRunners' booted-device auto-detection is unreliable), and TRX results land in
    artifacts/.

    On-device code coverage is not possible: dotnet-coverage/coverlet cannot instrument the
    Android/iOS app runtimes. This script produces TRX results and a
    console summary, not a coverage report -- that stays a unit-suite concern.

.PARAMETER Suite
    Which suite to run: DeviceTests (default) or UiTests. DeviceTests runs the DeviceRunners-hosted
    xunit assembly on-device. UiTests drives the UiTests.Host MAUI app through Appium, from the
    desktop: it installs the host app, starts an Appium server, resolves the launcher activity, and
    runs test/Plugin.Maui.NearbyConnectivity.UiTests against it. Device setup is identical either
    way, so both suites boot the same emulator/simulator.

.PARAMETER Platform
    android, ios, or all (default). 'all' runs what the host OS supports and reports what it
    skipped (iOS requires macOS + Xcode).

.PARAMETER AndroidApiLevel
    Which Android API level to test: latest, common, minimum (resolved from
    .config/android-api-levels.json), or a literal API level number. Defaults to 'latest'.
    CI runs all three levels as a matrix; a local run tests one at a time.

.PARAMETER AndroidArch
    System image ABI for the Android emulator: x86_64 or arm64-v8a. Defaults to the host's own
    architecture (uname -m) so Apple Silicon runs a native arm64-v8a image instead of x86_64 under
    emulation. Override to reproduce a specific CI leg (CI is always x86_64) or to force x86_64 on
    an Intel Mac/Linux host.

.PARAMETER AndroidGpu
    Emulator --gpu mode. Defaults to 'swiftshader_indirect' (software rendering, required on
    CI/KVM hosts with no real GPU) when $env:CI is set, or 'auto' (real hardware acceleration)
    otherwise. Override to force swiftshader locally when reproducing a CI-only flake.

.EXAMPLE
    ./scripts/device-tests.ps1

.EXAMPLE
    ./scripts/device-tests.ps1 -Platform android -AndroidApiLevel minimum

.EXAMPLE
    ./scripts/device-tests.ps1 -Platform android -AndroidArch x86_64 -AndroidGpu swiftshader_indirect

.EXAMPLE
    ./scripts/device-tests.ps1 -Platform android -Suite UiTests
#>
[CmdletBinding()]
param(
    [ValidateSet('DeviceTests', 'UiTests')]
    [string]$Suite = 'DeviceTests',

    [ValidateSet('android', 'ios', 'all')]
    [string]$Platform = 'all',

    [string]$AndroidApiLevel = 'latest',

    [ValidateSet('x86_64', 'arm64-v8a')]
    [string]$AndroidArch = $(if ((uname -m) -eq 'arm64') { 'arm64-v8a' } else { 'x86_64' }),

    [string]$AndroidGpu = $(if ($env:CI) { 'swiftshader_indirect' } else { 'auto' })
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$runnerProject = Join-Path $repoRoot 'test/Plugin.Maui.NearbyConnectivity.DeviceTests.Runner/Plugin.Maui.NearbyConnectivity.DeviceTests.Runner.csproj'
$uiHostProject = Join-Path $repoRoot 'test/Plugin.Maui.NearbyConnectivity.UiTests.Host/Plugin.Maui.NearbyConnectivity.UiTests.Host.csproj'
$uiTestProject = Join-Path $repoRoot 'test/Plugin.Maui.NearbyConnectivity.UiTests/Plugin.Maui.NearbyConnectivity.UiTests.csproj'
$uiAppId = 'com.phunkeler.nearbyconnectivity.uitests'
# The configuration the UI host app is built and installed in. Named once so the build and the
# APK/.app lookup below cannot disagree and install a binary from some other configuration.
#
# Release, not Debug: a Debug Android build uses Fast Deployment, which leaves the managed
# assemblies out of the APK and pushes them separately via `dotnet build -t:Install`. Installing
# that APK with plain `adb install` produces an app that aborts on launch with "No assemblies
# found ... Assuming this is part of Fast Deployment". Release embeds the assemblies.
$uiHostConfiguration = 'Release'
$artifacts = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

Write-Host 'Restoring local dotnet tools (maui CLI, AndroidSdk.Tool)...'
dotnet tool restore | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }

$failed = @()

# The one place .config/android-api-levels.json is parsed, so CI's matrix and a local run
# resolve 'latest'/'common'/'minimum' to the same number by construction.
function Get-AndroidApiLevel([string]$Name) {
    if ($Name -match '^\d+$') { return [int]$Name }
    $levelsPath = Join-Path $repoRoot '.config/android-api-levels.json'
    $levels = Get-Content $levelsPath -Raw | ConvertFrom-Json
    $value = $levels.$Name
    if (-not $value) { throw "Unknown Android API level key '$Name'. Expected latest, common, minimum, or a number." }
    return [int]$value
}

# dotnet maui device list --json returns snake_case fields: identifier, is_running, is_emulator,
# platform, details.avd -- verified against the pinned CLI build; do not assume PascalCase.
function Get-MauiDevices([string]$PlatformFilter) {
    $json = dotnet maui device list --json --platform $PlatformFilter 2>$null
    if ($LASTEXITCODE -ne 0) { throw "dotnet maui device list failed (exit $LASTEXITCODE)." }
    return $json | ConvertFrom-Json
}

# --- UI test (Appium) helpers -------------------------------------------------------------------
# Only used by -Suite UiTests. The device/emulator is already booted by the caller; these add the
# Appium server and the app-under-test on top of it.

# Appium and its drivers are a node toolchain, not a dotnet tool, so they cannot go in
# .config/dotnet-tools.json with the rest. Installed on demand and left in place: re-running is
# idempotent and a CI runner starts clean anyway.
function Install-AppiumIfMissing([string]$DriverName) {
    if (-not (Get-Command npx -ErrorAction SilentlyContinue)) {
        throw 'npx was not found on PATH. Appium requires Node.js; install it to run -Suite UiTests.'
    }

    if (-not (Get-Command appium -ErrorAction SilentlyContinue)) {
        Write-Host 'Installing Appium (npm install -g appium)...'
        npm install -g appium
        if ($LASTEXITCODE -ne 0) { throw "npm install -g appium failed (exit $LASTEXITCODE)." }
    }

    # `driver list --installed` exits non-zero when none are installed, so the exit code cannot
    # distinguish "not installed" from "command failed" -- match on the output instead.
    $installed = appium driver list --installed 2>&1 | Out-String
    if ($installed -notmatch [regex]::Escape($DriverName)) {
        Write-Host "Installing Appium driver '$DriverName'..."
        appium driver install $DriverName
        if ($LASTEXITCODE -ne 0) { throw "appium driver install $DriverName failed (exit $LASTEXITCODE)." }
    }
}

# Returns the server process so the caller can stop it in a finally block. Polls /status rather
# than sleeping a fixed interval: a cold first start is much slower than a warm one.
function Start-AppiumServer([int]$Port) {
    $logPath = Join-Path $artifacts "appium-$Port.log"
    Write-Host "Starting Appium server on port $Port (log: $logPath)..."

    $process = Start-Process -FilePath 'appium' `
        -ArgumentList @('--port', $Port, '--log-timestamp', '--log-no-colors') `
        -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err" `
        -PassThru -NoNewWindow

    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) { throw "Appium server exited immediately (exit $($process.ExitCode)). See $logPath." }
        try {
            $status = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/status" -TimeoutSec 2
            if ($status) { Write-Host "Appium server is ready on port $Port."; return $process }
        }
        catch { Start-Sleep -Milliseconds 500 }
    }

    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw "Appium server did not become ready within 60s. See $logPath."
}

function Stop-AppiumServer($Process) {
    if ($Process -and -not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    }
}

# Runs the UI test assembly with the environment the fixture reads, then summarises the TRX. The
# env vars are set for the child process only; the fixture skips the whole suite when APPIUM_URL is
# absent, which is what keeps this suite inert outside this script.
function Write-TestSummary([string]$TrxPath, [string]$Title) {
    $summary = pwsh (Join-Path $repoRoot 'scripts/trx-summary.ps1') -Path $TrxPath -Title $Title
    $summary | Out-Host
    if ($env:GITHUB_STEP_SUMMARY) { $summary | Add-Content -Path $env:GITHUB_STEP_SUMMARY }
}

function Invoke-UiTestAssembly([string]$Label, [hashtable]$Environment) {
    $trxName = "ui-$Label.trx"
    $screenshots = Join-Path $artifacts 'ui-screenshots'

    $previous = @{}
    foreach ($key in $Environment.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $Environment[$key])
    }
    [Environment]::SetEnvironmentVariable('UITEST_SCREENSHOT_DIR', $screenshots)

    # A run that fails before it writes a TRX must not be summarised from an earlier run's file.
    Remove-Item (Join-Path $artifacts $trxName) -Force -ErrorAction SilentlyContinue

    try {
        # Same RequiresRealPeer exclusion as the device suite: those tests need a second radio.
        dotnet test $uiTestProject --filter 'Category!=RequiresRealPeer' --logger "trx;LogFileName=$trxName" --results-directory $artifacts
        $exitCode = $LASTEXITCODE
    }
    finally {
        foreach ($key in $previous.Keys) {
            [Environment]::SetEnvironmentVariable($key, $previous[$key])
        }
        [Environment]::SetEnvironmentVariable('UITEST_SCREENSHOT_DIR', $null)
    }

    Write-TestSummary (Join-Path $artifacts $trxName) "UI $Label"

    if ($exitCode -ne 0) { throw "$Label UI tests failed (exit $exitCode)." }
}

function Invoke-AndroidUiTests([string]$Serial, [string]$Adb, [string]$Label) {
    Install-AppiumIfMissing 'uiautomator2'

    Write-Host 'Building and installing the UI test host app (adb install -r -g)...'
    dotnet build $uiHostProject -c $uiHostConfiguration -f net10.0-android -p:TargetFrameworks=net10.0-android | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet build (UI host) failed (exit $LASTEXITCODE)." }

    # Scoped to the configuration just built. A bare bin/ search also matches APKs left by earlier
    # builds in other configurations, and installing one of those silently tests a stale binary.
    $apkRoot = Join-Path (Split-Path $uiHostProject) "bin/$uiHostConfiguration/net10.0-android"
    $apk = Get-ChildItem -Path $apkRoot -Filter '*-Signed.apk' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $apk) { throw "Could not find the built UI host APK to install under '$apkRoot'." }

    # -g grants every manifest-declared runtime permission at install time. Without it the first
    # launch raises a system permission dialog over the app's own UI, which Appium cannot dismiss
    # (autoGrantPermissions only applies when Appium itself installs the app, and it does not here).
    & $Adb -s $Serial install -r -g $apk
    if ($LASTEXITCODE -ne 0) { throw "adb install -r -g (UI host) failed (exit $LASTEXITCODE)." }

    # .NET for Android mangles the launcher activity's Java class name per build, so it cannot be
    # hardcoded; appium:noReset also skips UiAutomator2's own manifest-reading detection, so the
    # capability must be supplied explicitly. Resolve it from the installed package.
    $resolved = & $Adb -s $Serial shell cmd package resolve-activity --brief $uiAppId | Select-Object -Last 1
    if (-not $resolved -or $resolved -notmatch '/') {
        throw "Could not resolve the launcher activity for '$uiAppId' (got '$resolved')."
    }
    $appActivity = ($resolved -split '/')[1].Trim()
    Write-Host "Resolved launcher activity: $appActivity"

    $port = 4723
    $server = $null
    try {
        $server = Start-AppiumServer $port
        Invoke-UiTestAssembly $Label @{
            'APPIUM_URL'      = "http://127.0.0.1:$port"
            'UITEST_PLATFORM' = 'android'
            'DEVICE_SERIAL'   = $Serial
            'APP_ACTIVITY'    = $appActivity
        }
    }
    finally {
        Stop-AppiumServer $server
    }
}

function Invoke-IosUiTests([string]$Udid, [string]$Rid) {
    Install-AppiumIfMissing 'xcuitest'

    Write-Host 'Building and installing the UI test host app (simctl install)...'
    dotnet build $uiHostProject -c $uiHostConfiguration -f net10.0-ios -p:TargetFrameworks=net10.0-ios -p:RuntimeIdentifier=$Rid | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet build (UI host) failed (exit $LASTEXITCODE)." }

    # Scoped to the configuration and RID just built, for the same reason as the Android APK above.
    $appRoot = Join-Path (Split-Path $uiHostProject) "bin/$uiHostConfiguration/net10.0-ios/$Rid"
    $appBundle = Get-ChildItem -Path $appRoot -Directory -Filter '*.app' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $appBundle) { throw "Could not find the built UI host .app bundle to install under '$appRoot'." }

    xcrun simctl install $Udid $appBundle
    if ($LASTEXITCODE -ne 0) { throw "simctl install failed (exit $LASTEXITCODE)." }

    $port = 4723
    $server = $null
    try {
        $server = Start-AppiumServer $port
        Invoke-UiTestAssembly 'ios' @{
            'APPIUM_URL'      = "http://127.0.0.1:$port"
            'UITEST_PLATFORM' = 'ios'
            'DEVICE_SERIAL'   = $Udid
        }
    }
    finally {
        Stop-AppiumServer $server
    }
}

function Invoke-AndroidTests {
    $level = Get-AndroidApiLevel $AndroidApiLevel
    # Arch is part of the name: an x86_64 AVD and an arm64-v8a AVD are different images and must
    # not collide under one name, e.g. a dev switching -AndroidArch to reproduce a CI-only issue.
    $avdName = "device-tests-$level-$AndroidArch"
    $sdkId = "system-images;android-$level;google_apis;$AndroidArch"

    # `adb` isn't guaranteed to be on PATH (it wasn't on the CI runner) -- `sdk find` uses
    # AndroidSdk.Tool's own locator instead of assuming ANDROID_SDK_ROOT/ANDROID_HOME is set,
    # which isn't guaranteed on a local dev machine either. Resolved once at function scope so
    # every call site below (pre-install, logcat tail, lock-screen dismiss, log capture) shares
    # one path instead of each hand-rolling its own -- and so it's just as visible to Start-Job's
    # separate runspace, which does not inherit the parent scope's variables.
    $adb = Join-Path (dotnet android sdk find) 'platform-tools/adb'

    $running = Get-MauiDevices 'android' | Where-Object { $_.is_running }
    if (-not $running) {
        # AndroidSdk.Tool (`dotnet android`) owns Android emulator create/boot -- its `avd start`
        # has native headless flags and boot-readiness checks (--cpu-threshold,
        # --response-threshold) that `dotnet maui`'s emulator commands don't expose. `dotnet maui`
        # still owns device enumeration (Get-MauiDevices, above) and iOS, where AndroidSdk.Tool
        # has no equivalent. Its JSON output is PascalCase (Newtonsoft default), unlike `dotnet
        # maui`'s snake_case -- do not assume one schema applies to both tools.
        $avds = (dotnet android avd list --format json | ConvertFrom-Json)
        if ($LASTEXITCODE -ne 0) { throw "dotnet android avd list failed (exit $LASTEXITCODE)." }
        $exists = $avds | Where-Object { $_.Name -eq $avdName }

        if (-not $exists) {
            Write-Host "Creating Android emulator '$avdName' (API $level, $AndroidArch)..."
            # Self-bootstrap: a fresh clone has no Android SDK images installed yet. This is a
            # one-time, large download -- surfaced explicitly so it doesn't look like a hang.
            Write-Host "  (installing Android SDK packages if missing -- one-time, large download)"
            dotnet android sdk accept-licenses --force
            dotnet android sdk install --package platform-tools --package emulator --package $sdkId
            dotnet android avd create --name $avdName --sdk $sdkId --force
            if ($LASTEXITCODE -ne 0) { throw "dotnet android avd create failed (exit $LASTEXITCODE)." }
        }

        Write-Host "Booting Android emulator '$avdName' (--gpu $AndroidGpu)..."
        # --cpu-threshold/--response-threshold wait for the guest to settle after boot-completed,
        # not just report it -- a device can report sys.boot_completed=1 while still under
        # first-boot CPU load, which is a known source of test flake on a cold emulator.
        # --no-window only in CI: a local dev likely wants to see the emulator, matching the iOS
        # leg's own $env:CI-gated --no-open below.
        # [string[]] is required, not stylistic: an untyped array splatted into a native command
        # (dotnet) is iterated character-by-character when its one element starts with '-',
        # exploding '--no-window' into '-','-','n','o',... -- verified via repro, this is the
        # actual cause of "Option does not have a name" on the CI leg.
        [string[]]$windowArgs = if ($env:CI) { @('--no-window') } else { @() }
        dotnet android avd start --name $avdName @windowArgs `
            --no-audio --no-boot-anim --no-snapshot-save --gpu $AndroidGpu --camera-back none `
            --wait-boot --timeout 300 --cpu-threshold 3 --response-threshold 5
        if ($LASTEXITCODE -ne 0) { throw "dotnet android avd start failed (exit $LASTEXITCODE)." }

        # Dismiss the lock screen so instrumented tests can interact with the UI -- no
        # equivalent flag on `avd start`, so this runs as a follow-up adb call.
        & $adb shell input keyevent 82 2>$null

        $running = Get-MauiDevices 'android' | Where-Object { $_.is_running }
        if (-not $running) { throw "No running Android device found after starting '$avdName'." }
    }

    $serial = ($running | Select-Object -First 1).identifier

    # Everything above is device setup, shared by both suites. Only what runs on the booted device
    # differs.
    if ($Suite -eq 'UiTests') {
        Write-Host "Running Android UI tests on '$serial' (API $level)..."
        Invoke-AndroidUiTests $serial $adb "android-$level"
        return
    }

    Write-Host "Running Android device tests on '$serial' (API $level)..."

    # Nearby Connections needs BLUETOOTH_SCAN/CONNECT/ADVERTISE and NEARBY_WIFI_DEVICES granted
    # at runtime (Android 12+) before GMS will start advertising -- ungranted, GMS's
    # StartAdvertisingAsync blocks forever rather than failing fast, hanging the whole run.
    # `adb install -g` auto-grants every manifest-declared runtime permission at install time;
    # verified empirically that a same-signature incremental reinstall (what DeviceRunners' own
    # Install MSBuild target performs as part of `dotnet test`, below) preserves those grants
    # rather than resetting them, so installing here first is sufficient.
    Write-Host 'Building and pre-installing the runner app (adb install -g)...'
    dotnet build $runnerProject -f net10.0-android -p:TargetFrameworks=net10.0-android | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet build (pre-install) failed (exit $LASTEXITCODE)." }
    # Scoped to the configuration just built, newest first. A bare recursive bin/ search also
    # matches APKs left by earlier builds in other configurations, and pre-installing one of those
    # silently runs the previous build's tests. Debug, not Release: `dotnet test` below builds and
    # reinstalls in its own default configuration, and pinning a different one here would install a
    # binary that run then contradicts.
    $apkRoot = Join-Path (Split-Path $runnerProject) 'bin/Debug/net10.0-android'
    $apk = Get-ChildItem -Path $apkRoot -Filter '*-Signed.apk' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $apk) { throw "Could not find the built runner APK to pre-install under '$apkRoot'." }
    & $adb -s $serial install -r -g $apk
    if ($LASTEXITCODE -ne 0) { throw "adb install -g failed (exit $LASTEXITCODE)." }

    # Live-tail structured plugin logs to the console
    # while the run is in progress -- in CI this interleaves directly into the Actions step log,
    # not just the post-run artifact, so a hang is visible as it happens instead of only after
    # the job times out. Start-Job runs in its own runspace and does not inherit $adb, so it's
    # passed explicitly alongside the serial.
    $logcatJob = Start-Job -ScriptBlock {
        param($AdbPath, $AdbSerial)
        & $AdbPath -s $AdbSerial logcat -v raw -s 'DOTNET:I'
    } -ArgumentList $adb, $serial
    $logcatReceiver = Start-Job -ScriptBlock {
        param($JobId)
        while ($true) {
            Receive-Job -Id $JobId | ForEach-Object { Write-Host "[logcat] $_" }
            Start-Sleep -Milliseconds 250
        }
    } -ArgumentList $logcatJob.Id

    try {
        # --logger trx is required, not cosmetic: DeviceRunners omits the CLI's --logger flag when
        # none is given, and writes no TRX at all. The name matches _DeviceRunnersTrxFile, which the
        # report phase reads back.
        # -p:TargetFrameworks pins the restore graph to this TFM. `-f` scopes the *build*, but restore
        # still walks every framework in <TargetFrameworks>, which fails with NETSDK1147 on a machine
        # that only has the one platform's workload installed (as each CI job does).
        # --filter excludes tests traited RequiresRealPeer (see OfferDeadlineTests.android.cs): they
        # call the real GMS AcceptConnectionAsync, which needs a genuine second device's
        # requestConnection to reach the connection state that call validates against -- a
        # single, radio-isolated emulator always fails them with STATUS_OUT_OF_ORDER_API_CALL.
        dotnet test $runnerProject -f net10.0-android -p:TargetFrameworks=net10.0-android -p:DeviceRunnersDevice=$serial --filter 'Category!=RequiresRealPeer' --logger 'trx;LogFileName=test-results.trx'
        $testExitCode = $LASTEXITCODE
    }
    finally {
        Stop-Job $logcatReceiver, $logcatJob -ErrorAction SilentlyContinue
        Receive-Job -Id $logcatJob.Id | ForEach-Object { Write-Host "[logcat] $_" }
        Remove-Job $logcatReceiver, $logcatJob -Force -ErrorAction SilentlyContinue
    }
    if ($testExitCode -ne 0) { throw "Android device tests failed (exit $testExitCode)." }

    # Only CI captured logcat until now; local flake triage gets the same evidence.
    & $adb logcat -d > (Join-Path $artifacts "device-android-$level-logcat.txt") 2>$null
}

function Invoke-IosTests {
    if (-not $IsMacOS) { throw 'iOS device tests require macOS with Xcode.' }

    $simName = 'device-tests'
    $running = Get-MauiDevices 'ios' | Where-Object { $_.is_running -and $_.name -eq $simName }

    if (-not $running) {
        # `xcrun simctl list devicetypes` is already ordered newest-first (verified: iPhone 17
        # Pro before iPhone 6s Plus), so the first iPhone entry is the newest device type -- omit
        # --runtime to let the CLI pick the newest installed runtime that supports it.
        $deviceType = (xcrun simctl list devicetypes -j | ConvertFrom-Json).devicetypes |
            Where-Object { $_.name -match '^iPhone' } |
            Select-Object -First 1 -ExpandProperty identifier
        if (-not $deviceType) { throw 'No iPhone simulator device type found via xcrun simctl.' }

        $createJson = dotnet maui apple simulator create $deviceType --name $simName --if-not-exists --json
        if ($LASTEXITCODE -ne 0) { throw "dotnet maui apple simulator create failed (exit $LASTEXITCODE). Run 'dotnet maui doctor' to diagnose." }
        # `simulator create --json` returns its own shape (udid/name/device_type), distinct from
        # the unified `device list --json` shape (identifier/is_running/...) used everywhere else
        # in this function -- verified separately against the pinned CLI build.
        $udid = ($createJson | ConvertFrom-Json).udid

        # `--if-not-exists` can hand back the UDID of a sim that's already booted (e.g. created
        # by a previous run this script didn't itself detect as running yet) -- `simulator start`
        # errors on an already-booted device (verified: exit 1, "Unable to boot device in
        # current state: Booted"), so check state first rather than treating that as a failure.
        $alreadyBooted = (Get-MauiDevices 'ios' | Where-Object { $_.identifier -eq $udid -and $_.is_running })
        if (-not $alreadyBooted) {
            Write-Host "Booting simulator '$simName' ($udid)..."
            # `--no-open` skips launching Simulator.app in CI, which has no display server; locally
            # the app is opened so the run is visible, matching the Android leg's own emulator window.
            if ($env:CI) {
                dotnet maui apple simulator start $udid --no-open
            }
            else {
                dotnet maui apple simulator start $udid
            }
            if ($LASTEXITCODE -ne 0) { throw "dotnet maui apple simulator start failed (exit $LASTEXITCODE). Run 'dotnet maui doctor' to diagnose." }
        }

        $running = Get-MauiDevices 'ios' | Where-Object { $_.identifier -eq $udid }
    }

    $device = $running | Select-Object -First 1
    $udid = $device.identifier

    # Match the RID to the host: Apple Silicon simulators are arm64, Intel ones x64.
    $rid = if ((uname -m) -eq 'arm64') { 'iossimulator-arm64' } else { 'iossimulator-x64' }

    if ($Suite -eq 'UiTests') {
        Write-Host "Running iOS UI tests on '$($device.name)' ($udid, $rid)..."
        Invoke-IosUiTests $udid $rid
        return
    }

    Write-Host "Running iOS device tests on '$($device.name)' ($udid, $rid)..."

    # See the Android note: restore ignores -f, so the TFM is pinned for the restore graph too. The
    # RequiresRealPeer filter is the Android one: a single simulator has no second device either.
    dotnet test $runnerProject -f net10.0-ios -p:TargetFrameworks=net10.0-ios -p:RuntimeIdentifier=$rid -p:DeviceRunnersDevice=$udid --filter 'Category!=RequiresRealPeer' --logger 'trx;LogFileName=test-results.trx'
    if ($LASTEXITCODE -ne 0) { throw "iOS device tests failed (exit $LASTEXITCODE)." }
}

# Both platforms write to the runner project's one test-results directory, so a run that fails
# before it writes a TRX would otherwise be summarised from the other platform's file, or from this
# platform's previous run.
function Clear-Results([string]$suffix) {
    if ($Suite -eq 'UiTests') { return }

    Remove-Item (Join-Path (Split-Path $runnerProject) 'test-results') -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem $artifacts -Filter "device-$suffix*" | Remove-Item -Force -ErrorAction SilentlyContinue
}

function Copy-Results([string]$suffix) {
    # The UI suite writes its TRX directly to artifacts/ (--results-directory); only the
    # DeviceRunners-hosted suite leaves results in the runner project's own output directory.
    if ($Suite -eq 'UiTests') { return }

    $resultsDir = Join-Path (Split-Path $runnerProject) 'test-results'
    if (Test-Path $resultsDir) {
        Get-ChildItem $resultsDir -Filter '*.trx' | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $artifacts "device-$suffix.trx") -Force
        }
        Get-ChildItem $resultsDir -Filter '*.txt' | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $artifacts "device-$suffix-$($_.Name)") -Force
        }
        Get-ChildItem $resultsDir -Filter '*.jsonl' | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $artifacts "device-$suffix-$($_.Name)") -Force
        }
    }

    # After the copy, so the summary reads this run's TRX and not the previous run's.
    Write-TestSummary (Join-Path $artifacts "device-$suffix.trx") "DeviceTests $suffix"
}

if ($Platform -in @('android', 'all')) {
    $androidSuffix = "android-$(Get-AndroidApiLevel $AndroidApiLevel)"
    # Write-Host, not just $failed: the collected message is printed at the end, but CI needs the
    # failure visible in the job log at the point it happened.
    Clear-Results $androidSuffix
    try { Invoke-AndroidTests } catch { Write-Host "android failed: $_"; $failed += "android: $_" } finally { Copy-Results $androidSuffix }
}

if ($Platform -in @('ios', 'all')) {
    if ($IsMacOS) {
        Clear-Results 'ios'
        try { Invoke-IosTests } catch { Write-Host "ios failed: $_"; $failed += "ios: $_" } finally { Copy-Results 'ios' }
    }
    elseif ($Platform -eq 'all') {
        Write-Warning 'Skipping iOS: requires macOS with Xcode.'
    }
    else {
        throw 'iOS device tests require macOS with Xcode.'
    }
}

if ($failed) {
    $failed | ForEach-Object { Write-Error -ErrorAction Continue $_ }
    exit 1
}

Write-Host "Done. TRX results in $artifacts"
