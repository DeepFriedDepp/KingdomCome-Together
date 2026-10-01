# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-148: synthetic test for the Lua half of carrying on the other screen: this
    player's grab and set-down, a partner's carry on its avatar, nothing alive moved,
    the landing rule and its fail-safe returns, the loser and the leaver, sacks, the
    switches. No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO148Synthetic.lua. Live evidence: docs/WO-148-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO148Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO148Synthetic.lua') `
    -Title 'WO-148 synthetic carrying Lua test'
exit $LASTEXITCODE
