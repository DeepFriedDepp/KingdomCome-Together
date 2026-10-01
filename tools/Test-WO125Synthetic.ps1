# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-125: synthetic test for the Lua half of continuity (per-world Henry files):
    mp_join_henry auto|fresh|playlineN/file as the first-join answer, the
    snapshot QuickSave, the live Henry test, mp_henry_reset and mp_henry_files.
    No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO125Synthetic.lua. The flow itself is live-tested -- see
    docs/WO-125-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO125Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO125Synthetic.lua') `
    -Title 'WO-125 synthetic continuity Lua test'
exit $LASTEXITCODE
