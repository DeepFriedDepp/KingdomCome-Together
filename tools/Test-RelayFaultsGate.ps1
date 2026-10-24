# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-206 B gate: the relay's fault mode is TEST-ONLY. Nothing that ships can turn it on, and its
    tests run and pass.

.DESCRIPTION
    The relay (dotnet\KcdMp.Server) has a fault mode for the two-copy test bench: delay, hold,
    throttle, stall or cut the link between a host and a joiner (Features\Faults). It loads only
    when BOTH the environment variable KCDMP_TEST_RELAY_FAULTS=1 AND the command-line option
    --test-faults <plan.json> are present; neither is a configuration key. This gate proves a
    shipped build cannot carry either half:

      0. Self-check: the config scan below must flag a fault key planted in a temp copy of the
         relay's appsettings.json, and must pass the unplanted copy. A scan that sees nothing
         proves nothing.
      1. The shipped config files -- the top-level *.json of every project Publish-Release.ps1
         publishes into the payload (KCDMP_launcher, KcdMp.Client, KcdMp.Server, KcdMp.MasterServer;
         appsettings*.json among them) -- and, when -Payload names a built payload folder, every
         text config file in it: no fault key, no plan, no mention of either switch half; and no
         file anywhere in the payload named like a fault plan.
      2. What starts the relay or builds the installer never sets the variable or passes the
         option: installer\, KCDMP_launcher\ (sources), tools\Build-Installer.ps1,
         tools\Publish-Release.ps1.
      3. The relay's fault tests (dotnet\KcdMp.Relay.Tests, filter Wo206Fault) -- each check with
         its twin. A filter that matches no test fails the gate. -SkipTests leaves this step out
         (the tests themselves run the gate that way, for its self-check twin).

    Prints one RESULT line; exit 0 = PASS, 1 = FAIL.

.PARAMETER Payload
    Optional: a built payload folder (release\KCDMP) to scan as well.

.PARAMETER SkipTests
    Leave out step 3 (used by the gate's own C# twin tests, which run inside step 3).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-RelayFaultsGate.ps1
    powershell -ExecutionPolicy Bypass -File tools\Test-RelayFaultsGate.ps1 -Payload release\KCDMP
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Payload = '',
    [switch] $SkipTests
)

$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }

# The two halves of the switch, and the shapes a fault plan or key would take in a config file.
$switchPattern = 'KCDMP_TEST_RELAY_FAULTS|--test-faults|test-faults'
$configPattern = '(?i)relay[_-]?faults?|test[_-]?faults|fault[_-]?plan|"faults"\s*:|' + $switchPattern
# A file named like a plan: anything with "fault" in its name that is not a binary.
$planNamePattern = '(?i)fault'
$binaryExt = @('.dll', '.exe', '.pdb', '.so', '.dylib', '.pak', '.whs', '.png', '.jpg', '.ico', '.woff', '.woff2', '.ttf')
$textExt = @('.json', '.config', '.xml', '.ini', '.txt', '.cmd', '.bat', '.ps1', '.iss', '.yaml', '.yml', '.toml', '.cfg', '.plan', '')

$fail = New-Object System.Collections.Generic.List[string]

# Every hit of the config pattern in one file, as "file:line: text".
function Find-FaultConfig([string] $path) {
    $hits = @()
    $n = 0
    foreach ($line in [System.IO.File]::ReadAllLines($path)) {
        $n++
        if ($line -match $configPattern) { $hits += ("{0}:{1}: {2}" -f $path, $n, $line.Trim()) }
    }
    return ,$hits
}

# ---- 0. self-check ----
Write-Host "`n=== 0. self-check: the scan sees a planted fault key ===`n"
$relayConfig = Join-Path $root 'dotnet\KcdMp.Server\appsettings.json'
$selfOk = $false
if (Test-Path $relayConfig) {
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("wo206-selfcheck-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        $clean = Join-Path $tmp 'appsettings.json'
        $planted = Join-Path $tmp 'appsettings.Planted.json'
        Copy-Item $relayConfig $clean
        $text = [System.IO.File]::ReadAllText($relayConfig)
        $text = $text -replace '"Echo": false,', ('"Echo": false,' + "`r`n" + '  "RelayFaults": { "Plan": "lag.json" },')
        [System.IO.File]::WriteAllText($planted, $text)
        $cleanHits = Find-FaultConfig $clean
        $plantedHits = Find-FaultConfig $planted
        $selfOk = ($cleanHits.Count -eq 0) -and ($plantedHits.Count -gt 0)
        Write-Host ("  planted copy: {0} hit(s); clean copy: {1} hit(s)" -f $plantedHits.Count, $cleanHits.Count)
    } finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "  $relayConfig not found"
}
if ($selfOk) { Write-Host "  ok    the scan flags the planted key and passes the clean copy" -ForegroundColor Green }
else { Write-Host "  FAIL  the self-check failed -- the scan cannot be trusted" -ForegroundColor Red; $fail.Add('self-check') }

# ---- 1. shipped config files ----
Write-Host "`n=== 1. shipped config files ===`n"
$projects = @('KCDMP_launcher', 'dotnet\KcdMp.Client', 'dotnet\KcdMp.Server', 'dotnet\KcdMp.MasterServer')
$configs = @()
foreach ($p in $projects) {
    $dir = Join-Path $root $p
    if (Test-Path $dir) { $configs += Get-ChildItem $dir -File -Filter '*.json' }
}
if (-not ($configs | Where-Object { $_.Name -like 'appsettings*.json' -and $_.DirectoryName -like '*KcdMp.Server' })) {
    Write-Host "  FAIL  the relay's appsettings*.json were not found -- the scan looked in the wrong place" -ForegroundColor Red
    $fail.Add('no-relay-config')
}
$payloadFiles = 0
if ($Payload) {
    if (-not (Test-Path $Payload)) {
        Write-Host "  FAIL  payload folder not found: $Payload" -ForegroundColor Red
        $fail.Add('payload-missing')
    } else {
        foreach ($f in Get-ChildItem $Payload -File -Recurse) {
            $payloadFiles++
            $ext = $f.Extension.ToLowerInvariant()
            if ($f.Name -match $planNamePattern -and $binaryExt -notcontains $ext) {
                Write-Host "  HIT   a file named like a fault plan ships: $($f.FullName)" -ForegroundColor Red
                $fail.Add("plan-file:$($f.Name)")
            }
            if ($textExt -contains $ext -and $f.Length -lt 4MB) { $configs += $f }
        }
    }
}
$configHits = 0
foreach ($c in $configs) {
    foreach ($h in (Find-FaultConfig $c.FullName)) {
        Write-Host "  HIT   $h" -ForegroundColor Red
        $configHits++
    }
}
if ($configHits -gt 0) { $fail.Add("config-hits:$configHits") }
else { Write-Host ("  ok    {0} config file(s) clean{1}" -f $configs.Count, $(if ($Payload) { " ($payloadFiles payload file(s) walked)" } else { '' })) -ForegroundColor Green }

# ---- 2. what starts the relay / builds the installer ----
Write-Host "`n=== 2. launcher, installer and build scripts never set either half ===`n"
$scan = @()
$scan += Get-Item (Join-Path $root 'tools\Build-Installer.ps1'), (Join-Path $root 'tools\Publish-Release.ps1') -ErrorAction SilentlyContinue
foreach ($d in @('installer', 'KCDMP_launcher')) {
    $dir = Join-Path $root $d
    if (Test-Path $dir) {
        $scan += Get-ChildItem $dir -File -Recurse |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $binaryExt -notcontains $_.Extension.ToLowerInvariant() -and $_.Length -lt 4MB }
    }
}
$launchHits = 0
foreach ($f in $scan) {
    $n = 0
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
        $n++
        if ($line -match $switchPattern) {
            Write-Host ("  HIT   {0}:{1}: {2}" -f $f.FullName, $n, $line.Trim()) -ForegroundColor Red
            $launchHits++
        }
    }
}
if ($scan.Count -lt 3) { Write-Host "  FAIL  only $($scan.Count) file(s) found to scan -- the scan looked in the wrong place" -ForegroundColor Red; $fail.Add('scan-empty') }
if ($launchHits -gt 0) { $fail.Add("launch-hits:$launchHits") }
else { Write-Host "  ok    $($scan.Count) file(s) clean" -ForegroundColor Green }

# ---- 3. the fault tests ----
$passed = 0; $failedT = 0; $total = 0
if ($SkipTests) {
    Write-Host "`n=== 3. tests skipped (-SkipTests) ===`n"
} else {
    $sdk = Join-Path $env:USERPROFILE '.dotnet-sdk8'
    if (Test-Path (Join-Path $sdk 'dotnet.exe')) {
        $env:DOTNET_ROOT = $sdk
        $env:PATH = "$sdk;$env:PATH"
    }
    $filter = 'FullyQualifiedName~Wo206Fault'
    Write-Host "`n=== 3. $filter (dotnet\KcdMp.Relay.Tests) ===`n"
    $lines = New-Object System.Collections.Generic.List[string]
    & dotnet test (Join-Path $root 'dotnet\KcdMp.Relay.Tests\KcdMp.Relay.Tests.csproj') -c $Configuration --nologo -v q --filter $filter |
        ForEach-Object {
            $lines.Add([string]$_)
            if ($_ -notmatch ': warning [A-Za-z]+[0-9]+:') { Write-Host $_ }
        }
    $code = $LASTEXITCODE
    foreach ($l in $lines) {
        if ($l -match 'Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+\d+,\s+Total:\s+(\d+)') {
            $failedT += [int]$Matches[1]; $passed += [int]$Matches[2]; $total += [int]$Matches[3]
        }
    }
    if ($total -eq 0) { Write-Host "  FAIL  no test matched '$filter' -- the gate did not run" -ForegroundColor Red; $fail.Add('no-tests') }
    elseif ($code -ne 0 -or $failedT -gt 0) { $fail.Add("tests-failed:$failedT") }
}

$pass = $fail.Count -eq 0
$verdict = if ($pass) { 'PASS' } else { 'FAIL' }
$testsText = if ($SkipTests) { 'tests=skipped' } else { "tests passed=$passed failed=$failedT total=$total" }
$why = if ($pass) { '' } else { " -- $($fail -join ', ')" }
$line = "RESULT: $verdict relay-faults selfcheck=$(if ($selfOk) { 'ok' } else { 'FAILED' }) configs=$($configs.Count) config-hits=$configHits launch-files=$($scan.Count) launch-hits=$launchHits $testsText$why"
Write-Host ""
Write-Host $line -ForegroundColor $(if ($pass) { 'Green' } else { 'Red' })
if ($pass) { exit 0 } else { exit 1 }
