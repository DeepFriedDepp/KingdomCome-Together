<#
.SYNOPSIS
    WO-147: synthetic test for the Lua half of the joiner's fight and the leash:
    the hostile copies near the joiner (relationship, animals, range, down/dead),
    the forced pull's conversation end, the three switches. No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO147Synthetic.lua. Live evidence: docs/WO-147-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO147Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO147Synthetic.lua') `
    -Title 'WO-147 synthetic fight-and-leash Lua test'
exit $LASTEXITCODE
