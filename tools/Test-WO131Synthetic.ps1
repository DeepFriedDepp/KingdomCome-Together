<#
.SYNOPSIS
    WO-131: synthetic test for the Lua halves of combat and bodies: the copy guard,
    the loot rule, perception, stand-ins, stand-ups, the dialogue hold, the far band.
    No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO131Synthetic.lua. Live evidence: docs/WO-131-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO131Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO131Synthetic.lua') `
    -Title 'WO-131 synthetic combat-and-bodies Lua test'
exit $LASTEXITCODE
