# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-150 Part 3: the installer's four cases, end to end, on fake Steam
    libraries -- with a Setup compiled from the real installer\KCDMP.iss.

.DESCRIPTION
    tools\Test-InstallerDetect.ps1 proves the detection Setup asks (helper +
    wrapper). This proves what Setup then DOES with the answer: the mod placed
    or held back, the staged copy, the verdict file, settings.json, the
    registry marker -- for each of:

      1  nothing installed
      2  the Modding Tools installed, workspace not linked
      3  linked, no mod
      4  everything in place (the maintainer's and today's testers' machines)

    It must be safe on a machine with a real install, so the Setup under test
    is the real script with exactly five things changed in a scratch copy
    (Get-PatchedIss below): its AppId and HKCU key (so it can never touch the
    real install's registration), no Start-menu/desktop shortcuts and no
    old-shortcut cleanup, and no process gate (the real gate kills a running
    launcher, agent and relay in silent mode -- never the maintainer's). Every
    file it writes is under -WorkDir. The unpatched script is compiled too, as
    the syntax gate for the shipping .iss.

    The payload is a small fake (a launcher stub, the real KcdMpSetup.exe, the
    real mod files staged as Build-Installer.ps1 stages them, a v2 manifest
    from tools\New-InstallManifest.ps1), so no publish is needed.

.PARAMETER WorkDir
    Scratch folder (required: nothing is written anywhere else).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkDir,
    [string] $HelperExe
)

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pass = 0; $fail = 0
function Assert-That($name, $condition, $detail) {
    if ($condition) { $script:pass++; Write-Host ("  PASS  {0}" -f $name) }
    else { $script:fail++; Write-Host ("  FAIL  {0}  --  {1}" -f $name, $detail) -ForegroundColor Red }
}
function New-Dir($p) { New-Item -ItemType Directory -Force -Path $p | Out-Null }

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found" }
if (-not $HelperExe) { $HelperExe = Join-Path $root "dotnet\KcdMp.SetupHost\bin\Release\net8.0\win-x64\publish\KcdMpSetup.exe" }
if (-not (Test-Path $HelperExe)) { throw "setup helper not found: $HelperExe (dotnet publish dotnet\KcdMp.SetupHost -c Release)" }

if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
$mirror = Join-Path $WorkDir "mirror"

# --- a mirror of the repo layout the .iss expects (..\release\KCDMP, ..\kdcmp, ..\LICENSE, ..\KCDMP_launcher\app.ico)
New-Dir "$mirror\installer"; New-Dir "$mirror\release\KCDMP\mod\kdcmp\Data"; New-Dir "$mirror\kdcmp\Data"; New-Dir "$mirror\KCDMP_launcher"
Copy-Item "$root\installer\SteamDetect.iss" "$mirror\installer\"
Copy-Item "$root\LICENSE" "$mirror\"
Copy-Item "$root\KCDMP_launcher\app.ico" "$mirror\KCDMP_launcher\"
Copy-Item "$root\kdcmp\mod.manifest" "$mirror\kdcmp\"
Copy-Item "$root\kdcmp\Data\kdcmp.pak" "$mirror\kdcmp\Data\"
$payload = "$mirror\release\KCDMP"
Set-Content "$payload\KCDMP_launcher.exe" "launcher stub" -Encoding ascii
Copy-Item $HelperExe "$payload\KcdMpSetup.exe"
Copy-Item "$root\kdcmp\mod.manifest" "$payload\mod\kdcmp\"
Copy-Item "$root\kdcmp\Data\kdcmp.pak" "$payload\mod\kdcmp\Data\"
& (Join-Path $root "tools\New-InstallManifest.ps1") -AppDir $payload `
    -ModFiles @{ "mod.manifest" = "$mirror\kdcmp\mod.manifest"; "Data\kdcmp.pak" = "$mirror\kdcmp\Data\kdcmp.pak" } `
    -OutFile "$payload\install-manifest.txt" | Out-Null

# --- 1. the shipping script compiles
$real = Get-Content "$root\installer\KCDMP.iss" -Raw
Set-Content "$mirror\installer\KCDMP.iss" $real -Encoding UTF8 -NoNewline
& $iscc /Q "/DAppVersion=0.0.150" "/O$WorkDir\out-real" "$mirror\installer\KCDMP.iss"
Assert-That "the shipping installer\KCDMP.iss compiles" ($LASTEXITCODE -eq 0) "ISCC exit $LASTEXITCODE"

# --- 2. the patched copy, for running
function Get-PatchedIss([string] $text) {
    $edits = @(
        @('AppId={{88C5B9F1-0E71-4D60-9418-5575D5684F95}', 'AppId={{5A0E1C50-0150-4150-9150-0000000F0150}'),
        @('Software\KCDMP', 'Software\KCDMP-WO150-TEST'),
        @('DefaultGroupName={#ShortcutName}', 'DefaultGroupName=KCDMP WO150 test'),
        @('ChangesAssociations=yes', 'ChangesAssociations=no'),
        @(('  Blocker := FirstInstallBlocker();' + "`n" + '  while Blocker'), ('  Blocker := '''';' + "`n" + '  while Blocker')),
        @('  while AnyOfOursRunning() do', '  while False do')
    )
    foreach ($e in $edits) {
        if (-not $text.Contains($e[0])) { throw "patch anchor not found: $($e[0])" }
        $text = $text.Replace($e[0], $e[1])
    }
    # no shortcuts and no old-shortcut cleanup: drop the [Icons] and [InstallDelete] entries
    $text = ($text -split "`n" | ForEach-Object {
        if ($_ -match '^Name: "\{(group|autodesktop)\}' -or $_ -match '^Type: (files|dirifempty); Name: "\{(autoprograms|autodesktop)\}') { "; (WO-150 test) $_" } else { $_ }
    }) -join "`n"
    return $text
}
Set-Content "$mirror\installer\KCDMP-test.iss" (Get-PatchedIss ($real -replace "`r`n", "`n")) -Encoding UTF8 -NoNewline
& $iscc /Q "/DAppVersion=0.0.150" "/O$WorkDir\out-test" "/FSetup-wo150-test" "$mirror\installer\KCDMP-test.iss"
if ($LASTEXITCODE -ne 0) { throw "the test copy did not compile" }
$setup = "$WorkDir\out-test\Setup-wo150-test.exe"

# --- fixtures
function New-Library($steam, $lib) {
    New-Dir "$steam\steamapps"; New-Dir "$lib\steamapps"
    $e = { param($p) $p -replace '\\', '\\' }
    Set-Content "$steam\steamapps\libraryfolders.vdf" -Encoding ascii -Value ("`"libraryfolders`"`n{`n`t`"0`"`n`t{`n`t`t`"path`"`t`t`"" + (& $e $steam) + "`"`n`t}`n`t`"1`"`n`t{`n`t`t`"path`"`t`t`"" + (& $e $lib) + "`"`n`t}`n}`n")
}
function New-Manifest($lib, $id, $dir) {
    Set-Content "$lib\steamapps\appmanifest_$id.acf" -Encoding ascii -Value "`"AppState`"`n{`n`t`"appid`"`t`t`"$id`"`n`t`"StateFlags`"`t`t`"4`"`n`t`"installdir`"`t`t`"$dir`"`n}`n"
}
function New-Case($name, [switch] $Game, [switch] $Mt, [switch] $Link) {
    $steam = "$WorkDir\$name\Steam"; $lib = "$WorkDir\$name\Lib"
    New-Library $steam $lib
    $g = "$lib\steamapps\common\KingdomComeDeliverance2"; $m = "$lib\steamapps\common\KCD2Mod"
    if ($Game) {
        New-Manifest $lib 1771300 "KingdomComeDeliverance2"
        foreach ($rel in "Data\Tables.pak", "Localization\English_xml.pak", "Data\Levels\klaster\level.pak") {
            New-Dir (Split-Path "$g\$rel" -Parent); Set-Content "$g\$rel" "pak $rel" -Encoding ascii
        }
    }
    if ($Mt) {
        New-Manifest $lib 2429020 "KCD2Mod"
        New-Dir "$m\Bin\Win64ReleaseSteamLTO_DLL"; New-Dir "$m\Engine"
        foreach ($f in "KingdomCome.exe", "Framework.dll", "CrySystem.dll") { Set-Content "$m\Bin\Win64ReleaseSteamLTO_DLL\$f" "" -Encoding ascii }
    }
    if ($Link) {
        foreach ($f in Get-ChildItem $g -Recurse -Filter *.pak -File) {
            $dst = Join-Path $m $f.FullName.Substring($g.Length + 1)
            New-Dir (Split-Path $dst -Parent); New-Item -ItemType HardLink -Path $dst -Target $f.FullName | Out-Null
        }
    }
    return [pscustomobject]@{ Steam = $steam; Mt = $m; App = "$WorkDir\$name\App" }
}

$regKey = 'HKCU:\Software\KCDMP-WO150-TEST'
function Invoke-Case($label, $c, [bool] $expectPlaced, [switch] $PreplaceMod) {
    Write-Host ""
    Write-Host $label
    if ($PreplaceMod) {
        # case 4: a previous install of ours already placed the mod (the registry marker says so)
        New-Dir "$($c.Mt)\Mods\kdcmp\Data"
        Copy-Item "$mirror\kdcmp\mod.manifest" "$($c.Mt)\Mods\kdcmp\"; Copy-Item "$mirror\kdcmp\Data\kdcmp.pak" "$($c.Mt)\Mods\kdcmp\Data\"
        New-Item -Path $regKey -Force | Out-Null
        Set-ItemProperty -Path $regKey -Name ModsPath -Value "$($c.Mt)\Mods\kdcmp"
    }
    $p = Start-Process $setup -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=$($c.App)", "/STEAMROOT=$($c.Steam)", "/LOG=$WorkDir\$label.log" -Wait -PassThru
    Assert-That "Setup exits 0 (no dead end)" ($p.ExitCode -eq 0) "exit $($p.ExitCode)"
    $verdict = @(Get-Content "$($c.App)\install-verify.txt" -ErrorAction SilentlyContinue)
    Assert-That "install-verify.txt says PASS" ($verdict.Count -gt 0 -and $verdict[0].StartsWith('PASS')) ($verdict -join ' / ')
    Assert-That "the launcher and the helper are installed" ((Test-Path "$($c.App)\KCDMP_launcher.exe") -and (Test-Path "$($c.App)\KcdMpSetup.exe")) $c.App
    Assert-That "the staged mod is in the app folder" (Test-Path "$($c.App)\mod\kdcmp\Data\kdcmp.pak") ""
    $placed = Test-Path "$($c.Mt)\Mods\kdcmp\Data\kdcmp.pak"
    if ($expectPlaced) {
        Assert-That "the mod is placed in the Modding Tools" $placed "$($c.Mt)\Mods\kdcmp"
        Assert-That "verdict: mod placed by Setup" ($verdict[-1] -eq 'mod placed by Setup') $verdict[-1]
        Assert-That "the uninstaller's ModsPath marker is written" ((Get-ItemProperty $regKey -ErrorAction SilentlyContinue).ModsPath -eq "$($c.Mt)\Mods\kdcmp") ""
    } else {
        Assert-That "nothing is put in the Modding Tools' Mods folder" (-not $placed) "$($c.Mt)\Mods\kdcmp"
        Assert-That "verdict: mod held back for the launcher" ($verdict[-1] -like 'mod held back*') $verdict[-1]
        Assert-That "no ModsPath marker for a mod Setup did not place" (-not (Get-ItemProperty $regKey -ErrorAction SilentlyContinue).ModsPath) ""
    }
    $settings = "$($c.App)\settings.json"
    if (Test-Path "$($c.Mt)\Bin\Win64ReleaseSteamLTO_DLL\KingdomCome.exe") {
        Assert-That "settings.json is seeded with the Modding Tools exe" ((Test-Path $settings) -and ((Get-Content $settings -Raw) -match 'KCD2Mod')) $settings
    } else {
        Assert-That "no settings.json is seeded when nothing was found" (-not (Test-Path $settings)) $settings
    }
    $log = Get-Content "$WorkDir\$label.log" -Raw
    Assert-That "Setup's log records the shared detection's verdict" ($log -match 'detect: steam=1 .* place_mod=' + ([int]$expectPlaced)) ""
    # leave nothing behind for the next case
    $un = Start-Process "$($c.App)\unins000.exe" -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Wait -PassThru
    Start-Sleep -Milliseconds 800
    Assert-That "the test install uninstalls cleanly" (($un.ExitCode -eq 0) -and -not (Test-Path $regKey)) "exit $($un.ExitCode)"
}

if (Test-Path $regKey) { Remove-Item $regKey -Recurse -Force }
Invoke-Case "case1-nothing-installed"       (New-Case case1)                     $false
Invoke-Case "case2-mt-installed-not-linked" (New-Case case2 -Game -Mt)           $false
Invoke-Case "case3-linked-no-mod"           (New-Case case3 -Game -Mt -Link)     $true
Invoke-Case "case4-everything-in-place"     (New-Case case4 -Game -Mt -Link)     $true -PreplaceMod

Write-Host ""
Write-Host ("{0}/{1} passed" -f $pass, ($pass + $fail))
if ($fail -gt 0) { exit 1 }
