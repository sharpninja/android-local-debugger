[CmdletBinding()]
param(
    [string]$AdbPath,
    [string]$Serial,
    [string]$ArtifactDirectory,
    [string]$ExpectedComputerName = 'payton-legion2',
    [int]$Port = 56000
)

$ErrorActionPreference = 'Stop'
$actualHost = [System.Net.Dns]::GetHostName()
if ($actualHost.Split('.')[0] -ine $ExpectedComputerName.Split('.')[0]) {
    throw "Expected host '$ExpectedComputerName'; this script is running on '$actualHost'."
}
if (-not $ArtifactDirectory) {
    if (Test-Path (Join-Path $PSScriptRoot 'LocalDebug-android-arm64.apk')) {
        $ArtifactDirectory = $PSScriptRoot
    } else {
        $ArtifactDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'
    }
}
$ArtifactDirectory = (Resolve-Path $ArtifactDirectory).Path
if (-not $AdbPath) {
    $command = Get-Command adb -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { $AdbPath = $command.Source }
}
if (-not $AdbPath) {
    $sdkRoots = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)
    if ($env:LOCALAPPDATA) { $sdkRoots += (Join-Path $env:LOCALAPPDATA 'Android\Sdk') }
    if (${env:ProgramFiles(x86)}) { $sdkRoots += (Join-Path ${env:ProgramFiles(x86)} 'Android\android-sdk') }
    foreach ($sdk in $sdkRoots) {
        if (-not $sdk) { continue }
        $candidate = Join-Path $sdk 'platform-tools\adb.exe'
        if (Test-Path $candidate) { $AdbPath = $candidate; break }
    }
}
if (-not $AdbPath) { throw 'ADB was not found. Supply -AdbPath with its existing installation path.' }
$AdbPath = (Resolve-Path $AdbPath).Path

function ConvertTo-NativeArgument([string]$Value) {
    if ($Value -notmatch '[\s"]') { return $Value }
    $quoted = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $quoted = [regex]::Replace($quoted, '(\\+)$', '$1$1')
    return '"' + $quoted + '"'
}

function Invoke-Adb {
    param([string[]]$Arguments, [int]$TimeoutSeconds = 45)
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $AdbPath
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $info
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            throw "ADB timed out after $TimeoutSeconds seconds: $($Arguments -join ' ')"
        }
        $text = $stdout.GetAwaiter().GetResult()
        $errors = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "ADB exit $($process.ExitCode): $text $errors"
        }
        return $text.Trim()
    } finally { $process.Dispose() }
}

$packageMetadata = @(Get-Content (Join-Path $ArtifactDirectory 'package-checks.json') -Raw | ConvertFrom-Json)
foreach ($package in $packageMetadata) {
    $file = Join-Path $ArtifactDirectory $package.file
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash
    if ($hash -ine $package.sha256) { throw "APK checksum mismatch: $file" }
}

[void](Invoke-Adb -Arguments @('start-server'))
$inventory = Invoke-Adb -Arguments @('devices', '-l')
$available = @()
foreach ($line in ($inventory -split '\r?\n')) {
    if ($line -match '^(\S+)\s+device\b') { $available += $matches[1] }
}
if ($Serial -and $Serial -notin $available) {
    throw 'The selected phone is not authorized and connected in ADB. Check its USB debugging authorization dialog.'
}
if (-not $Serial) {
    $candidates = @()
    foreach ($id in $available) {
        if ($id -match ':|^emulator-|\.tls-connect') { continue }
        $model = Invoke-Adb -Arguments @('-s', $id, 'shell', 'getprop', 'ro.product.model')
        if ($model -like 'SM-F936*') { $candidates += $id }
    }
    if ($candidates.Count -ne 1) { throw 'Connect and authorize exactly one Samsung Fold 4 by USB, or supply its -Serial.' }
    $Serial = $candidates[0]
}
$model = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'getprop', 'ro.product.model')
if ($model -notlike 'SM-F936*') { throw "The selected device is '$model', not a Samsung Fold 4 (SM-F936*)." }
if ($Serial -match ':|^emulator-|\.tls-connect') { throw 'This run requires the USB-connected phone.' }
$abi = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'getprop', 'ro.product.cpu.abilist')
if ($abi -notmatch 'arm64-v8a') { throw "The device does not support these ARM64 APKs: $abi" }
$devicePath = Invoke-Adb -Arguments @('-s', $Serial, 'get-devpath')
$release = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'getprop', 'ro.build.version.release')
$api = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'getprop', 'ro.build.version.sdk')
$screen = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'wm', 'size')

$runId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$results = Join-Path $ArtifactDirectory "device-results\$runId"
[void](New-Item -ItemType Directory -Path $results -Force)
$hostReport = [ordered]@{
    Host = $actualHost; Model = $model; Serial = $Serial; Transport = $devicePath
    AndroidRelease = $release; AndroidApi = $api; Abis = $abi; Screen = $screen
    StartedUtc = (Get-Date).ToUniversalTime().ToString('o')
    ApkHashes = @($packageMetadata | Select-Object file, sha256)
    Passed = $false; DeviceChecks = $null; Error = $null; UiLaunches = @()
}
$instrumentation = ''
try {
    Write-Host "Testing $model through $actualHost (USB)."
    foreach ($package in $packageMetadata) {
        $install = Invoke-Adb -Arguments @('-s', $Serial, 'install', '-r', (Join-Path $ArtifactDirectory $package.file)) -TimeoutSeconds 90
        if ($install -notmatch 'Success') { throw "APK installation was not successful: $install" }
    }
    [void](Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'am', 'force-stop', 'dev.localdebug.debugger'))
    [void](Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'am', 'force-stop', 'dev.localdebug.target'))
    $start = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'am', 'start', '-W', '-n', 'dev.localdebug.target/dev.localdebug.target.MainActivity')
    if ($start -notmatch 'Status: ok') { throw "Target activity launch failed: $start" }
    Start-Sleep -Seconds 2
    $targetPid = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'pidof', 'dev.localdebug.target')
    if (-not $targetPid) { throw 'The target process did not remain alive after launch.' }
    $hostReport['TargetProcessId'] = $targetPid
    # Instrumentation executes C# in the debugger APK on the phone. Do not add
    # adb forward/reverse: the loopback connection must stay entirely on-device.
    $instrumentation = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'am', 'instrument', '-w', '-r', '-e', 'port', "$Port", 'dev.localdebug.debugger/dev.localdebug.debugger.DeviceTests') -TimeoutSeconds 120
    $instrumentation | Set-Content (Join-Path $results 'instrumentation.txt') -Encoding UTF8
    $match = [regex]::Match($instrumentation, '(?m)^INSTRUMENTATION_RESULT: localdebug\.result=(.*)\r?$')
    if (-not $match.Success) { throw "No structured test report was returned: $instrumentation" }
    $device = $match.Groups[1].Value.Trim() | ConvertFrom-Json
    $hostReport['DeviceChecks'] = $device
    $match.Groups[1].Value.Trim() | Set-Content (Join-Path $results 'device-checks.json') -Encoding UTF8
    if (-not $device.Passed -or $device.ExpectedChecks -ne 8 -or @($device.Checks).Count -ne 8 -or @($device.Checks | Where-Object { $_.Outcome -ne 'Passed' }).Count -ne 0) {
        throw "Device checks failed: $($device.Error)"
    }
    if ($device.Metadata.ClientEnvironmentVersion -notmatch '^10\.') { throw 'The debugger did not report a .NET 10 runtime.' }
    if (-not @($device.Logs | Where-Object { $_ -match 'Attached:\s+mono\s+10\.' }).Count) {
        throw 'The target agent did not report .NET 10 Mono.'
    }
    if ($device.Metadata.ClientProcessId -eq $targetPid) { throw 'The debugger and target unexpectedly used the same process.' }
    $afterPid = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'pidof', 'dev.localdebug.target')
    if ($afterPid -ne $targetPid) { throw 'The target exited or restarted during the attach/detach tests.' }
    foreach ($package in @('dev.localdebug.target', 'dev.localdebug.debugger')) {
        $launch = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'am', 'start', '-W', '-n', "$package/$package.MainActivity")
        Start-Sleep -Seconds 2
        $processId = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'pidof', $package)
        if ($launch -notmatch 'Status: ok' -or -not $processId) { throw "UI launch failed for $package." }
        $hostReport['UiLaunches'] += [ordered]@{ Package = $package; ProcessId = $processId; Launch = $launch }
    }
    $hostReport['Passed'] = $true
    Write-Host 'PASS: all 8 on-phone checks, .NET 10 runtimes, separate processes, and both activity launches.'
} catch {
    $hostReport['Error'] = $_.Exception.Message
    throw
} finally {
    try {
        $log = Invoke-Adb -Arguments @('-s', $Serial, 'logcat', '-d', '-v', 'threadtime', '-s', 'LocalDebug.DeviceTests')
        $log | Set-Content (Join-Path $results 'device-log.txt') -Encoding UTF8
    } catch { }
    $hostReport['CompletedUtc'] = (Get-Date).ToUniversalTime().ToString('o')
    $hostReport | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $results 'host-report.json') -Encoding UTF8
    if (-not $hostReport['Passed']) {
        try { [void](Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'am', 'force-stop', 'dev.localdebug.debugger')) } catch { }
    }
    Write-Host "Results: $results"
}
