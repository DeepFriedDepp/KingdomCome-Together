# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    Assemble a self-contained release folder a friend can unzip and run --
    no .NET runtime install, no manual DLL copying.

.DESCRIPTION
    Publishes KCDMP_launcher, KcdMpClient, KcdMpServer and KcdMpMasterServer
    as self-contained win-x64 (via each project's FolderProfile.pubxml -- see
    docs/WO-7-progress.md for why that's set on the profile and not the
    .csproj), builds the native plugin if not already built, and
    copies everything the launcher's AppSettings defaults expect to find
    beside it (KCDMP.dll, KcdMpClient.exe,
    KcdMpServer.exe + their appsettings) into one folder.

    KcdMpMasterServer.exe goes into its own MasterServer\ subfolder instead
    of being flat-merged like the rest (WO-35). Confirmed live: flat-merging
    it let a later Copy-Item -- even a partial one republishing only the
    launcher -- silently overwrite one of its dependency DLLs with an
    incompatible version from another project's own bundle, crashing it with
    "Could not load Microsoft.Extensions.Configuration.Abstractions" on next
    launch. Isolating it removes the hazard rather than requiring every
    future partial update to remember not to trigger it.

    The native DLL statically links its C++ runtime
    (CMAKE_MSVC_RUNTIME_LIBRARY = MultiThreaded in native/CMakeLists.txt), so
    there is no VC++ redistributable to bundle or check for.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Publish-Release.ps1
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\release\KCDMP")
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

if (-not $env:DOTNET_ROOT) {
    $env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet-sdk8"
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutDir | Out-Null

# Publishes one project and leaves the output path in $script:PublishDir.
#
# The path comes back through a variable rather than as a return value on
# purpose. Everything a PowerShell function writes to the success stream is
# part of its return value, so logging with Write-Output made the caller
# receive an array of log lines with the path last ("Cannot find drive
# 'Publishing C'"); switching the logging to Write-Host fixed that but sent
# the MSBuild diagnostics somewhere a redirected or background run cannot
# capture, which is exactly when they are needed. This way logs stay on the
# success stream where any caller sees them, and the path is unambiguous.
function Publish-Project($csproj, $publishSubdir) {
    Write-Output "Publishing $csproj ..."
    & dotnet publish $csproj -c Release -p:PublishProfile=FolderProfile 2>&1 | ForEach-Object { Write-Output $_ }
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $csproj" }
    $dir = Join-Path (Split-Path $csproj -Parent) $publishSubdir
    if (-not (Test-Path $dir)) { throw "expected publish output not found: $dir" }
    $script:PublishDir = $dir
}

# --- Launcher (root of the release: everything else is copied beside it,
#     matching AppSettings' relative-path defaults for DllPath/AgentPath/
#     RelayPath) ---
Publish-Project (Join-Path $root "KCDMP_launcher\KCDMP_launcher.csproj") "bin\Release\net8.0-windows\publish"
$launcherPublish = $script:PublishDir
Copy-Item "$launcherPublish\*" $OutDir -Recurse -Force

# --- Agent ---
Publish-Project (Join-Path $root "dotnet\KcdMp.Client\KcdMp.Client.csproj") "bin\Release\net8.0\publish"
$clientPublish = $script:PublishDir
Copy-Item "$clientPublish\*" $OutDir -Recurse -Force

# --- Relay ---
Publish-Project (Join-Path $root "dotnet\KcdMp.Server\KcdMp.Server.csproj") "bin\Release\net8.0\publish"
$serverPublish = $script:PublishDir
Copy-Item "$serverPublish\*" $OutDir -Recurse -Force

# --- Master server (WO-35): auto-started by the launcher itself, not just
#     the relay -- see AppSettings.MasterServerPath / Home.razor.cs's
#     EnsureLocalMasterServerAsync. Must be present for the default
#     MasterServerUrl (a loopback address) to ever have anything answering it.
#     Its own subfolder, not flat-merged -- see the .SYNOPSIS note above for
#     why; AppModels.cs's MasterServerPath default matches this path. ---
Publish-Project (Join-Path $root "dotnet\KcdMp.MasterServer\KcdMp.MasterServer.csproj") "bin\Release\net8.0\publish"
$masterServerPublish = $script:PublishDir
$masterServerOutDir = Join-Path $OutDir "MasterServer"
New-Item -ItemType Directory -Path $masterServerOutDir -Force | Out-Null
Copy-Item "$masterServerPublish\*" $masterServerOutDir -Recurse -Force

# --- WO-150: KcdMpSetup.exe, the shared setup code as a NativeAOT exe. Setup
#     runs it before anything is installed (no .NET runtime yet) and the
#     launcher runs it for the one UAC step. Only the exe ships: AOT needs no
#     runtime files beside it, and the pdb is not shipped. ILCompiler finds
#     the MSVC linker through vswhere, which is not on PATH by default. ---
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
if ((Test-Path $vsInstaller) -and ($env:PATH -notlike "*$vsInstaller*")) { $env:PATH = "$vsInstaller;$env:PATH" }
$setupHost = Join-Path $root "dotnet\KcdMp.SetupHost\KcdMp.SetupHost.csproj"
Write-Output "Publishing $setupHost (NativeAOT) ..."
& dotnet publish $setupHost -c Release 2>&1 | ForEach-Object { Write-Output $_ }
if ($LASTEXITCODE -ne 0) { throw "publish failed: $setupHost" }
$setupExe = Join-Path $root "dotnet\KcdMp.SetupHost\bin\Release\net8.0\win-x64\publish\KcdMpSetup.exe"
if (-not (Test-Path $setupExe)) { throw "expected publish output not found: $setupExe" }
Copy-Item $setupExe $OutDir -Force

# --- Native plugin ---
# WO-157: no injector exe in the payload: the launcher loads KCDMP.dll itself
# (dotnet\KcdMp.Setup\GameInjector.cs). native\KCDMP_LauncherInjector stays a
# developer tool for probes and test harnesses; it never ships.
# WO-110 R10 (docs/WO-109-audit.md s5.3): ALWAYS rebuilt, not only when
# missing. A stale KCDMP.dll beside a fresh agent used to be prevented by the
# fresh-clone discipline alone -- there is no DLL/agent version handshake to
# catch it -- and a working-tree publish after a native edit shipped whatever
# native\build already held. Build-Native.ps1 parks a DLL that a running game
# still has loaded, so this is safe with the game up.
$nativeDll = Join-Path $root "native\build\KCDMP\KCDMP.dll"
Write-Output "Building the native plugin (always, WO-110 R10)..."
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "native\Build-Native.ps1")
if ($LASTEXITCODE -ne 0) { throw "native build failed" }
if (-not (Test-Path $nativeDll)) { throw "native build produced no artifact at $nativeDll" }
Copy-Item $nativeDll $OutDir -Force
# An earlier publish into this folder left the injector: it must not ride along.
Remove-Item (Join-Path $OutDir "KCDMP_LauncherInjector.exe") -Force -ErrorAction SilentlyContinue

Write-Output "`nRelease assembled at: $OutDir"
Write-Output "Contents:"
Get-ChildItem $OutDir -File | Select-Object Name, @{N='KB';E={[math]::Round($_.Length/1KB,1)}} | Format-Table

