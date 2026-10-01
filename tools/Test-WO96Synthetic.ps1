# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-96: synthetic test for divergence-gated prompting and WAITING_FOR_PEER
    -- the readiness prompt raised from the story-divergence signal instead
    of beat proximity, the explicit waiting state when there is nothing left
    to offer, the debounce, spent/declined beats, the prologue, and the
    2026-09-13 host's own sequence replayed. No game, relay or agent needed.

.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); different scenario file
    (Test-WO96Synthetic.lua). See that file's header for the scenario list
    and for what this deliberately does NOT prove.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO96Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO96Synthetic.lua') `
    -Title 'WO-96 synthetic divergence-prompt / WAITING_FOR_PEER test'
exit $LASTEXITCODE
