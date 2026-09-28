<#
.SYNOPSIS
    WO-140: synthetic test for the Lua half of sleeping together: the bed held
    before the lie-down, the prompt, the answers, the own-world line, the
    backstop and the switches. No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO140Synthetic.lua. Live evidence: docs/WO-140-findings.md.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO140Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO140Synthetic.lua') `
    -Title 'WO-140 synthetic sleeping-together Lua test'
exit $LASTEXITCODE
