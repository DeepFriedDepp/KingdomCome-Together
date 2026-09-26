<#
.SYNOPSIS
    WO-114: synthetic test for the Lua half of the leash -- the host's settings
    (mp_leash, mp_leash_warn_m, mp_leash_pull_m), the countdown row, the
    busy/mount answers, the dismount and the joiner's fast-travel switch. No
    game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under
    MoonSharp, engine stubbed, fake clock); scenario file
    Test-WO114Synthetic.lua. The leash rules themselves are the host agent's
    (LeashLogic, dotnet/KcdMp.Client.Tests/Wo114Tests.cs).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO114Synthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-WO114Synthetic.lua') `
    -Title 'WO-114 synthetic leash test'
exit $LASTEXITCODE
