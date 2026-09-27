<#
    WO-134: the new rules for world items (bodies, loose items, chests). No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO134Synthetic.lua. Live evidence: docs/WO-134-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO134Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO134Synthetic.lua') `
    -Title 'WO-134 synthetic world-items Lua test'
exit $LASTEXITCODE
