<#
.SYNOPSIS
    WO-141: synthetic test for the Lua half of activities and animal attacks:
    the switches, the player's one-shots at an object (the trough's wash), the
    avatar's nameplate key, the status line. No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO141Synthetic.lua. Live evidence: docs/WO-141-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO141Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO141Synthetic.lua') `
    -Title 'WO-141 synthetic activities Lua test'
exit $LASTEXITCODE
