<#
.SYNOPSIS
    WO-132: synthetic test for the Lua half of damage safety: a peer's avatar at its
    down (bleeding healed, hidden) and wake (shown), and the heal after a hit.
    No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO132Synthetic.lua. Live evidence: docs/WO-132-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO132Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO132Synthetic.lua') `
    -Title 'WO-132 synthetic damage-safety Lua test'
exit $LASTEXITCODE
