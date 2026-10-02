# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-151: synthetic test for the Lua half of WO-151: the copy in a fight (the lying hold, no tools),
    the horse puppet's gait, one owner for a ridden horse, block-only holds, one report per theft, joint
    responsibility for crimes and the forgetting of a cleared record. No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO151Synthetic.lua. Live evidence: docs/WO-151-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO151Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO151Synthetic.lua') `
    -Title 'WO-151 synthetic Lua test'
exit $LASTEXITCODE
