# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-179 0.1: the publishing gate -- nothing private leaves this machine in a push.

.DESCRIPTION
    Scans everything a push would publish that is not yet on the remote's main:
      * every ADDED line of every pending commit (code, comments, docs, test names, log tags -- all of it),
      * every file name a pending commit adds, renames or copies,
      * every pending commit message (subject and body),
    against a denylist that lives OUTSIDE the repository. The denylist's path comes from the environment
    variable KCDMP_PUBLISH_DENYLIST and is never committed. One entry per line, case-insensitive substring;
    a line "re:<regex>" is a .NET regular expression (case-insensitive); blank lines and lines starting with
    '#' are ignored.

    Fails closed: no variable, no file, a file inside the repository, or an empty list = the gate fails.
    Any hit = the gate fails and names the commit, the file and the line (on this console only).

    "Pending" = <base>..HEAD, where <base> is the remote's main: origin/main of this repository, or -- when
    this repository is a local clone (a release build's fresh clone) -- the origin/main of the repository it
    was cloned from. The remote is fetched first when it can be (read-only); a failed fetch uses the last
    known origin/main and says so.

.PARAMETER RepoDir
    The repository to scan (default: this script's repository).

.PARAMETER Push
    After a clean scan, push HEAD to the remote's main (git push origin HEAD:main) from the repository the
    commits came from. Without it the gate only scans.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Check-Publishing.ps1
    powershell -ExecutionPolicy Bypass -File tools\Check-Publishing.ps1 -Push
#>
param(
    [string]$RepoDir,
    [switch]$Push
)

$ErrorActionPreference = "Stop"
if (-not $RepoDir) { $RepoDir = Split-Path $PSScriptRoot -Parent }
$RepoDir = (Resolve-Path $RepoDir).Path

function Fail([string]$why) {
    Write-Host "PUBLISHING GATE: FAIL -- $why" -ForegroundColor Red
    exit 1
}

function Invoke-GitQ([string]$dir, [string[]]$gitArgs) {
    $out = & git.exe -C $dir @gitArgs 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return $out
}

# ---- the denylist (outside the repository, fail closed) ----------------------------------------------------------
$listPath = $env:KCDMP_PUBLISH_DENYLIST
if (-not $listPath) { Fail "KCDMP_PUBLISH_DENYLIST is not set (the denylist's path; the file lives outside the repository)" }
if (-not (Test-Path -LiteralPath $listPath -PathType Leaf)) { Fail "the denylist named by KCDMP_PUBLISH_DENYLIST does not exist" }
$listFull = (Resolve-Path -LiteralPath $listPath).Path
if ($listFull.StartsWith($RepoDir.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    Fail "the denylist is inside the repository -- it must live outside it"
}
$subs = New-Object System.Collections.Generic.List[string]
$rxs = New-Object System.Collections.Generic.List[System.Text.RegularExpressions.Regex]
foreach ($raw in [IO.File]::ReadAllLines($listFull)) {
    $e = $raw.Trim()
    if ($e.Length -eq 0 -or $e.StartsWith('#')) { continue }
    if ($e.StartsWith('re:')) {
        try { $rxs.Add((New-Object System.Text.RegularExpressions.Regex($e.Substring(3), 'IgnoreCase'))) }
        catch { Fail "a denylist regular expression does not parse (line: $($e.Length) chars)" }
    } else { $subs.Add($e) }
}
if (($subs.Count + $rxs.Count) -eq 0) { Fail "the denylist is empty" }

function Hit([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return $null }
    foreach ($s in $subs) { if ($text.IndexOf($s, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $s } }
    foreach ($r in $rxs) { $m = $r.Match($text); if ($m.Success) { return $m.Value } }
    return $null
}

# ---- the base: the remote's main ----------------------------------------------------------------------------------
$source = $RepoDir
$url = Invoke-GitQ $RepoDir @('config', '--get', 'remote.origin.url')
if ($url -and (Test-Path -LiteralPath $url -PathType Container)) {
    # a local clone (a release build's): its own origin is a folder -- the remote is that repository's origin
    $source = (Resolve-Path -LiteralPath $url).Path
}
$fetched = Invoke-GitQ $source @('fetch', '--quiet', 'origin', 'main')
if ($null -eq $fetched -and $LASTEXITCODE -ne 0) {
    Write-Host "PUBLISHING GATE: note -- 'git fetch origin main' failed in the source repository; using its last known origin/main" -ForegroundColor Yellow
}
$base = Invoke-GitQ $source @('rev-parse', 'origin/main')
if (-not $base) { Fail "no origin/main in $(Split-Path $source -Leaf) -- the pending range cannot be known" }
$base = ([string]$base).Trim()
if (-not (Invoke-GitQ $RepoDir @('cat-file', '-e', "$base^{commit}")) -and $LASTEXITCODE -ne 0) {
    Fail "the remote's main ($($base.Substring(0,10))) is not in this repository's history -- fetch first"
}
$commits = @(Invoke-GitQ $RepoDir @('rev-list', '--reverse', "$base..HEAD"))
$commits = @($commits | Where-Object { $_ })
Write-Host ("PUBLISHING GATE: {0} pending commit(s) over the remote's main {1}; denylist {2} entr(y/ies)" -f $commits.Count, $base.Substring(0, 10), ($subs.Count + $rxs.Count))

$hits = 0
foreach ($c in $commits) {
    $short = $c.Substring(0, 10)
    # the message, subject and body
    $msg = (& git -C $RepoDir log -1 --format=%B $c) -join "`n"
    $h = Hit $msg
    if ($h) { $hits++; Write-Host ("  HIT {0} commit message: '{1}'" -f $short, $h) -ForegroundColor Red }
    # file names added / renamed / copied
    foreach ($ln in @(& git -C $RepoDir show --format= --name-status -M -C $c)) {
        if (-not $ln) { continue }
        $parts = $ln -split "`t"
        if ($parts[0] -match '^[ARC]') {
            foreach ($p in $parts[1..($parts.Count - 1)]) {
                $h = Hit $p
                if ($h) { $hits++; Write-Host ("  HIT {0} file name {1}: '{2}'" -f $short, $p, $h) -ForegroundColor Red }
            }
        }
    }
    # every added line (binary files are listed by name only)
    $file = "?"; $lineNo = 0
    foreach ($ln in @(& git -C $RepoDir show --format= --unified=0 --no-color --no-ext-diff $c)) {
        if ($null -eq $ln) { continue }
        if ($ln.StartsWith('+++ ')) { $file = $ln.Substring(4) -replace '^b/', ''; continue }
        if ($ln.StartsWith('@@')) {
            if ($ln -match '\+(\d+)') { $lineNo = [int]$Matches[1] - 1 }
            continue
        }
        if ($ln.StartsWith('+')) {
            $lineNo++
            $h = Hit $ln.Substring(1)
            if ($h) { $hits++; Write-Host ("  HIT {0} {1}:{2}: '{3}'" -f $short, $file, $lineNo, $h) -ForegroundColor Red }
        }
    }
}

if ($hits -gt 0) { Fail "$hits hit(s) in what a push would publish -- rewrite those commits before pushing" }
Write-Host "PUBLISHING GATE: PASS -- nothing on the denylist in the pending commits" -ForegroundColor Green

if ($Push) {
    if ($commits.Count -eq 0) { Write-Host "PUBLISHING GATE: nothing to push"; exit 0 }
    $headHash = ((& git -C $RepoDir rev-parse HEAD) | Out-String).Trim()
    if ($source -ne $RepoDir) {
        # a clone: the commits are pushed from the repository they came from, and only if it holds the same HEAD
        $srcHead = ((& git -C $source rev-parse HEAD) | Out-String).Trim()
        if ($srcHead -ne $headHash) { Fail "the clone's HEAD is not the source repository's HEAD -- push from the source repository" }
    }
    & git -C $source push origin HEAD:main
    if ($LASTEXITCODE -ne 0) { Fail "git push failed" }
    Write-Host "PUBLISHING GATE: pushed $($headHash.Substring(0,10)) to the remote's main" -ForegroundColor Green
}
exit 0
