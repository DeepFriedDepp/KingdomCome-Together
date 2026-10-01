# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-133: synthetic test for the Lua half of quest safety: the old quest
    layer (prompt, F11/F12, mp_quest_* fires, approach, divergence, gap toasts)
    is off while the agent's shared-world gate holds, and unchanged without it.
    No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO133Synthetic.lua. Live evidence: docs/WO-133-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO133Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO133Synthetic.lua') `
    -Title 'WO-133 synthetic quest-safety Lua test'
exit $LASTEXITCODE
