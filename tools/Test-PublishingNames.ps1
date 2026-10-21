# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-202: the publishing gate's player-name rule, on a throwaway repository (made and removed in %TEMP%).

.DESCRIPTION
    A bare "remote" and a working repository whose git-ignored notes-x\WO-999\field\ holds a fake bundle: a Steam name line, a join
    line, a Windows user folder and a mark note that names a player by the start of their handle. Each case is one pending commit over the
    remote's main, scanned by tools\Check-Publishing.ps1 (no push):
      * a line naming any of them fails the gate;
      * the repository's own URL, which holds its owner's handle, passes;
      * the owner's handle anywhere else fails;
      * a line with none of them passes.
    The fake names are this script's own (no real player's name is in it).
#>
$ErrorActionPreference = 'Stop'
$gate = Join-Path $PSScriptRoot 'Check-Publishing.ps1'
$tmp = Join-Path $env:TEMP ("w202-pubnames-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$owner = 'ExampleOwnerQz'
$fails = 0
function Check([string]$what, [bool]$ok) {
    if ($ok) { Write-Host "PASS  $what" -ForegroundColor Green } else { Write-Host "FAIL  $what" -ForegroundColor Red; $script:fails++ }
}
function G([string]$dir, [string[]]$a) { & git.exe -C $dir @a 2>&1 | Out-Null; if ($LASTEXITCODE -ne 0) { throw "git $($a -join ' ') failed" } }
try {
    New-Item -ItemType Directory -Force (Join-Path $tmp 'remote.git') | Out-Null
    G (Join-Path $tmp 'remote.git') @('init', '--bare', '-q', '-b', 'main')
    $repo = Join-Path $tmp 'repo'
    New-Item -ItemType Directory -Force $repo | Out-Null
    G $repo @('init', '-q', '-b', 'main')
    G $repo @('config', 'user.email', 'test@example.invalid'); G $repo @('config', 'user.name', 'test')
    G $repo @('remote', 'add', 'origin', (Join-Path $tmp 'remote.git'))
    Set-Content -LiteralPath (Join-Path $repo '.gitignore') -Value 'notes-x/' -Encoding ascii
    Set-Content -LiteralPath (Join-Path $repo 'README.md') -Value 'start' -Encoding ascii
    G $repo @('add', '.'); G $repo @('commit', '-q', '-m', 'start'); G $repo @('push', '-q', 'origin', 'main')
    # the remote's URL as the gate reads it: a GitHub URL carrying the owner's handle (the push above already used the folder)
    G $repo @('remote', 'set-url', 'origin', "https://github.com/$owner/ExampleRepo.git")
    G $repo @('update-ref', 'refs/remotes/origin/main', 'HEAD')
    $field = Join-Path $repo 'notes-x\WO-999\field\A-h1'
    New-Item -ItemType Directory -Force $field | Out-Null
    $users = 'Users'   # the user-folder path is put together at run time: written out, it would trip the denylist on this very file
    @(
        '12:00:00.000 m=1 off=? [KcdLog] Found Steam name: Fakeplayer Zed',
        "12:00:01.000 m=2 off=-+0 MP-JOIN host: join 0x12345678 requested by SynthHandleQx (ghost 1) -- timeout 180 s once paused",
        "12:00:02.000 m=3 off=-+0 MP-CUTSCENE side=peer ghost=0 who=`"$owner`" state=start",
        "12:00:03.000 m=4 off=-+0 E:\$users\WinUserQv\Saved Games\kingdomcome2\saves",
        '[KCD2-MP] MP-MARK odd world_time=1 note=Synth was standing there -- the tester''s marker t=1.0'
    ) | Set-Content -LiteralPath (Join-Path $field 'agent.log') -Encoding ascii
    $deny = Join-Path $tmp 'deny.txt'
    Set-Content -LiteralPath $deny -Value 'zz-not-in-any-line-zz' -Encoding ascii
    $env:KCDMP_PUBLISH_DENYLIST = $deny
    $cases = @(
        @('the Steam name', 'see Fakeplayer Zed here', $false),
        @('a join requester', 'thanks SynthHandleQx', $false),
        @('a Windows user folder', 'path of WinUserQv', $false),
        @('a note''s name (the start of a handle)', 'Synth said hi', $false),
        @('the owner''s handle outside the URL', "by $owner", $false),
        @('the repository''s own URL', "see https://github.com/$owner/ExampleRepo for the code", $true),
        @('nothing of them', 'a plain line', $true)
    )
    foreach ($c in $cases) {
        G $repo @('reset', '-q', '--hard', 'refs/remotes/origin/main')
        Add-Content -LiteralPath (Join-Path $repo 'README.md') -Value $c[1] -Encoding ascii
        G $repo @('commit', '-q', '-am', 'case')
        & powershell -NoProfile -ExecutionPolicy Bypass -File $gate -RepoDir $repo *> $null
        Check ("{0}: the gate {1}" -f $c[0], $(if ($c[2]) { 'passes' } else { 'fails' })) (($LASTEXITCODE -eq 0) -eq $c[2])
    }
} finally {
    Remove-Item -Recurse -Force -LiteralPath $tmp -ErrorAction SilentlyContinue
}
if ($fails) { Write-Host "Test-PublishingNames: $fails failure(s)" -ForegroundColor Red; exit 1 }
Write-Host 'Test-PublishingNames: all passed' -ForegroundColor Green
exit 0
