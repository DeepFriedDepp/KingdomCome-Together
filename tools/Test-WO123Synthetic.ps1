# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-123: synthetic test for the Lua half of the join -- the host's pause:
    busy deferral, ratio + NPC list + input hold, resume by list, the safety timer,
    mp_join_cancel, mp_join_timeout and the WO123-BUILD marker. No game,
    relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO123Synthetic.lua. The engine's side (the clock and NPCs actually freezing,
    the input hold) is live-tested -- see docs/WO-123-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO123Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO123Synthetic.lua') `
    -Title 'WO-123 synthetic join Lua test'
exit $LASTEXITCODE
