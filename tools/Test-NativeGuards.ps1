# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-151 Phase 0 static check: no silent fault guard and no argument that points at a
    value it does not own can come back into the native DLL.

.DESCRIPTION
    Static/lexical over native\KCDMP\*.cpp and *.h, no build needed. Assertions:

      1. No raw __try outside native\KCDMP\fault_guard.h. Every structured-exception
         guard goes through fault::guarded() / guarded_or() with a named site, which
         logs the first fault at once (with the faulting module+offset), again at the
         10th and the 100th, sums them every 60 s and switches a game-code site off
         after 8 (docs/WO-148-findings.md s7.12 item 1: 0.42.5-0.42.7's frame-rate
         collapse was a fault swallowed thousands of times by a raw guard).
      2. No build_argument( anywhere: a reflection argument is an rttr::Arg<T> (or
         StringArg) that holds its own value -- the pointer-taking builder let the
         stamina argument outlive its value (WO-148 s7.4).
      3. Every guarded call in the file names a site (KCDMP_FAULT_CALL/READ,
         KCDMP_SITE_CALL/READ or a fault::Site object) -- counted, as a sanity check
         that the scan saw the conversion at all.

    Comments are ignored (a // line may name __try). Run by native\Build-Native.ps1 before
    every build and by tools\Build-Installer.ps1 with the other static checks.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-NativeGuards.ps1
#>
[CmdletBinding()]
param(
    [string] $NativeDir
)

if (-not $NativeDir) {
    $root = $PSScriptRoot
    if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $NativeDir = Join-Path $root '..\native\KCDMP'
}

$ErrorActionPreference = 'Stop'

$script:pass = 0
$script:fail = 0
function Ok([string] $m)  { $script:pass++; Write-Host "  PASS  $m" -ForegroundColor Green }
function Bad([string] $m) { $script:fail++; Write-Host "  FAIL  $m" -ForegroundColor Red }
function Check([bool] $cond, [string] $m) { if ($cond) { Ok $m } else { Bad $m } }

if (-not (Test-Path $NativeDir)) { throw "native source folder not found at $NativeDir" }
$files = @(Get-ChildItem $NativeDir -File | Where-Object { $_.Extension -in '.cpp', '.h' })

Write-Host "`n=== WO-151 Phase 0: the native fault guards and reflection arguments, $NativeDir ===`n"
Check ($files.Count -gt 30) "found $($files.Count) native source files to check"

# Strips // comments (outside string literals) and /* */ blocks, line by line.
function Get-CodeLines([string] $path) {
    $out = New-Object System.Collections.Generic.List[object]
    $inBlock = $false
    $n = 0
    foreach ($line in [System.IO.File]::ReadAllLines($path)) {
        $n++
        $code = New-Object System.Text.StringBuilder
        $i = 0; $inStr = $false; $q = [char]0
        while ($i -lt $line.Length) {
            $c = $line[$i]
            if ($inBlock) {
                if ($c -eq '*' -and $i + 1 -lt $line.Length -and $line[$i + 1] -eq '/') { $inBlock = $false; $i += 2; continue }
                $i++; continue
            }
            if ($inStr) {
                [void]$code.Append($c)
                if ($c -eq '\') { if ($i + 1 -lt $line.Length) { [void]$code.Append($line[$i + 1]) }; $i += 2; continue }
                if ($c -eq $q) { $inStr = $false }
                $i++; continue
            }
            if ($c -eq '"' -or $c -eq "'") { $inStr = $true; $q = $c; [void]$code.Append($c); $i++; continue }
            if ($c -eq '/' -and $i + 1 -lt $line.Length -and $line[$i + 1] -eq '/') { break }
            if ($c -eq '/' -and $i + 1 -lt $line.Length -and $line[$i + 1] -eq '*') { $inBlock = $true; $i += 2; continue }
            [void]$code.Append($c); $i++
        }
        $out.Add([pscustomobject]@{ N = $n; Text = $code.ToString() })
    }
    return $out
}

$rawTry = @()
$rawBuild = @()
$sites = 0
$guards = 0
foreach ($f in $files) {
    $code = Get-CodeLines $f.FullName
    foreach ($l in $code) {
        if ($l.Text -match '\b__try\b' -and $f.Name -ne 'fault_guard.h') { $rawTry += "$($f.Name):$($l.N)  $($l.Text.Trim())" }
        if ($l.Text -match '\bbuild_argument\s*\(') { $rawBuild += "$($f.Name):$($l.N)  $($l.Text.Trim())" }
        $sites += ([regex]::Matches($l.Text, '\bKCDMP_(FAULT|SITE)_(CALL|READ)\s*\(')).Count
        $sites += ([regex]::Matches($l.Text, '\bfault::Site\s+[A-Za-z_]\w*\s*\{')).Count
        $guards += ([regex]::Matches($l.Text, '\bfault::guarded(_or)?\s*(<[^>]*>)?\s*\(')).Count
    }
}

Check ($rawTry.Count -eq 0) "no raw __try outside fault_guard.h (every guard is fault::guarded with a named site)"
foreach ($r in $rawTry) { Write-Host "        raw __try: $r" -ForegroundColor Red }
Check ($rawBuild.Count -eq 0) "no build_argument( anywhere (a reflection argument holds its own value: rttr::Arg<T>)"
foreach ($r in $rawBuild) { Write-Host "        build_argument: $r" -ForegroundColor Red }
Check ($sites -ge 280) "the guards name their sites ($sites site declarations, $guards guarded calls)"
Check ($guards -ge 250) "the guarded calls are there ($guards)"

$h = Join-Path $NativeDir 'fault_guard.h'
$hc = if (Test-Path $h) { [System.IO.File]::ReadAllText($h) } else { '' }
Check ($hc -match 'kSwitchOffAfter\s*=\s*8\b') "fault_guard.h switches a game-code site off after 8 faults"
Check ($hc -match '__except\s*\(\s*filter\(') "fault_guard.h's guard records every fault through its filter"

Write-Host "`n$($script:pass) passed, $($script:fail) failed`n"
if ($script:fail -gt 0) { exit 1 }
exit 0
