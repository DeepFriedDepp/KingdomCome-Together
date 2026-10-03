# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-154 Phase 9: an upgrade keeps every setting the player has, byte for byte.

.DESCRIPTION
    For each previous installer (default: 0.43.0, and 0.44.0 -- the first WO-150 build): a throwaway install
    against a fixture Steam tree (nothing touches the real game or a real install), then the player's files are
    written by hand the way a player's machine has them -- settings.json with a custom game path, changed relay and
    network choices, a voice choice from the old default, a Steam code, unknown keys (one nested), its own key order,
    four-space indents and CRLF; servers; favourites; the agent's file with a player name -- then the Setup under test
    installs over it. Every one of those files must be byte-identical afterwards (Setup adds nothing to them).

    -Launcher also starts the upgraded launcher once from the install folder (its working directory, where it reads
    settings.json), lets it run -LauncherSeconds, closes it, and compares again: a launcher that only reads must leave
    every byte alone; anything it wrote is printed key by key (the rule: fill a missing key only, never change, reset,
    reorder or drop one). The launcher runs its read-only setup checklist against the machine's real Steam (it changes
    nothing when everything is in place) and writes its own daily log to %AppData%\KCDMP_Launcher\app<date>.log, its
    normal place: move that file aside after a run on a player's machine. A launcher that is no longer running at the
    end of the wait fails the run (the comparison would prove nothing).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\wo154\Test-SettingsUpgrade.ps1 -SetupExe release\KingdomComeTogether-Setup-0.45.0.exe
#>
[CmdletBinding()]
param(
    [string] $SetupExe,
    [string[]] $PreviousSetupExe,
    [string] $WorkDir = (Join-Path $env:TEMP "kcdmp-settings-upgrade"),
    [switch] $Launcher,
    [int] $LauncherSeconds = 25
)

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$version = (Get-Content (Join-Path $root 'VERSION') -TotalCount 1).Trim()
if (-not $SetupExe) { $SetupExe = Join-Path $root "release\KingdomComeTogether-Setup-$version.exe" }
if (-not (Test-Path $SetupExe)) { throw "Setup not found: $SetupExe" }
if (-not $PreviousSetupExe) {
    $PreviousSetupExe = @(
        (Join-Path $root 'release\Old-Installers\KingdomComeTogether-Setup-0.43.0.exe'),
        (Join-Path $root 'release\KingdomComeTogether-Setup-0.44.0.exe'))
}
foreach ($p in $PreviousSetupExe) { if (-not (Test-Path $p)) { throw "previous Setup not found: $p" } }
$regKey = 'HKCU:\Software\KCDMP'
if (Test-Path $regKey) { throw "An install is registered at $regKey -- this suite installs and uninstalls throwaway copies only." }

$pass = 0; $fail = 0
function Assert-That($name, $condition, $detail) {
    if ($condition) { $script:pass++; Write-Host ("    PASS  {0}" -f $name) }
    else { $script:fail++; Write-Host ("    FAIL  {0}  --  {1}" -f $name, $detail) -ForegroundColor Red }
}

# The fixture Steam tree of tools\Test-InstallerUpgrade.ps1 (a Modding Tools game Setup accepts, a retail game, a
# linked workspace), so Setup places the mod into the fixture and nowhere else.
function New-SteamFixture($dir) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    $steam = Join-Path $dir 'Steam'
    $game = Join-Path $steam 'steamapps\common\KCD2Mod'
    $bin  = Join-Path $game 'Bin\Win64ReleaseSteamLTO_DLL'
    New-Item -ItemType Directory -Force -Path (Join-Path $steam 'steamapps'), (Join-Path $game 'Data'), (Join-Path $game 'Engine'), $bin | Out-Null
    foreach ($f in @('KingdomCome.exe', 'Framework.dll', 'CrySystem.dll')) { Set-Content -Path (Join-Path $bin $f) -Value 'fixture' -Encoding ASCII }
    Set-Content -Path (Join-Path $steam 'steamapps\appmanifest_2429020.acf') -Encoding ASCII -Value "`"AppState`"`r`n{`r`n`t`"appid`"`t`t`"2429020`"`r`n`t`"name`"`t`t`"Kingdom Come: Deliverance II Modding tools`"`r`n`t`"installdir`"`t`t`"KCD2Mod`"`r`n}"
    $retail = Join-Path $steam 'steamapps\common\KingdomComeDeliverance2'
    New-Item -ItemType Directory -Force -Path (Join-Path $retail 'Data') | Out-Null
    Set-Content -Path (Join-Path $retail 'Data\Tables.pak') -Value 'fixture pak' -Encoding ASCII
    Set-Content -Path (Join-Path $steam 'steamapps\appmanifest_1771300.acf') -Encoding ASCII -Value "`"AppState`"`r`n{`r`n`t`"appid`"`t`t`"1771300`"`r`n`t`"StateFlags`"`t`t`"4`"`r`n`t`"installdir`"`t`t`"KingdomComeDeliverance2`"`r`n}"
    New-Item -ItemType HardLink -Path (Join-Path $game 'Data\Tables.pak') -Target (Join-Path $retail 'Data\Tables.pak') | Out-Null
    Set-Content -Path (Join-Path $steam 'steamapps\libraryfolders.vdf') -Encoding ASCII -Value ("`"libraryfolders`"`r`n{`r`n`t`"0`"`r`n`t{`r`n`t`t`"path`"`t`t`"" + $steam.Replace('\', '\\') + "`"`r`n`t}`r`n}")
    return $steam
}

function Invoke-Setup($exe, $appDir, $steam, $log) {
    $p = Start-Process -FilePath $exe -Wait -PassThru -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$appDir", "/STEAMROOT=$steam", "/LOG=$log")
    return $p.ExitCode
}

function Remove-Install($appDir) {
    $unins = Join-Path $appDir 'unins000.exe'
    if (Test-Path $unins) { Start-Process $unins -Wait -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES' | Out-Null; Start-Sleep -Milliseconds 1500 }
    if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force -ErrorAction SilentlyContinue }
    if (Test-Path $regKey) { Remove-Item $regKey -Recurse -Force }
}

function Write-Bytes($path, [string]$text) { [System.IO.File]::WriteAllBytes($path, [System.Text.UTF8Encoding]::new($false).GetBytes($text)) }

# What a player's machine has, written by hand (CRLF, four-space indents, the player's own key order, unknown keys).
function Write-PlayerFiles($appDir) {
    $nl = "`r`n"
    $settings = '{' + $nl +
        '    "Language": "de",' + $nl +
        '    "GamePath": "E:\\Games\\Steam Library\\steamapps\\common\\KCD2Mod\\Bin\\Win64ReleaseSteamLTO_DLL\\KingdomCome.exe",' + $nl +
        '    "MyOwnNote": "kept by the player",' + $nl +
        '    "HostPort": 7790,' + $nl +
        '    "MasterServerUrl": "http://192.0.2.10:5100",' + $nl +
        '    "VoiceChatEnabled": true,' + $nl +
        '    "HostAllowSteam": false,' + $nl +
        '    "LastSteamCode": "QX7K-2M9P",' + $nl +
        '    "InjectDelaySeconds": 35,' + $nl +
        '    "SomethingFromTheFuture": { "list": [ 1, 2, { "deep": "yes" } ], "flag": false },' + $nl +
        '    "ServerInfoPort": 5280' + $nl +
        '}' + $nl
    Write-Bytes (Join-Path $appDir 'settings.json') $settings
    Write-Bytes (Join-Path $appDir 'custom_servers.json') ('[' + $nl +
        '  { "Name": "Home relay", "Ip": "192.0.2.20", "Port": 7778, "InfoPort": 5273 },' + $nl +
        '  { "Name": "Second house", "Ip": "relay.example.net", "Port": 7790, "InfoPort": 5280, "Extra": "unknown field" }' + $nl + ']' + $nl)
    Write-Bytes (Join-Path $appDir 'favorites.json') ('["relay.example.net:7790"]' + $nl)
    Write-Bytes (Join-Path $appDir 'kcdmp-client.json') ('{' + $nl + '  "PlayerName": "Player Two",' + $nl + '  "ServerHost": "relay.example.net",' + $nl + '  "ServerPort": 7790,' + $nl + '  "Custom": 1' + $nl + '}' + $nl)
}
$playerFiles = @('settings.json', 'custom_servers.json', 'favorites.json', 'kcdmp-client.json')
function Get-Hashes($appDir) {
    $h = @{}
    foreach ($f in $playerFiles) { $p = Join-Path $appDir $f; $h[$f] = if (Test-Path $p) { (Get-FileHash $p -Algorithm SHA256).Hash } else { '(missing)' } }
    return $h
}

if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$steam = New-SteamFixture (Join-Path $WorkDir 'fixture')
Write-Host "Setup under test : $SetupExe"
foreach ($prev in $PreviousSetupExe) {
    $tag = [IO.Path]::GetFileNameWithoutExtension($prev)
    $appDir = Join-Path $WorkDir "app-$tag"
    Write-Host ''
    Write-Host "=== upgrade from $tag"
    $c = Invoke-Setup $prev $appDir $steam (Join-Path $WorkDir "$tag-install.log")
    Assert-That "$tag installs (exit 0)" ($c -eq 0) "exit $c"
    $seeded = Join-Path $appDir 'settings.json'
    if (Test-Path $seeded) { Write-Host ("    (the previous Setup seeded settings.json: {0} bytes -- replaced by the player's own)" -f (Get-Item $seeded).Length) }
    Write-PlayerFiles $appDir
    $before = Get-Hashes $appDir
    $c = Invoke-Setup $SetupExe $appDir $steam (Join-Path $WorkDir "$tag-upgrade.log")
    Assert-That "$tag -> $version upgrade (exit 0)" ($c -eq 0) "exit $c"
    $after = Get-Hashes $appDir
    foreach ($f in $playerFiles) { Assert-That "$tag upgrade: $f byte-identical" ($before[$f] -eq $after[$f]) "$($before[$f]) -> $($after[$f])" }
    $verdict = Join-Path $appDir 'install-verify.txt'
    Assert-That "$tag upgrade: Setup's own verdict is PASS" ((Test-Path $verdict) -and ((Get-Content $verdict -Raw) -match 'PASS')) ((Get-Content $verdict -Raw -ErrorAction SilentlyContinue) -replace '\s+', ' ')
    if ($Launcher) {
        $exe = Join-Path $appDir 'KCDMP_launcher.exe'
        if (-not (Test-Path $exe)) { Assert-That "${tag}: the launcher exists" $false $exe }
        else {
            $lp = Start-Process -FilePath $exe -WorkingDirectory $appDir -PassThru -WindowStyle Minimized
            Start-Sleep -Seconds $LauncherSeconds
            Assert-That "${tag}: the launcher is still running after ${LauncherSeconds} s" (-not $lp.HasExited) "it exited early (code $($lp.ExitCode)) -- the comparison below would prove nothing"
            Get-Process -Id $lp.Id -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null }
            Start-Sleep -Seconds 3
            Get-Process -Id $lp.Id -ErrorAction SilentlyContinue | Stop-Process -Force
            Get-Process KcdMpClient, KcdMpServer -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$appDir*" } | Stop-Process -Force
            $afterL = Get-Hashes $appDir
            foreach ($f in $playerFiles) {
                $same = $after[$f] -eq $afterL[$f]
                Assert-That "$tag after the launcher ran ${LauncherSeconds} s: $f byte-identical" $same "$($after[$f]) -> $($afterL[$f])"
                if (-not $same -and $f -eq 'settings.json') { Write-Host '      the launcher wrote:'; Get-Content (Join-Path $appDir $f) | ForEach-Object { Write-Host "        $_" } }
            }
        }
    }
    Remove-Install $appDir
}
Write-Host ''
Write-Host ("RESULT: {0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 } else { exit 0 }
