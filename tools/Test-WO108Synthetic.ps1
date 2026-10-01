# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-108: synthetic test for the pause lever's 0.26.4 wiring -- shipped
    defaults, the suspend-set invariant, identity logging, the relax tag,
    mp_resume_all, the two presets, the coverage-gap detector, the release
    dwell and the reload re-assert. No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO108Synthetic.lua. See that file's header for the scenario list and
    for what this deliberately does NOT prove (anything about the engine).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO108Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO108Synthetic.lua') `
    -Title 'WO-108 synthetic pause-lever test'
exit $LASTEXITCODE
