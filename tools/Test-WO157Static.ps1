# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-157 static checks, a release gate (tools\Build-Installer.ps1):
      1. the mod and its installer never add an antivirus or Windows Security exclusion and never change Windows'
         security settings, in any form (the maintainer's rule from WO-157 on): no such call in any shipped source;
      2. no separate injector: nothing the launcher, Setup or the payload build ships starts or copies
         KCDMP_LauncherInjector.exe (the launcher loads the DLL itself, dotnet\KcdMp.Setup\GameInjector.cs);
      3. the code-signing settings are never committed (tools\signing.local.json is git-ignored and untracked);
      4. the release-candidate path stays a separate, logged switch (Build-Installer.ps1 still runs the soak without it).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-WO157Static.ps1
#>
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$pass = 0; $fail = 0
function Check([string]$name, [bool]$ok, [string]$detail = "") {
    if ($ok) { $script:pass++; Write-Host "  PASS  $name" }
    else { $script:fail++; Write-Host "  FAIL  $name  $detail" -ForegroundColor Red }
}

# The shipped sources: the launcher, the .NET projects (not their tests), the installer, the native plugin, the mod.
$dirs = @("KCDMP_launcher", "dotnet", "installer", "native\KCDMP", "kdcmp\Data")
$files = foreach ($d in $dirs) {
    Get-ChildItem (Join-Path $root $d) -Recurse -File -Include *.cs, *.razor, *.iss, *.cpp, *.h, *.lua, *.ps1, *.xml |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|build)\\' -and $_.FullName -notmatch '\.Tests\\' -and $_.FullName -notmatch '\\tests\\' }
}

# 1. Windows' security, never changed. Reading the Smart App Control state (a read-only registry open) is allowed.
$forbidden = @(
    'Add-MpPreference', 'Set-MpPreference', 'MpPreference', 'ExclusionPath', 'ExclusionProcess', 'ExclusionExtension',
    'Windows Defender\\Exclusions', 'DisableRealtimeMonitoring', 'DisableAntiSpyware', 'TamperProtection',
    'SetValue\("VerifiedAndReputablePolicyState', 'netsh advfirewall', 'Set-ExecutionPolicy', 'EnableLUA'
)
$hits = @()
foreach ($f in $files) {
    $text = Get-Content $f.FullName -Raw
    foreach ($p in $forbidden) { if ($text -match $p) { $hits += "$($f.FullName.Substring($root.Length + 1)): $p" } }
}
Check "no antivirus exclusion and no Windows security setting is changed anywhere in the shipped sources" ($hits.Count -eq 0) ($hits -join "; ")
$sac = Get-Content (Join-Path $root "KCDMP_launcher\Pages\Home.Wo154.cs") -Raw
Check "the Smart App Control state is only read (OpenSubKey without write access)" ($sac -match 'OpenSubKey\(@"SYSTEM\\CurrentControlSet\\Control\\CI\\Policy"\)' -and $sac -notmatch 'OpenSubKey\([^)]*Policy"\s*,\s*true')

# 2. No injector exe.
$launcherCode = Get-ChildItem (Join-Path $root "KCDMP_launcher") -Recurse -File -Include *.cs, *.razor | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
$starts = @($launcherCode | Where-Object { (Get-Content $_.FullName -Raw) -match '"KCDMP_LauncherInjector\.exe"' })
Check "the launcher never starts KCDMP_LauncherInjector.exe" ($starts.Count -eq 0) ($starts.Name -join ", ")
$publish = Get-Content (Join-Path $root "tools\Publish-Release.ps1") -Raw
Check "the payload build does not copy the injector (and removes a stale one)" ($publish -notmatch 'Copy-Item \$nativeInjector' -and $publish -match 'Remove-Item \(Join-Path \$OutDir "KCDMP_LauncherInjector\.exe"\)')
$homeCs = Get-Content (Join-Path $root "KCDMP_launcher\Pages\Home.razor.cs") -Raw
Check "CONNECT loads the DLL from the launcher's own process (GameInjector)" ($homeCs -match 'InjectFromLauncherAsync' -and (Get-Content (Join-Path $root "KCDMP_launcher\Pages\Home.Wo157.cs") -Raw) -match 'GameInjector\.Inject\(')
$iss = Get-Content (Join-Path $root "installer\KCDMP.iss") -Raw
Check "the installer names no injector file to install" ($iss -notmatch 'Source:[^\r\n]*LauncherInjector')

# 3. Signing settings never committed.
Push-Location $root
try {
    $ignored = & git check-ignore -q "tools/signing.local.json"; $ign = $LASTEXITCODE -eq 0
    $tracked = (& git ls-files "tools/signing.local.json") -ne $null
} finally { Pop-Location }
Check "tools\signing.local.json is git-ignored" $ign
Check "tools\signing.local.json is not tracked" (-not $tracked)
$cs = Get-Content (Join-Path $root "tools\CodeSigning.ps1") -Raw
Check "the signing helper reads its settings from the environment or the ignored file only" ($cs -match 'KCDMP_SIGN_' -and $cs -match 'signing\.local\.json' -and $cs -notmatch 'https://[a-z]+\.codesigning\.azure\.net')

. (Join-Path $root "tools\CodeSigning.ps1")
$noFile = Join-Path $env:TEMP ("kcdmp-nosign-" + [guid]::NewGuid().ToString("N") + ".json")
Check "signing: no settings -> an unsigned build (null)" ($null -eq (Get-KcdmpSigningConfig -LocalFile $noFile -Environment @{}))
$half = $false
try { Get-KcdmpSigningConfig -LocalFile $noFile -Environment @{ KCDMP_SIGN_ENDPOINT = "https://x.codesigning.azure.net/" } | Out-Null } catch { $half = $_.Exception.Message -match 'half configured' }
Check "signing: half configured -> the build stops" $half
$full = Get-KcdmpSigningConfig -LocalFile $noFile -Environment @{ KCDMP_SIGN_ENDPOINT = "https://x.codesigning.azure.net/"; KCDMP_SIGN_ACCOUNT = "acct"; KCDMP_SIGN_PROFILE = "prof"; KCDMP_SIGN_DLIB = "C:\x\Azure.CodeSigning.Dlib.dll" }
Check "signing: all set -> used, with Microsoft's timestamp server by default" ($full.Account -eq "acct" -and $full.Timestamp -eq "http://timestamp.acs.microsoft.com")
$inno = Get-KcdmpSignCommand $full "C:\st\signtool.exe" "C:\m.json" -ForInno
Check "signing: Inno gets the command with `$q quotes, no literal quote" ($inno -notmatch '"' -and $inno.StartsWith('$qC:\st\signtool.exe$q sign /fd SHA256'))

# 4. The release-candidate path.
$bi = Get-Content (Join-Path $root "tools\Build-Installer.ps1") -Raw
Check "Build-Installer: -ReleaseCandidate skips only the soak, and says so in the build log and beside the installer" ($bi -match '\[switch\]\$ReleaseCandidate' -and $bi -match 'not soak-tested: release candidate, not for public release' -and $bi -match 'Start-Transcript' -and $bi -match 'RELEASE-CANDIDATE-\$Version\.txt')
Check "Build-Installer: without it the soak is demanded" ($bi -match '& python \$soak check')
Check "the installer writes the release-candidate line into install-verify.txt" ($iss -match '#ifdef ReleaseCandidate' -and $iss -match "not soak-tested: release candidate, not for public release")
Check "the installer is signed only when Build-Installer defines the tool" ($iss -match '#ifdef KcdmpSign\s+SignTool=kcdmpsign')

Write-Host ""
Write-Host "--------------------------------------------"
Write-Host "  passed: $pass   failed: $fail"
if ($fail -gt 0) { exit 1 } else { exit 0 }
