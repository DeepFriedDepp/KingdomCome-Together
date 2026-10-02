# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-150 Stage B: Warhorse's WorkspaceSetup.exe, run once against an install
    exactly the way the launcher runs it, with the evidence the WO asks for.

.DESCRIPTION
    1. Lists every entry under <MT>\Data and <MT>\Localization (path, size,
       modified and created time, attributes, link target, link count).
    2. Starts sampling the foreground window every 100 ms, and looks for any
       visible top-level window owned by a WorkspaceSetup process.
    3. Runs "KcdMpSetup.exe tool --mt <MT> --delete-answer N": the launcher's
       own runner -- no window, standard input/output through pipes. N keeps
       every existing file, so even a tool that could be driven would change
       nothing in a workspace that is already complete.
    4. Lists again and compares. The two listings must be identical.

    Reads and starts processes only; writes nothing but -OutDir. Nothing here
    types, clicks, or brings a window to the front.

.PARAMETER MtRoot
    The Modding Tools root (default: what the shared detection finds).
.PARAMETER ToolExe
    Override the tool (the Stage A rehearsal points this at the fake tool).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $OutDir,
    [string] $MtRoot,
    [string] $ToolExe,
    [string] $HelperExe
)

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $HelperExe) { $HelperExe = Join-Path $root "dotnet\KcdMp.SetupHost\bin\Release\net8.0\win-x64\publish\KcdMpSetup.exe" }
if (-not (Test-Path $HelperExe)) { throw "setup helper not found: $HelperExe" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

if (-not $MtRoot) {
    Start-Process $HelperExe -ArgumentList 'detect', '--out', "`"$OutDir\detect.txt`"" -Wait
    $MtRoot = ((Get-Content "$OutDir\detect.txt") | Where-Object { $_ -like 'mt_root=*' }) -replace '^mt_root=', ''
    if (-not $MtRoot) { throw "no Modding Tools found" }
}

Add-Type -Namespace Wo150 -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
public delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string n, uint a, uint s, IntPtr sa, uint d, uint f, IntPtr t);
[StructLayout(LayoutKind.Sequential)] public struct BHFI { public uint a, c1, c2, a1, a2, w1, w2, vol, sh, sl, links, ih, il; }
[DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle h, out BHFI i);
public static uint Links(string p) {
  using (var h = CreateFileW(p, 0x80, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)) {   // no-follow: the entry itself
    BHFI i; return (!h.IsInvalid && GetFileInformationByHandle(h, out i)) ? i.links : 0; } }
public static System.Collections.Generic.List<string> VisibleWindowsOf(uint pid) {
  var list = new System.Collections.Generic.List<string>();
  EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) list.Add(h.ToString()); return true; }, IntPtr.Zero);
  return list; }
'@

function Get-Listing {
    $mtFull = (Get-Item $MtRoot).FullName
    foreach ($sub in 'Data', 'Localization') {
        if (-not (Test-Path "$mtFull\$sub")) { continue }
        Get-ChildItem "$mtFull\$sub" -Recurse -Force | Sort-Object FullName | ForEach-Object {
            $rel = $_.FullName.Substring($mtFull.Length + 1)
            $target = if ($_.LinkType) { ($_.Target -join ';') } else { '' }
            $len = if ($_.PSIsContainer) { '' } else { $_.Length }
            $links = if ($_.PSIsContainer) { '' } else { [Wo150.Win]::Links($_.FullName) }
            '{0}|{1}|{2}|{3}|{4}|{5}|{6}' -f $rel, $len, $_.LastWriteTimeUtc.Ticks, $_.CreationTimeUtc.Ticks, $_.Attributes, $target, $links
        }
    }
}

Write-Host "Modding Tools: $MtRoot"
$before = @(Get-Listing)
$before | Set-Content "$OutDir\listing-before.txt" -Encoding UTF8
Write-Host "before: $($before.Count) entries"

$fgBefore = [Wo150.Win]::GetForegroundWindow()
$toolArgs = @('tool', '--mt', "`"$MtRoot`"", '--delete-answer', 'N', '--out', "`"$OutDir\tool-run.txt`"")
if ($ToolExe) { $toolArgs += @('--tool-exe', "`"$ToolExe`"") }
$helper = Start-Process $HelperExe -ArgumentList $toolArgs -PassThru

$fgSeen = New-Object 'System.Collections.Generic.HashSet[string]'
$fgOwners = New-Object 'System.Collections.Generic.HashSet[int]'
$toolWindows = New-Object 'System.Collections.Generic.HashSet[string]'
$toolPids = New-Object 'System.Collections.Generic.HashSet[int]'
$samples = 0
while (-not $helper.HasExited) {
    $fg = [Wo150.Win]::GetForegroundWindow()
    [void]$fgSeen.Add($fg.ToString())
    $owner = [uint32]0; [void][Wo150.Win]::GetWindowThreadProcessId($fg, [ref]$owner); [void]$fgOwners.Add([int]$owner)
    foreach ($p in Get-Process -Name WorkspaceSetup, FakeWorkspaceSetup -ErrorAction SilentlyContinue) {
        [void]$toolPids.Add($p.Id)
        foreach ($w in [Wo150.Win]::VisibleWindowsOf([uint32]$p.Id)) { [void]$toolWindows.Add("$($p.Id):$w") }
    }
    $samples++
    Start-Sleep -Milliseconds 100
}
$fgAfter = [Wo150.Win]::GetForegroundWindow()

$after = @(Get-Listing)
$after | Set-Content "$OutDir\listing-after.txt" -Encoding UTF8
$diff = Compare-Object $before $after

$report = @(
    "helper exit: $($helper.ExitCode)",
    "tool processes seen: $($toolPids.Count)",
    "visible windows owned by the tool: $($toolWindows.Count)",
    "foreground ever owned by the helper or the tool: $(@($fgOwners | Where-Object { $_ -eq $helper.Id -or $toolPids.Contains($_) }).Count -gt 0)",
    "foreground samples: $samples; distinct foreground windows during the run: $($fgSeen.Count); unchanged before/after: $($fgBefore -eq $fgAfter)",
    "listing: before $($before.Count) entries, after $($after.Count) entries, differences: $(@($diff).Count)"
) + ($diff | ForEach-Object { "  $($_.SideIndicator) $($_.InputObject)" })
$report | Set-Content "$OutDir\evidence.txt" -Encoding UTF8
$report | ForEach-Object { Write-Host $_ }
Write-Host "--- tool-run.txt (redacted) ---"
Get-Content "$OutDir\tool-run.txt" | ForEach-Object { Write-Host $_ }
if (@($diff).Count -gt 0 -or $toolWindows.Count -gt 0) { exit 1 }
exit 0
