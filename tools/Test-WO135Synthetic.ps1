<#
.SYNOPSIS
    WO-135: synthetic test for the Lua halves: the avatar's quiet groups, knockout
    sync and wake, takedown requests and the host's side, the avatar stand-up.
    No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO135Synthetic.lua. Live evidence: docs/WO-135-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO135Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO135Synthetic.lua') `
    -Title 'WO-135 synthetic puppet-and-knockout Lua test'
exit $LASTEXITCODE
