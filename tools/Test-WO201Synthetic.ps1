# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-201: synthetic test for the Lua halves of the field-session fixes (the
    inventory screen guard, the twins grace, the rescue toasts, ASCII toasts,
    the stale queue, the host's mount read and the saddle rule, the host-busy
    toast, the save lock, hand props, mark notes). No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); different scenario file
    (Test-WO201Synthetic.lua). See that file's header for the scenario list
    and for what this deliberately does NOT prove.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO200Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO201Synthetic.lua') `
    -Title 'WO-201 synthetic field-session fixes test'
exit $LASTEXITCODE
