# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-154 Phase 7b: synthetic test for the mod menu's Lua half. No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under MoonSharp, engine stubbed,
    fake clock); scenario file Test-WO154Synthetic.lua: the keys through the real OnAction hook, the
    open/close rules, navigation, every item's change through its console command's own function,
    the joiner's locked host levers, the clean screen, fast travel with WO-114's block, the agent's
    restore, mp_menu. The agent's half: dotnet/KcdMp.Client.Tests/Wo154MenuTests.cs.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO154Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO154Synthetic.lua') `
    -Title 'WO-154 synthetic mod menu Lua test'
exit $LASTEXITCODE
