<#
    WO-134 Phase 1: the drop regression (WO-48 item sync must behave exactly as
    before), in three session configurations. No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO134DropsSynthetic.lua. Live evidence: docs/WO-134-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO134DropsSynthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO134DropsSynthetic.lua') `
    -Title 'WO-134 drops synthetic drop regression Lua test'
exit $LASTEXITCODE
