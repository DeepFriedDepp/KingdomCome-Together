<#
.SYNOPSIS
    WO-138: synthetic test for the Lua half of no pausing: the Lua sender quiet
    while the DLL streams, the rescan set to the DLL, the dialogue edge, the
    joiner's hold (hold, don't hide), the inventory lever. No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO138Synthetic.lua. Live evidence: docs/WO-138-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO138Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO138Synthetic.lua') `
    -Title 'WO-138 synthetic no-pausing Lua test'
exit $LASTEXITCODE
