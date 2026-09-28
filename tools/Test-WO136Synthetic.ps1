<#
.SYNOPSIS
    WO-136: synthetic test for the Lua halves: the load hold, animal stand-ins,
    the rider-owned horse, the preset removal, the torch.
    No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO136Synthetic.lua. Live evidence: docs/WO-136-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO136Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO136Synthetic.lua') `
    -Title 'WO-136 synthetic world-presence Lua test'
exit $LASTEXITCODE
