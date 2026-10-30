# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
  WO-211 0.2: write the build id into the repo root's BUILD_ID (git-ignored) and print it.

.DESCRIPTION
  The version stays the maintainer's (VERSION). Builds of one version are told apart by the build id: the short commit of HEAD and the
  build time (UTC, yyMMddHHmm), e.g. d4f2cb4-2610300412. Every assembly (Directory.Build.props), the DLL (native/KCDMP/CMakeLists.txt) and
  the installer's name read it. A working tree with uncommitted changes gets a "-dirty" suffix after the hash. Reads git only; never
  writes VERSION.
#>
param([string]$Repo = (Split-Path -Parent $PSScriptRoot), [string]$Id = '')
$ErrorActionPreference = 'Stop'
if (-not $Id) {
    $hash = (& git -C $Repo rev-parse --short=7 HEAD).Trim()
    if (-not $hash) { throw 'Write-BuildId: no git HEAD' }
    $dirty = (& git -C $Repo status --porcelain --untracked-files=no) | Where-Object { $_ -and ($_ -notmatch '^\s*M\s+docs/') }
    $suffix = if ($dirty) { '-dirty' } else { '' }
    $Id = '{0}{1}-{2}' -f $hash, $suffix, (Get-Date).ToUniversalTime().ToString('yyMMddHHmm')
}
if ($Id -notmatch '^[0-9A-Za-z.-]{1,40}$') { throw "Write-BuildId: '$Id' is not a build id ([0-9A-Za-z.-]{1,40})" }
Set-Content -Path (Join-Path $Repo 'BUILD_ID') -Value $Id -NoNewline -Encoding ascii
$Id
