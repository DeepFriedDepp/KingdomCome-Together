<#
.SYNOPSIS
    WO-139: synthetic test for the Lua half of crime and guards: detection,
    the stop on the joiner, the host's witnesses and guards, the legal horses,
    no robbing each other, no time skip for punishment. No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO139Synthetic.lua. Live evidence: docs/WO-139-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO139Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO139Synthetic.lua') `
    -Title 'WO-139 synthetic crime-and-guards Lua test'
exit $LASTEXITCODE
