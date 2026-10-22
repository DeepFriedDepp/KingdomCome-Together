# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    WO-203 gate (the keylogger rule): nothing we build can read what a person types or clicks.

.DESCRIPTION
    Build-blocking, with a self-test. Nothing of this project -- the mod, the agent, the launcher, the installer, the
    relay, and every test tool, script and harness -- may read physical input. This gate looks for every way to do it:

      * keyboard / mouse hooks (the Windows hook API and its keyboard, mouse, message and window-procedure hook kinds);
      * key-state polling (asynchronous key state, key state, the whole keyboard's state);
      * raw input (registering raw input devices, reading raw input data or buffers);
      * DirectInput and XInput (their libraries and their create / acquire / get-state entry points);
      * window subclassing (the window-procedure slot, the subclass API, WinForms / WPF message hooks);
      * the game's own input event stream (CryInput event listeners, posted input events);
      * clipboard reads;
      * mouse position reads;
      * keyboard / mouse capture libraries for scripts.

    Binaries (PE files): the import table AND the delay-load import table are parsed, and every imported function and
    library is checked; managed assemblies are checked by the names in their metadata (P/Invoke entry points), and every
    binary also by the API names as ASCII and UTF-16 text (a lookup by name). Scripts and sources (PowerShell, Python, Lua,
    shell, batch, JavaScript, Inno Setup, C#, C/C++ and more) are checked line by line, inside zip / pak archives too.

    Sending input (SendInput, a message posted to the game's own window) is not reading it: test tools may send. Docs are
    not scanned. The names are built from fragments below so that this file never matches itself.

    Exemptions are listed in $ExemptPaths by exact path or file name with the reason, printed on every run; Microsoft's own
    .NET runtime files (shipped inside the launcher's self-contained publish) are exempt only by exact name AND only if they
    carry Microsoft's copyright mark -- none of our code calls their input APIs.

.PARAMETER Binaries
    Files or folders: every PE file (.exe .dll .sys .ocx .node .pyd) under them is checked.
.PARAMETER Scripts
    Files or folders: every script / source file (and every zip or pak archive's script entries) under them is checked.
.PARAMETER Repo
    Add the repository's own files: every tracked script / source file, new untracked ones inside the repository's own
    top-level folders, and every work order's test-tool folder in the repository's git-ignored folders (<ignored>\WO-<n>\tools,
    found through git itself and never named here), binaries and scripts.
.PARAMETER SelfTest
    Prove the gate catches what it must: compile throwaway binaries (one importing the asynchronous key-state API, one
    delay-loading the keyboard-state API, one managed hook declaration, one clean) and scripts, and check each verdict.
.PARAMETER ListAll
    Print one line for every binary and every script checked (default: binaries one line each, scripts as counts).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Check-NoInputCapture.ps1 -SelfTest -Repo -Binaries native\build
#>
param(
    [string[]]$Binaries = @(),
    [string[]]$Scripts = @(),
    [switch]$Repo,
    [switch]$SelfTest,
    [switch]$ListAll
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# ---- the names, from fragments ------------------------------------------------------------------------------------
function J([string[]]$p) { return ($p -join '') }
$K = @{
    hookEx      = (J 'SetWindows', 'HookEx')
    asyncKey    = (J 'GetAsync', 'KeyState')
    keyState    = (J 'GetKey', 'State')
    kbState     = (J 'GetKeyboard', 'State')
    rawReg      = (J 'RegisterRaw', 'InputDevices')
    rawData     = (J 'GetRaw', 'InputData')
    rawBuf      = (J 'GetRaw', 'InputBuffer')
    di8         = (J 'DirectInput', '8Create')
    di          = (J 'DirectInput', 'Create')
    diDev       = (J 'IDirectInput', 'Device')
    diKb        = (J 'c_dfDI', 'Keyboard')
    diMouse     = (J 'c_dfDI', 'Mouse')
    guidKb      = (J 'GUID_Sys', 'Keyboard')
    guidMouse   = (J 'GUID_Sys', 'Mouse')
    xiState     = (J 'XInput', 'GetState')
    xiKey       = (J 'XInput', 'GetKeystroke')
    subclass    = (J 'SetWindow', 'Subclass')
    wndprocL    = (J 'GWLP_', 'WNDPROC')
    wndprocS    = (J 'GWL_', 'WNDPROC')
    clipOpen    = (J 'Open', 'Clipboard')
    clipData    = (J 'GetClipboard', 'Data')
    cursorPos   = (J 'GetCursor', 'Pos')
    physCursor  = (J 'GetPhysicalCursor', 'Pos')
    msgPos      = (J 'GetMessage', 'Pos')
    inListener  = (J 'IInputEvent', 'Listener')
    onInput     = (J 'OnInput', 'Event')
    postInput   = (J 'PostInput', 'Event')
    addListener = (J 'Add', 'EventListener')
    addPrio     = (J 'AddPriority', 'EventListener')
    getClip     = (J 'Get-Clip', 'board')
    clipGet     = (J 'clipboard', '_get')
    pyclip      = (J 'pyper', 'clip')
    w32clip     = (J 'win32', 'clipboard')
    capLib1     = (J 'pyn', 'put')
    capLib2     = (J 'pyauto', 'gui')
    capLib3     = (J 'AutoHot', 'key')
    diLib       = (J 'din', 'put')
}
$hookKinds = @((J 'WH_KEY', 'BOARD'), (J 'WH_MOU', 'SE'), (J 'WH_GET', 'MESSAGE'), (J 'WH_CALL', 'WNDPROC'))

# Imported functions that read input (any library), and libraries whose import alone is acquisition.
$BadFunctions = @(
    @{ n = $K.hookEx;     why = 'Windows hook (keyboard / mouse / message hooks)' }
    @{ n = $K.asyncKey;   why = 'key-state polling (asynchronous)' }
    @{ n = $K.keyState;   why = 'key-state polling' }
    @{ n = $K.kbState;    why = 'key-state polling (whole keyboard)' }
    @{ n = $K.rawReg;     why = 'raw input registration' }
    @{ n = $K.rawData;    why = 'raw input read' }
    @{ n = $K.rawBuf;     why = 'raw input read (buffered)' }
    @{ n = $K.di8;        why = 'DirectInput' }
    @{ n = $K.di;         why = 'DirectInput' }
    @{ n = $K.xiState;    why = 'XInput state read' }
    @{ n = $K.xiKey;      why = 'XInput keystroke read' }
    @{ n = $K.subclass;   why = 'window subclassing' }
    @{ n = $K.clipOpen;   why = 'clipboard access' }
    @{ n = $K.clipData;   why = 'clipboard read' }
    @{ n = $K.cursorPos;  why = 'mouse position read' }
    @{ n = $K.physCursor; why = 'mouse position read' }
)
$BadLibraries = @(
    @{ rx = '^' + $K.diLib + '8?\.dll$';       why = 'DirectInput library' }
    @{ rx = '^xinput(1_[1-4]|9_1_0|uap)\.dll$'; why = 'XInput library' }
)

# Script / source line patterns (regex, each built from the fragments).
function E([string]$s) { return [regex]::Escape($s) }
$ScriptRules = @(
    @{ rx = (E $K.hookEx);                                         why = 'Windows hook' }
    @{ rx = '\b(' + (($hookKinds | ForEach-Object { E $_ }) -join '|') + ')'; why = 'hook kind (keyboard / mouse / message / window procedure)' }
    @{ rx = '\b' + (E $K.asyncKey) + '\b';                         why = 'key-state polling (asynchronous)' }
    @{ rx = '\b' + (E $K.keyState) + '\b';                         why = 'key-state polling' }
    @{ rx = '\b' + (E $K.kbState) + '\b';                          why = 'key-state polling (whole keyboard)' }
    @{ rx = (E $K.rawReg) + '|' + (E $K.rawData) + '|' + (E $K.rawBuf); why = 'raw input' }
    @{ rx = (E $K.di8) + '|\b' + (E $K.di) + '\b|' + (E $K.diDev) + '|' + (E $K.diKb) + '|' + (E $K.diMouse) + '|' + (E $K.guidKb) + '|' + (E $K.guidMouse) + '|\b' + $K.diLib + '8?\b'; why = 'DirectInput' }
    @{ rx = (E $K.xiState) + '|' + (E $K.xiKey) + '|\bxinput(1_[1-4]|9_1_0)\b'; why = 'XInput' }
    @{ rx = (E $K.subclass) + '|' + (E $K.wndprocL) + '|' + (E $K.wndprocS) + '|SetWindowLong(Ptr)?[AW]?\s*\([^)]*,\s*-4\s*,|\.AssignHandle\s*\(|HwndSource\b[^\n]*\.AddHook\s*\(|\bAddHook\s*\(\s*WndProc'; why = 'window subclassing' }
    @{ rx = (E $K.inListener) + '|\b' + (E $K.onInput) + '\b|\b' + (E $K.postInput) + '\b|\b' + (E $K.addPrio) + '\b|\b(IInput|gEnv->pInput|pInput)\s*->\s*' + (E $K.addListener) + '\b'; why = "the game's input event stream" }
    @{ rx = (E $K.clipOpen) + '|' + (E $K.clipData) + '|' + (E $K.getClip) + '|Clipboard\]::Get|Clipboard\.Get(Text|Data|Image)|' + $K.clipGet + '|' + $K.pyclip + '|' + $K.w32clip; why = 'clipboard read' }
    @{ rx = '\b(' + (E $K.cursorPos) + '|' + (E $K.physCursor) + '|' + (E $K.msgPos) + ')\b|Cursor\]::Position|Cursor\.Position|' + $K.capLib2 + '\.position|mouse\.get_position'; why = 'mouse position read' }
    @{ rx = '\b' + $K.capLib1 + '\b|\bimport\s+(keyboard|mouse)\b|\bfrom\s+(keyboard|mouse)\s+import\b|\b' + $K.capLib2 + '\b|' + $K.capLib3 + '|\bkeyboard\.(read_|record|on_press|hook)'; why = 'keyboard / mouse capture library' }
)

$ScriptExt = @('.ps1', '.psm1', '.psd1', '.py', '.pyw', '.lua', '.sh', '.bash', '.bat', '.cmd', '.js', '.mjs', '.cjs', '.ts',
    '.vbs', '.ahk', '.au3', '.iss', '.cs', '.csx', '.vb', '.cpp', '.cc', '.cxx', '.c', '.h', '.hpp', '.hh', '.inl', '.razor',
    '.cshtml', '.java', '.go', '.rs', '.rb', '.pl', '.nsi', '.lua.txt')
$ArchiveExt = @('.pak', '.zip')
$PeExt = @('.exe', '.dll', '.sys', '.ocx', '.node', '.pyd')

# Reviewed exemptions, printed on every run.
$ExemptPaths = @{
    # exact repository-relative path -> reason
    'tools/Test-NoInputTooling.py' = "the payload gate's own pattern list (WO-175): byte patterns it searches for, never called"
}
$MicrosoftRuntime = @('System.Windows.Forms.dll', 'System.Windows.Forms.Primitives.dll', 'System.Windows.Forms.Design.dll',
    'Microsoft.VisualBasic.Forms.dll', 'PresentationCore.dll', 'PresentationFramework.dll', 'PresentationNative_cor3.dll',
    'PenImc_cor3.dll', 'wpfgfx_cor3.dll', 'UIAutomationClient.dll', 'UIAutomationClientSideProviders.dll',
    'UIAutomationProvider.dll', 'UIAutomationTypes.dll', 'WindowsBase.dll', 'System.Console.dll', 'System.Private.CoreLib.dll',
    'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll',
    'Microsoft.Web.WebView2.Wpf.dll', 'WebView2Loader.dll', 'Microsoft.AspNetCore.Components.WebView.WindowsForms.dll',
    'Microsoft.AspNetCore.Components.WebView.Wpf.dll', 'System.Windows.Forms.resources.dll', 'D3DCompiler_47_cor3.dll',
    'vcruntime140_cor3.dll', 'System.Drawing.Common.dll', 'System.Private.Windows.Core.dll', 'Accessibility.dll',
    'ReachFramework.dll', 'System.Xaml.dll', 'DirectWriteForwarder.dll')
$MicrosoftWhy = 'Microsoft .NET / WebView2 runtime file (framework input API, not called by this project)'

# ---- PE parsing (import + delay-load tables) ------------------------------------------------------------------------
if (-not ('NicPe' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Text;

public static class NicPe
{
    static int U16(byte[] b, long o) { return b[o] | (b[o + 1] << 8); }
    static uint U32(byte[] b, long o) { return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)); }
    static ulong U64(byte[] b, long o) { return (ulong)U32(b, o) | ((ulong)U32(b, o + 4) << 32); }

    sealed class Sec { public uint Va, VSize, Raw, RawSize; }

    static long Off(List<Sec> secs, uint rva, long len)
    {
        foreach (Sec s in secs)
        {
            uint size = Math.Max(s.VSize, s.RawSize);
            if (rva >= s.Va && rva < s.Va + size)
            {
                long o = (long)rva - s.Va + s.Raw;
                return (o >= 0 && o < len) ? o : -1;
            }
        }
        return -1;
    }

    static string Str(byte[] b, long o)
    {
        if (o < 0) return null;
        long e = o;
        while (e < b.Length && b[e] != 0 && e - o < 512) e++;
        return Encoding.ASCII.GetString(b, (int)o, (int)(e - o));
    }

    // Returns the imports as "library!function" (library lowercased; "#n" for an ordinal), delay-loaded ones prefixed
    // "delay:". isPe=false for a file that is not a PE image; managed=true when it carries a CLR header.
    public static List<string> Imports(byte[] b, out bool isPe, out bool managed, out string error)
    {
        List<string> res = new List<string>();
        isPe = false; managed = false; error = null;
        try
        {
            if (b.Length < 0x40 || b[0] != (byte)'M' || b[1] != (byte)'Z') return res;
            long pe = U32(b, 0x3C);
            if (pe <= 0 || pe + 24 > b.Length || U32(b, pe) != 0x00004550) return res;
            isPe = true;
            int nsec = U16(b, pe + 6);
            int optSize = U16(b, pe + 20);
            long opt = pe + 24;
            int magic = U16(b, opt);
            bool p64 = magic == 0x20B;
            ulong imageBase = p64 ? U64(b, opt + 24) : U32(b, opt + 28);
            long dd = opt + (p64 ? 112 : 96);
            uint ndd = U32(b, opt + (p64 ? 108 : 92));
            List<Sec> secs = new List<Sec>();
            long st = opt + optSize;
            for (int i = 0; i < nsec; i++)
            {
                long s = st + i * 40;
                if (s + 40 > b.Length) break;
                Sec x = new Sec();
                x.VSize = U32(b, s + 8); x.Va = U32(b, s + 12); x.RawSize = U32(b, s + 16); x.Raw = U32(b, s + 20);
                secs.Add(x);
            }
            Func<int, uint> dirRva = delegate (int i) { return i < ndd ? U32(b, dd + i * 8) : 0u; };
            Func<int, uint> dirSize = delegate (int i) { return i < ndd ? U32(b, dd + i * 8 + 4) : 0u; };
            managed = dirRva(14) != 0 && dirSize(14) != 0;

            // the import table
            uint imp = dirRva(1);
            if (imp != 0)
            {
                long d = Off(secs, imp, b.Length);
                for (int guard = 0; d >= 0 && d + 20 <= b.Length && guard < 4096; guard++, d += 20)
                {
                    uint oft = U32(b, d), name = U32(b, d + 12), ft = U32(b, d + 16);
                    if (oft == 0 && name == 0 && ft == 0) break;
                    string lib = (Str(b, Off(secs, name, b.Length)) ?? "?").ToLowerInvariant();
                    ReadThunks(b, secs, oft != 0 ? oft : ft, p64, lib, "", res, 0);
                }
            }
            // the delay-load import table
            uint dimp = dirRva(13);
            if (dimp != 0)
            {
                long d = Off(secs, dimp, b.Length);
                for (int guard = 0; d >= 0 && d + 32 <= b.Length && guard < 4096; guard++, d += 32)
                {
                    uint attr = U32(b, d), name = U32(b, d + 4), nt = U32(b, d + 16);
                    if (attr == 0 && name == 0 && nt == 0) break;
                    ulong bias = (attr & 1) == 0 ? imageBase : 0;   // version-1 descriptors hold virtual addresses
                    string lib = (Str(b, Off(secs, (uint)(name - bias), b.Length)) ?? "?").ToLowerInvariant();
                    ReadThunks(b, secs, (uint)(nt - bias), p64, lib, "delay:", res, (attr & 1) == 0 ? imageBase : 0);
                }
            }
        }
        catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
        return res;
    }

    static void ReadThunks(byte[] b, List<Sec> secs, uint rva, bool p64, string lib, string pfx, List<string> res, ulong bias)
    {
        long t = Off(secs, rva, b.Length);
        int step = p64 ? 8 : 4;
        int count = 0;
        for (; t >= 0 && t + step <= b.Length && count < 65536; t += step, count++)
        {
            ulong v = p64 ? U64(b, t) : U32(b, t);
            if (v == 0) break;
            bool ordinal = p64 ? (v & 0x8000000000000000UL) != 0 : (v & 0x80000000UL) != 0;
            if (ordinal) { res.Add(pfx + lib + "!#" + (v & 0xFFFF)); continue; }
            long n = Off(secs, (uint)((v - bias) & 0x7FFFFFFF), b.Length);
            string fn = n >= 0 ? Str(b, n + 2) : null;
            res.Add(pfx + lib + "!" + (fn ?? "?"));
        }
        if (count == 0) res.Add(pfx + lib + "!");
    }

    // Byte search for an ASCII name, and the same name as UTF-16LE.
    public static bool Contains(byte[] hay, string name)
    {
        return IndexOf(hay, Encoding.ASCII.GetBytes(name)) >= 0 || IndexOf(hay, Encoding.Unicode.GetBytes(name)) >= 0;
    }

    static int IndexOf(byte[] hay, byte[] needle)
    {
        if (needle.Length == 0 || hay.Length < needle.Length) return -1;
        byte first = needle[0];
        int last = hay.Length - needle.Length;
        for (int i = 0; i <= last; i++)
        {
            if (hay[i] != first) continue;
            int j = 1;
            while (j < needle.Length && hay[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }
}
'@
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# ---- checks ---------------------------------------------------------------------------------------------------------
function Get-Rel([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    if ($full.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    }
    return $full
}

function Test-MicrosoftRuntime([string]$path, [byte[]]$bytes) {
    $leaf = Split-Path $path -Leaf
    if ($MicrosoftRuntime -notcontains $leaf) { return $false }
    return [NicPe]::Contains($bytes, 'Microsoft Corporation')
}

# One binary: returns @{ hits = @(...); imports = n; delay = n; managed = bool; pe = bool; error = string }
function Test-Binary([string]$path, [byte[]]$bytes) {
    $isPe = $false; $managed = $false; $err = $null
    $imports = [NicPe]::Imports($bytes, [ref]$isPe, [ref]$managed, [ref]$err)
    $hits = New-Object System.Collections.ArrayList
    $seen = @{}
    foreach ($imp in $imports) {
        $delay = $imp.StartsWith('delay:')
        $x = if ($delay) { $imp.Substring(6) } else { $imp }
        $lib, $fn = $x.Split('!', 2)
        foreach ($bl in $BadLibraries) {
            if ($lib -match $bl.rx -and -not $seen.ContainsKey("lib:$lib")) {
                $seen["lib:$lib"] = 1
                [void]$hits.Add(('{0}import of {1} ({2})' -f ($(if ($delay) { 'delay-load ' } else { '' })), $lib, $bl.why))
            }
        }
        foreach ($bf in $BadFunctions) {
            if ($fn -eq $bf.n -or $fn -eq ($bf.n + 'A') -or $fn -eq ($bf.n + 'W')) {
                [void]$hits.Add(('{0}import {1}!{2} ({3})' -f ($(if ($delay) { 'delay-load ' } else { '' })), $lib, $fn, $bf.why))
            }
        }
    }
    if ($isPe) {
        # the names as text: a managed P/Invoke entry point, or a lookup by name (GetProcAddress) in native code
        foreach ($bf in $BadFunctions) {
            if ([NicPe]::Contains($bytes, $bf.n)) {
                $already = $false
                foreach ($h in $hits) { if ($h -like "*!$($bf.n)*") { $already = $true } }
                if (-not $already) { [void]$hits.Add(('name {0} in the file ({1}{2})' -f $bf.n, $bf.why, $(if ($managed) { ', managed metadata / strings' } else { ', a lookup by name' }))) }
            }
        }
    }
    $nDelay = @($imports | Where-Object { $_.StartsWith('delay:') }).Count
    return @{ hits = $hits; imports = ($imports.Count - $nDelay); delay = $nDelay; managed = $managed; pe = $isPe; error = $err }
}

function Test-ScriptText([string]$name, [string]$text) {
    $hits = New-Object System.Collections.ArrayList
    $lines = $text -split "`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($r in $ScriptRules) {
            if ($lines[$i] -match $r.rx) {
                $snippet = $lines[$i].Trim()
                if ($snippet.Length -gt 140) { $snippet = $snippet.Substring(0, 140) + '...' }
                [void]$hits.Add(('line {0}: {1} -- {2}' -f ($i + 1), $r.why, $snippet))
            }
        }
    }
    return $hits
}

function Read-Text([byte[]]$bytes) {
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) { return [Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2) }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { return [Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3) }
    return [Text.Encoding]::UTF8.GetString($bytes)
}

function Get-Ext([string]$name) {
    $n = $name.ToLowerInvariant()
    if ($n.EndsWith('.lua.txt')) { return '.lua.txt' }
    return [IO.Path]::GetExtension($n)
}

# ---- the run --------------------------------------------------------------------------------------------------------
$results = New-Object System.Collections.ArrayList   # @{ kind; name; hits; exempt; note }
$counts = @{ binaries = 0; scripts = 0; archives = 0; archiveEntries = 0; peImports = 0; peDelay = 0 }

function Add-Result($kind, $name, $hits, $exempt, $note) {
    # an empty list returned from a function arrives as $null: keep only real hit lines
    [void]$results.Add(@{ kind = $kind; name = $name; hits = @($hits | Where-Object { $_ -and "$_".Trim() }); exempt = $exempt; note = $note })
}

function Invoke-OneFile([string]$path, [string]$want) {
    $ext = Get-Ext $path
    $rel = Get-Rel $path
    $leaf = Split-Path $path -Leaf
    if ($want -ne 'script' -and $PeExt -contains $ext) {
        $bytes = [IO.File]::ReadAllBytes($path)
        $r = Test-Binary $path $bytes
        if (-not $r.pe) { return }
        $counts.binaries++; $counts.peImports += $r.imports; $counts.peDelay += $r.delay
        $exempt = $null
        if ($r.hits.Count -gt 0 -and (Test-MicrosoftRuntime $path $bytes)) { $exempt = $MicrosoftWhy }
        if ($ExemptPaths.ContainsKey($rel)) { $exempt = $ExemptPaths[$rel] }
        $note = '{0} imports, {1} delay-load{2}{3}' -f $r.imports, $r.delay, $(if ($r.managed) { ', managed' } else { '' }), $(if ($r.error) { ', PARSE ERROR ' + $r.error } else { '' })
        if ($r.error) { [void]$r.hits.Add('the PE could not be parsed: ' + $r.error) }
        Add-Result 'binary' $rel $r.hits $exempt $note
        return
    }
    if ($want -ne 'binary' -and $ArchiveExt -contains $ext) {
        $counts.archives++
        try {
            $zip = [IO.Compression.ZipFile]::OpenRead($path)
            try {
                foreach ($e in $zip.Entries) {
                    $eext = Get-Ext $e.FullName
                    if ($ScriptExt -notcontains $eext) { continue }
                    $counts.archiveEntries++
                    $ms = New-Object IO.MemoryStream
                    $s = $e.Open(); $s.CopyTo($ms); $s.Dispose()
                    $hits = Test-ScriptText ($rel + '!' + $e.FullName) (Read-Text $ms.ToArray())
                    Add-Result 'script' ($rel + '!' + $e.FullName) $hits $null 'in an archive'
                }
            } finally { $zip.Dispose() }
        } catch {
            Add-Result 'script' $rel @('the archive could not be opened: ' + $_.Exception.Message) $null ''
        }
        return
    }
    if ($want -ne 'binary' -and $ScriptExt -contains $ext) {
        $counts.scripts++
        $hits = Test-ScriptText $rel (Read-Text ([IO.File]::ReadAllBytes($path)))
        $exempt = $null
        if ($ExemptPaths.ContainsKey($rel)) { $exempt = $ExemptPaths[$rel] }
        Add-Result 'script' $rel $hits $exempt ''
    }
}

function Invoke-Root([string]$root, [string]$want) {
    if (-not (Test-Path -LiteralPath $root)) { Write-Output ("MISSING  {0} (nothing to check there)" -f $root); return }
    if (Test-Path -LiteralPath $root -PathType Leaf) { Invoke-OneFile (Resolve-Path -LiteralPath $root).Path $want; return }
    Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](obj|\.git|\.vs|node_modules|__pycache__)[\\/]' } |
        ForEach-Object { Invoke-OneFile $_.FullName $want }
}

function Get-RepoFiles {
    Push-Location $repoRoot
    try {
        $tracked = @(git -c core.quotepath=off ls-files)
        $topTracked = @{}
        foreach ($t in $tracked) { $topTracked[($t -split '/')[0]] = 1 }
        # new files not committed yet, only inside the repository's own top-level folders (never a copy of something
        # else that happens to sit beside it)
        $untracked = @(git -c core.quotepath=off ls-files --others --exclude-standard) | Where-Object { $topTracked.ContainsKey(($_ -split '/')[0]) -and ($_ -match '/') }
        return @($tracked + $untracked | Where-Object { $ScriptExt -contains (Get-Ext $_) } | ForEach-Object { Join-Path $repoRoot $_ })
    } finally { Pop-Location }
}

# The work orders' test-tool folders: <ignored>\WO-<n>\tools in the repository's git-ignored top-level folders, found through git
# (the same way the publishing gate finds the field folders), so no private folder is named in this tracked file.
function Get-ToolDirs {
    Push-Location $repoRoot
    try {
        $dirs = @()
        foreach ($d in @(git -c core.quotepath=off ls-files --others --ignored --exclude-standard --directory)) {
            $trim = $d.TrimEnd('/')
            if (-not $trim -or $trim.Contains('/')) { continue }   # top-level ignored folders only
            $full = Join-Path $repoRoot $trim
            if (-not (Test-Path -LiteralPath $full -PathType Container)) { continue }
            foreach ($wo in @(Get-ChildItem -LiteralPath $full -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^WO-[0-9]+' })) {
                $t = Join-Path $wo.FullName 'tools'
                if (Test-Path -LiteralPath $t -PathType Container) { $dirs += $t }
            }
        }
        return $dirs
    } finally { Pop-Location }
}
$script:toolRoots = @()

# ---- the self-test ----------------------------------------------------------------------------------------------------
function Invoke-SelfTest {
    $dir = Join-Path $repoRoot 'native\build\no-input-capture-selftest'
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { throw 'self-test: vswhere not found -- the C++ build tools are needed to compile the test binaries' }
    $vs = & $vswhere -products * -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
    if (-not (Test-Path $vcvars)) { throw 'self-test: vcvars64.bat not found' }

    $ak = $K.asyncKey; $ks = $K.kbState; $hk = $K.hookEx; $rr = $K.rawReg; $gc = $K.getClip
    Set-Content -Encoding ascii (Join-Path $dir 'bad_import.c') @"
#include <windows.h>
int main(void) { return (int)$ak(VK_SHIFT) & 1; }
"@
    Set-Content -Encoding ascii (Join-Path $dir 'bad_delay.c') @"
#include <windows.h>
__declspec(dllexport) int probe(void) { BYTE s[256]; return $ks(s) ? 1 : 0; }
"@
    Set-Content -Encoding ascii (Join-Path $dir 'clean.c') @"
#include <windows.h>
int main(void) { return (int)(GetTickCount() & 1); }
"@
    $bat = Join-Path $dir 'build.bat'
    Set-Content -Encoding ascii $bat @"
@echo off
call "$vcvars" >nul 2>&1
cd /d "$dir"
cl /nologo /O1 bad_import.c /link user32.lib >build.log 2>&1 || exit /b 1
cl /nologo /O1 /LD bad_delay.c /link user32.lib delayimp.lib /DELAYLOAD:user32.dll >>build.log 2>&1 || exit /b 2
cl /nologo /O1 clean.c >>build.log 2>&1 || exit /b 3
"@
    & cmd /c "`"$bat`""
    if ($LASTEXITCODE -ne 0) { throw "self-test: the test binaries did not compile (step $LASTEXITCODE; $dir\build.log)" }

    # a managed assembly declaring the hook API as a P/Invoke
    $cs = "public static class SelfTestHook { [System.Runtime.InteropServices.DllImport(`"user32.dll`")] public static extern System.IntPtr $hk(int idHook, System.IntPtr fn, System.IntPtr mod, uint tid); }"
    Add-Type -TypeDefinition $cs -OutputAssembly (Join-Path $dir 'bad_managed.dll') -OutputType Library

    # scripts
    Set-Content -Encoding utf8 (Join-Path $dir 'bad_raw.py') "import ctypes`nctypes.windll.user32.$rr(None, 1, 0)`n"
    Set-Content -Encoding utf8 (Join-Path $dir 'bad_clip.ps1') "`$t = $gc`n"
    Set-Content -Encoding utf8 (Join-Path $dir 'clean_send.py') "import ctypes`n# send only: the game's own window, SendInput`nctypes.windll.user32.SendInput(0, None, 0)`n"
    $zipPath = Join-Path $dir 'bad_archive.pak'
    $z = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        $e = $z.CreateEntry('Scripts/probe.lua'); $w = New-Object IO.StreamWriter($e.Open())
        $w.Write("-- reads the keyboard`nlocal s = $ak(16)`n"); $w.Dispose()
    } finally { $z.Dispose() }

    $cases = @(
        @{ f = 'bad_import.exe';   want = 'binary'; fail = $true;  what = 'a native exe importing the asynchronous key-state API' }
        @{ f = 'bad_delay.dll';    want = 'binary'; fail = $true;  what = 'a native dll delay-loading the keyboard-state API' }
        @{ f = 'bad_managed.dll';  want = 'binary'; fail = $true;  what = 'a managed assembly declaring the hook API (P/Invoke)' }
        @{ f = 'clean.exe';        want = 'binary'; fail = $false; what = 'a clean native exe' }
        @{ f = 'bad_raw.py';       want = 'script'; fail = $true;  what = 'a Python script registering raw input' }
        @{ f = 'bad_clip.ps1';     want = 'script'; fail = $true;  what = 'a PowerShell script reading the clipboard' }
        @{ f = 'clean_send.py';    want = 'script'; fail = $false; what = 'a send-only Python script' }
        @{ f = 'bad_archive.pak';  want = 'script'; fail = $true;  what = 'a pak holding a Lua script with the key-state API' }
    )
    $ok = $true
    foreach ($c in $cases) {
        $before = $results.Count
        Invoke-OneFile (Join-Path $dir $c.f) $c.want
        $mine = @($results | Select-Object -Skip $before)
        $results.RemoveRange($before, $results.Count - $before)
        $hit = @($mine | Where-Object { @($_.hits).Count -gt 0 }).Count -gt 0
        $good = ($hit -eq $c.fail)
        if (-not $good) { $ok = $false }
        $detail = (@($mine | ForEach-Object { $_.hits }) | Select-Object -First 2) -join ' | '
        [void]$selfLines.Add(('SELFTEST {0}  {1}: {2} -> {3}{4}' -f $(if ($good) { 'ok  ' } else { 'FAIL' }), $c.f, $c.what,
            $(if ($hit) { 'caught' } else { 'clean' }), $(if ($detail) { " ($detail)" } else { '' })))
    }
    # the counters belong to the real scan
    foreach ($k in @($counts.Keys)) { $counts[$k] = 0 }
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    $script:selfVerdict = $ok
}

# ---- main -------------------------------------------------------------------------------------------------------------
Write-Output ('no-input-capture gate (WO-203, the keylogger rule) -- {0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
$selfOk = $true
$selfLines = New-Object System.Collections.ArrayList
if ($SelfTest) {
    $script:selfVerdict = $false
    Invoke-SelfTest | Out-Null
    $selfOk = ($script:selfVerdict -eq $true) -and ($selfLines.Count -eq 8)
    $selfLines | ForEach-Object { Write-Output $_ }
    Write-Output ('SELFTEST {0}' -f $(if ($selfOk) { 'PASS: every planted capture was caught, the clean files passed' } else { 'FAILED: the gate cannot be trusted' }))
}

if ($Repo) {
    foreach ($f in (Get-RepoFiles)) { Invoke-OneFile $f 'script' }
    foreach ($tools in (Get-ToolDirs)) { $script:toolRoots += (Get-Rel $tools); Invoke-Root $tools 'any' }
}
# powershell -File passes "a,b" as one string: split it
foreach ($b in @($Binaries | ForEach-Object { $_ -split ',' } | Where-Object { $_ })) { Invoke-Root $b.Trim() 'binary' }
foreach ($s in @($Scripts | ForEach-Object { $_ -split ',' } | Where-Object { $_ })) { Invoke-Root $s.Trim() 'any' }

foreach ($k in $ExemptPaths.Keys) { Write-Output ("EXEMPT-RULE {0}: {1}" -f $k, $ExemptPaths[$k]) }
$bad = 0
foreach ($r in $results) {
    $hits = @($r.hits)
    if ($hits.Count -gt 0 -and -not $r.exempt) { $bad++ }
    $inTools = $false
    foreach ($tr in $script:toolRoots) { if ($r.name -like "$tr/*") { $inTools = $true } }
    $show = $ListAll -or $r.kind -eq 'binary' -or $hits.Count -gt 0 -or $inTools
    if (-not $show) { continue }
    $tag = if ($hits.Count -eq 0) { 'OK    ' } elseif ($r.exempt) { 'EXEMPT' } else { 'HIT   ' }
    Write-Output ('{0} {1} {2}{3}' -f $tag, $r.kind.PadRight(6), $r.name, $(if ($r.note) { "  [$($r.note)]" } else { '' }))
    foreach ($h in $hits) { Write-Output ('         - {0}{1}' -f $h, $(if ($r.exempt) { "  (exempt: $($r.exempt))" } else { '' })) }
}
Write-Output ('checked: {0} binaries ({1} imports, {2} delay-load imports parsed), {3} scripts, {4} archives ({5} script entries)' -f `
    $counts.binaries, $counts.peImports, $counts.peDelay, $counts.scripts, $counts.archives, $counts.archiveEntries)
if (-not $selfOk) { Write-Output 'RESULT: FAIL (the self-test failed)'; exit 2 }
if ($bad -gt 0) { Write-Output ("RESULT: FAIL -- {0} file(s) can read input" -f $bad); exit 1 }
Write-Output 'RESULT: PASS -- nothing checked can read what a person types or clicks'
exit 0
