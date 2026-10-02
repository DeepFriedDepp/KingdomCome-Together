# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    Tests the installer's Steam/Modding-Tools detection against synthetic
    fixtures and against this machine's real Steam library.

.DESCRIPTION
    Builds a fixture tree of fake Steam libraries (multi-library, missing app,
    malformed vdf, retail-instead-of-modding-tools, ...), compiles
    installer\tests\SteamDetectProbe.iss -- which #includes the installer's
    own installer\SteamDetect.iss and carries the same setup helper
    (KcdMpSetup.exe, WO-150), so this exercises the shipping code rather than
    a copy of it -- runs it against each fixture, and asserts the result.

    WO-150 adds the four installer cases on fake libraries: nothing installed;
    the Modding Tools installed but not linked; linked but no mod; everything
    in place. Setup places the mod only in the last two.

    No game and no Steam interaction required. Safe to run any time: nothing
    outside the fixture tree is written, and the real machine is only read.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-InstallerDetect.ps1
#>
param(
    [string]$WorkDir = (Join-Path $env:TEMP "kcdmp-detect-fixtures"),
    # WO-150: the helper the probe carries. Default: the payload's, else the AOT publish output (built when missing).
    [string]$HelperExe
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

$pass = 0
$fail = 0
function Assert-That($name, $condition, $detail) {
    if ($condition) {
        $script:pass++
        Write-Host ("  PASS  {0}" -f $name)
    } else {
        $script:fail++
        Write-Host ("  FAIL  {0}  --  {1}" -f $name, $detail) -ForegroundColor Red
    }
}

function Get-Iscc {
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    foreach ($candidate in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "Inno Setup 6 not found. Install it with: winget install --id JRSoftware.InnoSetup"
}

# --- fixtures ------------------------------------------------------------
#
# Real Steam metadata, only smaller: libraryfolders.vdf and appmanifest are
# both the same flat "key" "value" text format, tab-indented, with path
# separators doubled.

function New-Dir($path) { New-Item -ItemType Directory -Force -Path $path | Out-Null }
function New-EmptyFile($path) {
    New-Dir (Split-Path $path -Parent)
    Set-Content -Path $path -Value "" -Encoding ascii
}

function New-LibraryFoldersVdf($steamRoot, $paths) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('"libraryfolders"')
    [void]$sb.AppendLine('{')
    for ($i = 0; $i -lt $paths.Count; $i++) {
        $escaped = $paths[$i] -replace '\\', '\\'
        [void]$sb.AppendLine("`t`"$i`"")
        [void]$sb.AppendLine("`t{")
        [void]$sb.AppendLine("`t`t`"path`"`t`t`"$escaped`"")
        [void]$sb.AppendLine("`t`t`"label`"`t`t`"`"")
        [void]$sb.AppendLine("`t}")
    }
    [void]$sb.AppendLine('}')
    New-Dir "$steamRoot\steamapps"
    Set-Content -Path "$steamRoot\steamapps\libraryfolders.vdf" -Value $sb.ToString() -Encoding ascii
}

function New-AppManifest($library, $installDir) {
    New-Dir "$library\steamapps"
    $text = @"
"AppState"
{
	"appid"		"2429020"
	"name"		"Kingdom Come: Deliverance II Modding tools"
	"installdir"		"$installDir"
}
"@
    Set-Content -Path "$library\steamapps\appmanifest_2429020.acf" -Value $text -Encoding ascii
}

# A Modding Tools layout: the two DLLs the plugin needs beside the exe, and
# the Data/Engine pair the pre-WO-150 detection used to identify the root.
function New-ModdingToolsLayout($library, $installDir) {
    $base = "$library\steamapps\common\$installDir"
    New-Dir "$base\Data"
    New-Dir "$base\Engine"
    New-EmptyFile "$base\Bin\Win64ReleaseSteamLTO_DLL\KingdomCome.exe"
    New-EmptyFile "$base\Bin\Win64ReleaseSteamLTO_DLL\Framework.dll"
    New-EmptyFile "$base\Bin\Win64ReleaseSteamLTO_DLL\CrySystem.dll"
    New-EmptyFile "$base\Bin\Win64ReleaseSteamLTO_DLL\WHGame.dll"
    return $base
}

# Retail: same exe name, same WHGame.dll, monolithic -- no Framework/CrySystem.
function New-RetailLayout($library, $installDir) {
    $base = "$library\steamapps\common\$installDir"
    New-Dir "$base\Data"
    New-Dir "$base\Engine"
    New-EmptyFile "$base\Bin\Win64MasterMasterSteamPGO\KingdomCome.exe"
    New-EmptyFile "$base\Bin\Win64MasterMasterSteamPGO\WHGame.dll"
}

# WO-150: the game itself (app 1771300), with paks laid out like the real one's.
function New-GameLayout($library) {
    New-Dir "$library\steamapps"
    Set-Content -Path "$library\steamapps\appmanifest_1771300.acf" -Encoding ascii -Value @"
"AppState"
{
	"appid"		"1771300"
	"StateFlags"		"4"
	"installdir"		"KingdomComeDeliverance2"
}
"@
    $g = "$library\steamapps\common\KingdomComeDeliverance2"
    foreach ($rel in @("Data\Tables.pak", "Data\Scripts.pak", "Localization\English_xml.pak", "Data\Levels\klaster\level.pak")) {
        New-Dir (Split-Path "$g\$rel" -Parent)
        Set-Content -Path "$g\$rel" -Value "pak $rel" -Encoding ascii
    }
    return $g
}

# WO-150: the workspace, linked the way the launcher links it on one drive (hard links: no admin needed).
function Link-Workspace($game, $mt) {
    foreach ($f in Get-ChildItem $game -Recurse -Filter *.pak -File) {
        $rel = $f.FullName.Substring($game.Length + 1)
        $dst = Join-Path $mt $rel
        New-Dir (Split-Path $dst -Parent)
        New-Item -ItemType HardLink -Path $dst -Target $f.FullName | Out-Null
    }
}

Write-Host "Building fixtures in $WorkDir ..."
if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Dir $WorkDir

# multi-library: app lives in the second library, not the Steam root.
New-Dir "$WorkDir\multi\Steam\steamapps"
New-LibraryFoldersVdf "$WorkDir\multi\Steam" @("$WorkDir\multi\Steam", "$WorkDir\multi\Lib2")
New-AppManifest "$WorkDir\multi\Lib2" "KCD2Mod"
$multiBase = New-ModdingToolsLayout "$WorkDir\multi\Lib2" "KCD2Mod"

# app in the Steam root library itself.
New-Dir "$WorkDir\rootlib\Steam\steamapps"
New-LibraryFoldersVdf "$WorkDir\rootlib\Steam" @("$WorkDir\rootlib\Steam")
New-AppManifest "$WorkDir\rootlib\Steam" "KCD2Mod"
New-ModdingToolsLayout "$WorkDir\rootlib\Steam" "KCD2Mod" | Out-Null

# two real libraries, neither holding the app.
New-Dir "$WorkDir\missing\Steam\steamapps"
New-Dir "$WorkDir\missing\Lib2\steamapps"
New-LibraryFoldersVdf "$WorkDir\missing\Steam" @("$WorkDir\missing\Steam", "$WorkDir\missing\Lib2")

# malformed vdf, app present in the root library: parsing must degrade to
# "just the Steam root" instead of failing outright.
New-Dir "$WorkDir\malformed\Steam\steamapps"
Set-Content -Path "$WorkDir\malformed\Steam\steamapps\libraryfolders.vdf" `
    -Value "{{{ this is not a vdf `" `" `"path`" oops" -Encoding ascii
New-AppManifest "$WorkDir\malformed\Steam" "KCD2Mod"
New-ModdingToolsLayout "$WorkDir\malformed\Steam" "KCD2Mod" | Out-Null

# no vdf at all (very old Steam installs).
New-Dir "$WorkDir\novdf\Steam\steamapps"
New-AppManifest "$WorkDir\novdf\Steam" "KCD2Mod"
New-ModdingToolsLayout "$WorkDir\novdf\Steam" "KCD2Mod" | Out-Null

# manifest names an app whose files are gone.
New-Dir "$WorkDir\ghost\Steam\steamapps"
New-LibraryFoldersVdf "$WorkDir\ghost\Steam" @("$WorkDir\ghost\Steam")
New-AppManifest "$WorkDir\ghost\Steam" "KCD2Mod"

# the discriminator's whole point: a manifest pointing at a retail layout.
New-Dir "$WorkDir\retail\Steam\steamapps"
New-LibraryFoldersVdf "$WorkDir\retail\Steam" @("$WorkDir\retail\Steam")
New-AppManifest "$WorkDir\retail\Steam" "KCD2Mod"
New-RetailLayout "$WorkDir\retail\Steam" "KCD2Mod"

# a library on a drive that is not currently connected.
New-Dir "$WorkDir\offline\Steam\steamapps"
New-LibraryFoldersVdf "$WorkDir\offline\Steam" @("$WorkDir\offline\Steam", "Z:\NoSuchSteamLibrary")

# WO-150 Part 3 -- the four installer cases.
New-Dir "$WorkDir\case1\Steam\steamapps"
New-LibraryFoldersVdf "$WorkDir\case1\Steam" @("$WorkDir\case1\Steam")

foreach ($c in "case2", "case3", "case4") {
    New-Dir "$WorkDir\$c\Steam\steamapps"
    New-LibraryFoldersVdf "$WorkDir\$c\Steam" @("$WorkDir\$c\Steam", "$WorkDir\$c\Lib")
    $game = New-GameLayout "$WorkDir\$c\Lib"
    New-AppManifest "$WorkDir\$c\Lib" "KCD2Mod"
    $mt = New-ModdingToolsLayout "$WorkDir\$c\Lib" "KCD2Mod"
    if ($c -ne "case2") { Link-Workspace $game $mt }
    if ($c -eq "case4") {
        New-Dir "$mt\Mods\kdcmp\Data"
        Set-Content -Path "$mt\Mods\kdcmp\mod.manifest" -Value "x" -Encoding ascii
    }
}

# --- helper (WO-150) -------------------------------------------------------

if (-not $HelperExe) {
    $payloadHelper = Join-Path $root "release\KCDMP\KcdMpSetup.exe"
    $aotHelper = Join-Path $root "dotnet\KcdMp.SetupHost\bin\Release\net8.0\win-x64\publish\KcdMpSetup.exe"
    if (Test-Path $payloadHelper) { $HelperExe = $payloadHelper }
    else {
        if (-not (Test-Path $aotHelper)) {
            Write-Host "Publishing the setup helper (NativeAOT) ..."
            if (-not $env:DOTNET_ROOT) { $env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet-sdk8"; $env:PATH = "$env:DOTNET_ROOT;$env:PATH" }
            $vsInstaller = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
            if (Test-Path $vsInstaller) { $env:PATH = "$vsInstaller;$env:PATH" }
            & dotnet publish (Join-Path $root "dotnet\KcdMp.SetupHost\KcdMp.SetupHost.csproj") -c Release --nologo -v q
            if ($LASTEXITCODE -ne 0) { throw "helper publish failed" }
        }
        $HelperExe = $aotHelper
    }
}
if (-not (Test-Path $HelperExe)) { throw "setup helper not found: $HelperExe" }
Write-Host "Setup helper: $HelperExe"

# --- probe ---------------------------------------------------------------

$iscc = Get-Iscc
$probeIss = Join-Path $root "installer\tests\SteamDetectProbe.iss"
$probeExe = Join-Path $root "installer\tests\SteamDetectProbe.exe"
$probeLog = Join-Path $WorkDir "probe.log"

Write-Host "Compiling $probeIss ..."
& $iscc /Q "/DHelperExe=$HelperExe" $probeIss
if ($LASTEXITCODE -ne 0) { throw "probe compile failed" }

Write-Host "Running probe ..."
Start-Process $probeExe -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/LOG=$probeLog", "/FIXTURES=$WorkDir" -Wait
if (-not (Test-Path $probeLog)) { throw "probe produced no log at $probeLog" }

$results = @{}
foreach ($line in Get-Content $probeLog) {
    if ($line -match 'RESULT\s+(\S+)\s+\|\s*(.*)$') {
        $results[$Matches[1]] = $Matches[2].Trim()
    }
}

function Get-Field($caseName, $field) {
    if (-not $results.ContainsKey($caseName)) { return "<case missing>" }
    foreach ($part in $results[$caseName] -split '\|') {
        $kv = $part.Trim()
        if ($kv -like "$field=*") { return $kv.Substring($field.Length + 1) }
    }
    return "<field missing>"
}

Write-Host ""
Write-Host "Steam detection (through the setup helper)"

Assert-That "the helper ran for every case" (@($results.Keys | Where-Object { $_ -notlike 'checkexe-*' -and (Get-Field $_ ran) -ne "1" }).Count -eq 0) ($results.Keys -join ',')
Assert-That "multi-library: both libraries seen" ((Get-Field multi-library libs) -eq "2") (Get-Field multi-library libs)
Assert-That "multi-library: app found in second library" ((Get-Field multi-library found) -eq "1") $results["multi-library"]
Assert-That "multi-library: exe is the fixture's exe" `
    ((Get-Field multi-library exe) -eq "$multiBase\Bin\Win64ReleaseSteamLTO_DLL\KingdomCome.exe") (Get-Field multi-library exe)
Assert-That "multi-library: install root derived from the exe" ((Get-Field multi-library root) -eq $multiBase) (Get-Field multi-library root)

Assert-That "app in the root library is found" ((Get-Field app-in-root-library found) -eq "1") $results["app-in-root-library"]
Assert-That "no app anywhere: not found" ((Get-Field missing-app found) -eq "0") $results["missing-app"]
Assert-That "missing-app still enumerated both libraries" ((Get-Field missing-app libs) -eq "2") (Get-Field missing-app libs)

Assert-That "malformed vdf degrades to the Steam root" ((Get-Field malformed-vdf libs) -eq "1") (Get-Field malformed-vdf libs)
Assert-That "malformed vdf still finds a root-library app" ((Get-Field malformed-vdf found) -eq "1") $results["malformed-vdf"]
Assert-That "no vdf at all still finds a root-library app" ((Get-Field no-vdf-at-all found) -eq "1") $results["no-vdf-at-all"]

Assert-That "manifest without files is registered but not found" `
    (((Get-Field manifest-but-no-files found) -eq "0") -and ((Get-Field manifest-but-no-files registered) -eq "1")) $results["manifest-but-no-files"]
Assert-That "retail layout is rejected by the discriminator" ((Get-Field retail-not-modding-tools found) -eq "0") $results["retail-not-modding-tools"]
Assert-That "disconnected library is skipped" ((Get-Field offline-library libs) -eq "1") (Get-Field offline-library libs)
Assert-That "a Steam root that does not exist means no Steam" ((Get-Field no-steam steam) -eq "0") $results["no-steam"]

Write-Host ""
Write-Host "The four installer cases (WO-150 Part 3)"
Assert-That "case 1, nothing installed: Setup holds the mod back" `
    (((Get-Field case1-nothing found) -eq "0") -and ((Get-Field case1-nothing place_mod) -eq "0")) $results["case1-nothing"]
Assert-That "case 2, Modding Tools installed, not linked: installed is not set up" `
    (((Get-Field case2-mt-unlinked found) -eq "1") -and ((Get-Field case2-mt-unlinked workspace) -eq "unlinked") -and ((Get-Field case2-mt-unlinked place_mod) -eq "0")) $results["case2-mt-unlinked"]
Assert-That "case 3, linked, no mod: Setup places the mod" `
    (((Get-Field case3-linked-no-mod workspace) -eq "linked") -and ((Get-Field case3-linked-no-mod place_mod) -eq "1")) $results["case3-linked-no-mod"]
Assert-That "case 4, everything in place: Setup installs as today" `
    (((Get-Field case4-everything workspace) -eq "linked") -and ((Get-Field case4-everything place_mod) -eq "1")) $results["case4-everything"]

Write-Host ""
Write-Host "Browse"
Assert-That "an explicit Modding Tools exe is used" ((Get-Field browse-explicit found) -eq "1") $results["browse-explicit"]
Assert-That "check-exe accepts the Modding Tools build" ((Get-Field checkexe-mt ok) -eq "1") $results["checkexe-mt"]
Assert-That "check-exe refuses retail" ((Get-Field checkexe-retail ok) -eq "0") $results["checkexe-retail"]

Write-Host ""
Write-Host "This machine (read only)"
$realExe = Get-Field real-machine exe
Assert-That "Steam located from the registry" ((Get-Field real-machine steam) -eq "1") $results["real-machine"]
Assert-That "Modding Tools found on this machine" ((Get-Field real-machine found) -eq "1") $results["real-machine"]
Assert-That "detected exe exists" ($realExe -ne "" -and (Test-Path $realExe)) $realExe
if ($realExe -ne "" -and (Test-Path $realExe)) {
    $dir = Split-Path $realExe -Parent
    Assert-That "detected exe passes the discriminator" `
        ((Test-Path "$dir\Framework.dll") -and (Test-Path "$dir\CrySystem.dll")) $dir
}
Write-Host ("  INFO  this machine: workspace={0} place_mod={1}" -f (Get-Field real-machine workspace), (Get-Field real-machine place_mod))

Write-Host ""
Write-Host ("{0}/{1} passed" -f $pass, ($pass + $fail))
if ($fail -gt 0) { exit 1 }
