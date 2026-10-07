# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-157: optional code signing for tools\Build-Installer.ps1 (Azure Artifact Signing). Dot-source it.

.DESCRIPTION
    The settings are read from the environment or from tools\signing.local.json (git-ignored), never from
    anything committed. docs\CODE-SIGNING.md is the maintainer's setup page.

      KCDMP_SIGN_ENDPOINT   the account's regional endpoint, e.g. https://<region>.codesigning.azure.net/
      KCDMP_SIGN_ACCOUNT    the Artifact Signing account name
      KCDMP_SIGN_PROFILE    the certificate profile name
      KCDMP_SIGN_DLIB       full path of Azure.CodeSigning.Dlib.dll (bin\x64, from the
                            Microsoft.ArtifactSigning.Client package)
      KCDMP_SIGN_SIGNTOOL   optional: full path of signtool.exe (default: the newest x64 one in the Windows SDK)
      KCDMP_SIGN_TIMESTAMP  optional: RFC 3161 timestamp server (default http://timestamp.acs.microsoft.com)

    The JSON file uses the same names without the KCDMP_SIGN_ prefix (Endpoint, Account, Profile, Dlib,
    SignTool, Timestamp); the environment wins over the file. The sign-in itself is Azure's
    (DefaultAzureCredential: `az login`, or AZURE_TENANT_ID / AZURE_CLIENT_ID / AZURE_CLIENT_SECRET) and is
    never read or stored here.

    None set: the build is unsigned and says so. Some set, some not: the build stops (a half-configured
    signing must never quietly ship unsigned).
#>

$script:SigningKeys = @('Endpoint', 'Account', 'Profile', 'Dlib')

function Get-KcdmpSigningConfig {
    param([string]$LocalFile = (Join-Path $PSScriptRoot 'signing.local.json'), [hashtable]$Environment)
    $envOf = { param($n) if ($Environment) { $Environment[$n] } else { [Environment]::GetEnvironmentVariable($n) } }
    $file = @{}
    if (Test-Path $LocalFile) {
        $json = Get-Content $LocalFile -Raw | ConvertFrom-Json
        foreach ($p in $json.PSObject.Properties) { $file[$p.Name] = [string]$p.Value }
    }
    $cfg = [ordered]@{}
    foreach ($k in @('Endpoint', 'Account', 'Profile', 'Dlib', 'SignTool', 'Timestamp')) {
        $v = & $envOf ("KCDMP_SIGN_" + $k.ToUpperInvariant())
        if ([string]::IsNullOrWhiteSpace($v)) { $v = $file[$k] }
        $cfg[$k] = if ([string]::IsNullOrWhiteSpace($v)) { $null } else { $v.Trim() }
    }
    $set = @($script:SigningKeys | Where-Object { $cfg[$_] })
    if ($set.Count -eq 0) { return $null }
    if ($set.Count -lt $script:SigningKeys.Count) {
        $missing = ($script:SigningKeys | Where-Object { -not $cfg[$_] }) -join ', '
        throw "code signing is half configured: missing $missing (see docs\CODE-SIGNING.md). Set all of them, or none to build unsigned."
    }
    if (-not $cfg.Timestamp) { $cfg.Timestamp = 'http://timestamp.acs.microsoft.com' }
    if ($cfg.Endpoint -notmatch '^https://') { throw "KCDMP_SIGN_ENDPOINT must be the account's https:// endpoint" }
    return [pscustomobject]$cfg
}

function Find-KcdmpSignTool {
    param($Config)
    if ($Config.SignTool) {
        if (-not (Test-Path $Config.SignTool)) { throw "signtool not found: $($Config.SignTool)" }
        return $Config.SignTool
    }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $found = Get-ChildItem $kits -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^10\.' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $found) { throw "signtool.exe (x64) not found in the Windows SDK; set KCDMP_SIGN_SIGNTOOL" }
    return $found
}

<# The metadata file Artifact Signing's dlib reads; written to a temp path for the build, deleted after. #>
function New-KcdmpSigningMetadata {
    param($Config)
    $path = Join-Path ([IO.Path]::GetTempPath()) ("kcdmp-sign-" + [guid]::NewGuid().ToString('N') + ".json")
    [ordered]@{ Endpoint = $Config.Endpoint; CodeSigningAccountName = $Config.Account; CertificateProfileName = $Config.Profile } |
        ConvertTo-Json | Set-Content -Encoding ascii $path
    return $path
}

<# The signtool command line (without the files): also handed to Inno Setup for the Setup and its uninstaller. #>
function Get-KcdmpSignCommand {
    param($Config, [string]$SignTool, [string]$Metadata, [switch]$ForInno)
    $cmd = ('"{0}" sign /fd SHA256 /tr "{1}" /td SHA256 /dlib "{2}" /dmdf "{3}"' -f $SignTool, $Config.Timestamp, $Config.Dlib, $Metadata)
    # ISCC's /S<name>=<command>: $q is a quote (a literal one does not survive Windows PowerShell's
    # argument passing to a native program); Inno appends the file as $f itself.
    if ($ForInno) { $cmd = $cmd.Replace('"', '$q') }
    return $cmd
}

<# Signs every .exe/.dll under $Dir that has no valid signature yet (Microsoft's own runtime files keep theirs). #>
function Invoke-KcdmpSignPayload {
    param([string]$Dir, $Config, [string]$SignTool, [string]$Metadata)
    $pe = Get-ChildItem $Dir -Recurse -File -Include *.exe, *.dll
    $todo = @($pe | Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -ne 'Valid' })
    Write-Host "Code signing: $($todo.Count) of $($pe.Count) exe/dll file(s) need a signature"
    # signtool takes many files per call; batches keep the command line short.
    for ($i = 0; $i -lt $todo.Count; $i += 20) {
        $batch = $todo[$i..([Math]::Min($i + 19, $todo.Count - 1))] | ForEach-Object { $_.FullName }
        & $SignTool sign /fd SHA256 /tr $Config.Timestamp /td SHA256 /dlib $Config.Dlib /dmdf $Metadata @batch
        if ($LASTEXITCODE -ne 0) { throw "signtool failed (exit $LASTEXITCODE) on: $($batch -join ', ')" }
    }
    Assert-KcdmpSigned -Files ($pe | ForEach-Object { $_.FullName })
}

<# Every file must carry a valid, timestamped signature. #>
function Assert-KcdmpSigned {
    param([string[]]$Files)
    $bad = @()
    foreach ($f in $Files) {
        $sig = Get-AuthenticodeSignature $f
        if ($sig.Status -ne 'Valid') { $bad += "$f ($($sig.Status))"; continue }
        if (-not $sig.TimeStamperCertificate) { $bad += "$f (no timestamp)" }
    }
    if ($bad.Count) { throw "signature check FAILED for $($bad.Count) file(s):`n  " + ($bad -join "`n  ") }
    Write-Host "Code signing: $($Files.Count) file(s) carry a valid, timestamped signature"
}
