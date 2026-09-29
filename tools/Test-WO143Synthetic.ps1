<#
.SYNOPSIS
    WO-143: synthetic test for the Lua half of activities part 2: the five
    switches, the temporary tools, the forced looks, the status line. No game,
    relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO143Synthetic.lua. Live evidence: docs/WO-143-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO143Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO143Synthetic.lua') `
    -Title 'WO-143 synthetic activities part 2 Lua test'
exit $LASTEXITCODE
