<#
.SYNOPSIS
    WO-137: synthetic test for the Lua half of shared quests: dead is dead
    (no pause or detach on a corpse, dead stand-ins), talking to a host copy,
    the host's hold, the kill switch. No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO137Synthetic.lua. Live evidence: docs/WO-137-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO137Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO137Synthetic.lua') `
    -Title 'WO-137 synthetic shared-quests Lua test'
exit $LASTEXITCODE
