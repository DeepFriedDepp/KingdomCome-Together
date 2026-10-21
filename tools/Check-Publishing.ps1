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
    # WO-202: under "Stop", Windows PowerShell 5.1 turns a git line on stderr into a terminating error even with 2>$null -- a failed
    # fetch (offline, a moved remote) ended the gate instead of using the last known origin/main as documented above
    try { $out = & git.exe -C $dir @gitArgs 2>$null } catch { return $null }
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

# a hit is shown masked (its first two characters and its length): a name never lands in a console transcript either
function Mask([string]$h) { if ($h.Length -le 2) { return '*' * $h.Length }; return $h.Substring(0, 2) + ('*' * ($h.Length - 2)) }

function Hit([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return $null }
    if ($script:repoUrls) { foreach ($u in $script:repoUrls) { $text = $text.Replace($u, ' ') } }   # WO-202: the repository's own URL only
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
# ---- WO-202: the players' names and handles in the field bundles (gathered now, never written anywhere) -----------------
# The testers' unpacked log bundles live in the repository's git-ignored folders, in a "field" folder per work order (<ignored>\<WO>\
# field\); the folders are found through git itself, never named here. Every name or handle a bundle carries -- a Steam name, the
# join's partner, who asked for the world or is ready in it, who played a cutscene, a partner knight's hover name, a Windows user folder,
# and a word of a mark's note that is such a name or its start -- gates like the denylist. The repository's own URL is the only
# exception: it is taken out of every scanned text before the names are matched. The names are only counted here, never printed.
$namePats = @(
    'Found Steam name: ([^\r\n]+?)\s*$', 'MP-JOIN[^\r\n]*? partner=(\S+)',
    'requested by ([^\r\n]+?) \(ghost \d+\)', 'join 0x[0-9a-f]+: ([^\r\n]+?) is ready', 'who="([^"]+)"',
    '-NAMES slot=\d+ revision=\d+ name="([^"]+)"', '-MARK slot=\d+ colour=\w+ dir=\w+ name="([^"]+)"',
    '[A-Za-z]:[\\/]+Users[\\/]+([^\\/"\r\n]+)[\\/]') | ForEach-Object { New-Object System.Text.RegularExpressions.Regex($_, 'Multiline') }
$notePat = New-Object System.Text.RegularExpressions.Regex('note=([^\r\n]*?)(?: -- the tester|"|$)', 'Multiline')
$nameSkip = @('public', 'default', 'all users', 'default user', '%user%', 'none', 'true', 'false', 'yes', 'nil', 'null', 'off')
$names = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$noteWords = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$fieldFiles = 0
$fieldDirs = New-Object System.Collections.Generic.List[string]
foreach ($root in @($source, $RepoDir) | Select-Object -Unique) {
    # the top-level folders git ignores, then <folder>\<any>\field
    foreach ($ig in @(Invoke-GitQ $root @('ls-files', '--others', '--ignored', '--exclude-standard', '--directory'))) {
        if (-not $ig -or $ig -notmatch '^[^/]+/$') { continue }
        $top = Join-Path $root $ig.TrimEnd('/')
        foreach ($sub in @(Get-ChildItem -LiteralPath $top -Directory -ErrorAction SilentlyContinue)) {
            $fd = Join-Path $sub.FullName 'field'
            if ((Test-Path -LiteralPath $fd -PathType Container) -and -not $fieldDirs.Contains($fd)) { $fieldDirs.Add($fd) }
        }
    }
}
foreach ($fd in $fieldDirs) {
    foreach ($f in @(Get-ChildItem -LiteralPath $fd -Recurse -File -Include '*.log', 'MANIFEST.txt', '*.json' -ErrorAction SilentlyContinue)) {
        $fieldFiles++
        $t = [IO.File]::ReadAllText($f.FullName)
        foreach ($rx in $namePats) {
            foreach ($m in $rx.Matches($t)) {
                $v = $m.Groups[1].Value.Trim()
                if ($v.Length -ge 3 -and $nameSkip -notcontains $v.ToLowerInvariant() -and $v -notmatch '^[\d.:+-]+$') { [void]$names.Add($v) }
            }
        }
        foreach ($m in $notePat.Matches($t)) {
            foreach ($w in [regex]::Matches($m.Groups[1].Value, '[A-Za-z][A-Za-z0-9_]{3,}')) { [void]$noteWords.Add($w.Value) }
        }
    }
}
foreach ($w in @($noteWords)) {
    foreach ($n in @($names)) { if ($n.StartsWith($w, [StringComparison]::OrdinalIgnoreCase)) { [void]$names.Add($w); break } }
}
foreach ($n in $names) { $subs.Add($n) }
# the repository's own URL (and its owner/name path) is the one place its owner's handle may appear
$repoUrls = New-Object System.Collections.Generic.List[string]
if ($url) {
    $u = ([string]$url).Trim()
    if (-not (Test-Path -LiteralPath $u -PathType Container)) { $repoUrls.Add($u); $repoUrls.Add(($u -replace '\.git$', '')) }
}
$srcUrl = Invoke-GitQ $source @('config', '--get', 'remote.origin.url')
if ($srcUrl) { $s2 = ([string]$srcUrl).Trim(); $repoUrls.Add($s2); $repoUrls.Add(($s2 -replace '\.git$', '')) }
foreach ($u in @($repoUrls)) { if ($u -match 'github\.com[:/](.+?)(?:\.git)?$') { $repoUrls.Add($Matches[1]) } }
$repoUrls = @($repoUrls | Where-Object { $_ } | Select-Object -Unique | Sort-Object Length -Descending)
Write-Host ("PUBLISHING GATE: {0} player name(s) or handle(s) from {1} field bundle file(s) gate too (the repository's own URL excepted)" -f $names.Count, $fieldFiles)

$commits = @(Invoke-GitQ $RepoDir @('rev-list', '--reverse', "$base..HEAD"))
$commits = @($commits | Where-Object { $_ })
Write-Host ("PUBLISHING GATE: {0} pending commit(s) over the remote's main {1}; denylist {2} entr(y/ies)" -f $commits.Count, $base.Substring(0, 10), ($subs.Count + $rxs.Count))

$hits = 0
foreach ($c in $commits) {
    $short = $c.Substring(0, 10)
    # the message, subject and body
    $msg = (& git -C $RepoDir log -1 --format=%B $c) -join "`n"
    $h = Hit $msg
    if ($h) { $hits++; Write-Host ("  HIT {0} commit message: '{1}'" -f $short, (Mask $h)) -ForegroundColor Red }
    # file names added / renamed / copied
    foreach ($ln in @(& git -C $RepoDir show --format= --name-status -M -C $c)) {
        if (-not $ln) { continue }
        $parts = $ln -split "`t"
        if ($parts[0] -match '^[ARC]') {
            foreach ($p in $parts[1..($parts.Count - 1)]) {
                $h = Hit $p
                if ($h) { $hits++; Write-Host ("  HIT {0} file name {1}: '{2}'" -f $short, $p, (Mask $h)) -ForegroundColor Red }
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
            if ($h) { $hits++; Write-Host ("  HIT {0} {1}:{2}: '{3}'" -f $short, $file, $lineNo, (Mask $h)) -ForegroundColor Red }
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
