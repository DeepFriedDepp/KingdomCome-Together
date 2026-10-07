# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-159 Phase 5: checks a start save against the recipe before it is bundled. Plain PASS / FAIL with reasons.

.DESCRIPTION
    The recipe (docs/WO-159-findings.md): saved on the Modding Tools build, AFTER the prologue, as Henry (the player
    entity is not player_bohuta), in a world no partner ever joined and with no mod data in it, and nothing in the
    save's header that names the account that made it. A calm spot with nothing scripted running cannot be read
    from the file: check it by eye.

    Checks, through the agent's own save reader (KcdMpClient.exe --w159 validate):
      reads and verifies             the footer md5, the block framing, a player_henry soul record
      Modding Tools build            the header's build is not a regular-game build
      after the prologue, as Henry   the player entity is player_henry
      a playthrough seed             the world's identity is there (New adventure gives every copy a new one)
      no partner / mod data          no "kcd2mp" name anywhere in the world (a partner's figure, a mod entity)
      no account or machine name     DebugInfoHistory UserName and BuildComputer are empty
      no mods listed in the header   UsedMods is empty

    -WriteScrubbed <out.whs> writes a copy with the account and machine names cleared and the mods list emptied (header only;
    the world is not touched), when the save passes the first four checks. Supply that copy. The input is never
    changed.

.PARAMETER Path
    A .whs file, or a folder holding exactly one (e.g. assets\start-save).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Validate-StartSave.ps1 -Path assets\start-save
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Validate-StartSave.ps1 -Path C:\temp\permanent002.whs -WriteScrubbed C:\temp\clean\permanent002.whs
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Path,
    [string] $WriteScrubbed = '',
    [string] $Agent = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

if (-not $Agent) {
    $Agent = Get-ChildItem (Join-Path $repo 'dotnet\KcdMp.Client\bin') -Recurse -Filter KcdMpClient.exe -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Agent -or -not (Test-Path $Agent)) { Write-Host 'FAIL  the agent (KcdMpClient.exe) was not found: build dotnet\KcdMp.Client first, or pass -Agent' -ForegroundColor Red; exit 2 }

$file = $Path
if (Test-Path $Path -PathType Container) {
    $whs = @(Get-ChildItem $Path -Filter *.whs)
    if ($whs.Count -ne 1) { Write-Host "FAIL  $Path holds $($whs.Count) saves (one is expected)" -ForegroundColor Red; exit 1 }
    $file = $whs[0].FullName
}
if (-not (Test-Path $file -PathType Leaf)) { Write-Host "FAIL  no such file: $file" -ForegroundColor Red; exit 1 }

$a = @('--w159', 'validate', '--file', (Resolve-Path $file).Path)
if ($WriteScrubbed) {
    $outDir = Split-Path -Parent $WriteScrubbed
    if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Force $outDir | Out-Null }
    if (Test-Path $WriteScrubbed) { Write-Host "FAIL  $WriteScrubbed exists; refusing to overwrite" -ForegroundColor Red; exit 2 }
    $a += @('--write-scrubbed', [IO.Path]::GetFullPath($WriteScrubbed))
}
$line = & $Agent @a | Where-Object { $_ -like 'W159 *' } | Select-Object -First 1
if (-not $line) { Write-Host 'FAIL  the agent gave no answer' -ForegroundColor Red; exit 2 }
$r = ($line.Substring(5) | ConvertFrom-Json)

Write-Host "Start save: $(Split-Path -Leaf $file)"
foreach ($c in $r.checks) {
    $tag = if ($c.pass) { 'PASS' } else { 'FAIL' }
    $col = if ($c.pass) { 'Green' } else { 'Red' }
    $d = if ($c.detail -and $c.detail -ne 'ok') { "  -- $($c.detail)" } else { '' }
    Write-Host ("  {0}  {1}{2}" -f $tag, $c.name, $d) -ForegroundColor $col
}
Write-Host '  ----  a calm spot with nothing scripted running: not readable from the file (check it by eye)'
if ($r.scrubbed) { Write-Host "Scrubbed copy written: $($r.scrubbed) (check it again with this tool)" -ForegroundColor Cyan }
if ($r.pass) { Write-Host 'RESULT: PASS' -ForegroundColor Green; exit 0 }
Write-Host 'RESULT: FAIL' -ForegroundColor Red
exit 1
