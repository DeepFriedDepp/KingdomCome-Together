# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-203 II.5 gate: the relay passes every message type. Every type the protocol defines has a
    relay rule, every rule crosses the REAL relay at its limits, and every W164 builder's worst
    case fits its row.

.DESCRIPTION
    Session C (0.62.0) lost 463 W164 frames (0x74) at the relay -- every WO-201 speech line and
    some votes -- counted only as "unknown-or-wrong-length" in a log nobody read. The type was in
    the relay's table; its row's text limit (80 bytes) was older than the senders. This gate runs
    the two test sets that would have caught it:

      1. dotnet\KcdMp.Relay.Tests, the RelayTableGate tests: reflection over Protocol's message ids
         against Protocol.RelayUp + Protocol.JoinWire (no type without a rule, no rule without a
         type, no id used twice or both ways); every join row through the real relay at its
         minimum and maximum, one byte past each dropped AND counted, the framing surviving; every
         other up type round-tripped with a minimal valid body and its wrong lengths counted; Act
         I's and Act II's channels (0x76/0x78) carrying 1400-byte texts both ways.
      2. dotnet\KcdMp.Client.Tests, the Wo203W164Fit tests: every W164 kind's builder at its worst
         case fits the row, and LootMsg.BuildUp refuses a text over its row before it is sent.

    A filter that matches no test fails the gate (a renamed class must not turn it green).
    Prints one RESULT line; exit 0 = PASS, 1 = a test failed or did not run.
    Run by tools\Build-Installer.ps1 just before the relay round-trip gate.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-RelayTable.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }

# The .NET 8 SDK is user-scope on the build machine; the machine-wide dotnet may be another version.
$sdk = Join-Path $env:USERPROFILE '.dotnet-sdk8'
if (Test-Path (Join-Path $sdk 'dotnet.exe')) {
    $env:DOTNET_ROOT = $sdk
    $env:PATH = "$sdk;$env:PATH"
} elseif (-not $env:DOTNET_ROOT) {
    Write-Host "  note: $sdk not found and DOTNET_ROOT unset -- using the dotnet on PATH"
}

# One dotnet test run, its output shown without the build's warnings; returns (ok, passed, failed, total).
function Invoke-GateTests([string] $project, [string] $filter) {
    Write-Host "`n=== $filter ($project) ===`n"
    $lines = New-Object System.Collections.Generic.List[string]
    & dotnet test (Join-Path $root $project) -c $Configuration --nologo -v q --filter $filter |
        ForEach-Object {
            $lines.Add([string]$_)
            if ($_ -notmatch ': warning [A-Za-z]+[0-9]+:') { Write-Host $_ }
        }
    $code = $LASTEXITCODE
    $passed = 0; $failed = 0; $total = 0
    foreach ($l in $lines) {
        if ($l -match 'Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+\d+,\s+Total:\s+(\d+)') {
            $failed += [int]$Matches[1]; $passed += [int]$Matches[2]; $total += [int]$Matches[3]
        }
    }
    $ok = ($code -eq 0) -and ($failed -eq 0) -and ($total -gt 0)
    if ($total -eq 0) { Write-Host "  FAIL  no test matched '$filter' -- the gate did not run" -ForegroundColor Red }
    return @($ok, $passed, $failed, $total)
}

$relay = Invoke-GateTests 'dotnet\KcdMp.Relay.Tests\KcdMp.Relay.Tests.csproj' 'FullyQualifiedName~RelayTableGate'
$fit   = Invoke-GateTests 'dotnet\KcdMp.Client.Tests\KcdMp.Client.Tests.csproj' 'FullyQualifiedName~Wo203W164Fit'

$pass = $relay[0] -and $fit[0]
$verdict = if ($pass) { 'PASS' } else { 'FAIL' }
$line = "RESULT: $verdict relay-table passed=$($relay[1]) failed=$($relay[2]) total=$($relay[3]); w164-fit passed=$($fit[1]) failed=$($fit[2]) total=$($fit[3])"
Write-Host ""
Write-Host $line -ForegroundColor $(if ($pass) { 'Green' } else { 'Red' })
if ($pass) { exit 0 } else { exit 1 }
