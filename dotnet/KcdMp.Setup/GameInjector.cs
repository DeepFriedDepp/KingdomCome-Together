// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace KcdMp.Setup;

/// <summary>WO-157: how a load of KCDMP.dll into the game ended.</summary>
public enum InjectOutcome
{
    Injected,
    /// <summary>The process is gone, or is not the game the launcher started (its image path differs).</summary>
    WrongProcess,
    /// <summary>KCDMP.dll is not there (or unreadable).</summary>
    DllMissing,
    /// <summary>KCDMP.dll is there but is not the file Setup installed (its sha256 differs from the install manifest).</summary>
    DllNotShipped,
    /// <summary>Windows refused to open the game process (another user, or a security product).</summary>
    OpenFailed,
    /// <summary>The target is a 32-bit process.</summary>
    Wow64,
    /// <summary>Writing the DLL's path into the game failed.</summary>
    WriteFailed,
    /// <summary>The loader thread could not be started in the game.</summary>
    ThreadFailed,
    /// <summary>The game's LoadLibrary returned null (a missing dependency, or the file was blocked as it loaded).</summary>
    LoadFailed,
}

/// <summary>WO-157: the result, with one log line and one plain message.</summary>
public sealed record InjectResult(InjectOutcome Outcome, int Win32Error, string Detail)
{
    public bool Ok => Outcome == InjectOutcome.Injected;

    public string LogLine => $"MP-INJECT {Outcome.ToString().ToLowerInvariant()} err={Win32Error} {Detail}";

    /// <summary>What the player reads (no paths beyond the file names).</summary>
    public string Message => Outcome switch
    {
        InjectOutcome.Injected => "The multiplayer plugin is loaded into the game.",
        InjectOutcome.WrongProcess => "The game the launcher started is not running any more (or another program took its place). Close the game and click Launch again.",
        InjectOutcome.DllMissing => "KCDMP.dll, the mod's plugin, is not in the install folder. Windows Security or another antivirus may have removed it: check Protection history, restore and allow it, or run Setup again.",
        InjectOutcome.DllNotShipped => "KCDMP.dll in the install folder is not the file Setup installed, so the launcher did not load it. Run Setup again to put the right file back.",
        InjectOutcome.OpenFailed => "Windows did not let the launcher reach the game (access denied). Start the launcher as the same Windows user as the game, and check that no security program blocks it.",
        InjectOutcome.Wow64 => "The game process is 32-bit; the mod's plugin is 64-bit. Check the game path in Settings.",
        InjectOutcome.LoadFailed => "The game could not load the mod's plugin (KCDMP.dll). An antivirus may have blocked the file as it loaded, or a file it needs is missing. Run Setup again; if it keeps failing, send the logs with Report a bug.",
        _ => "The multiplayer plugin couldn't be loaded into the game. Close the game and try again.",
    };
}

/// <summary>
/// WO-157: loads KCDMP.dll into the game from the launcher's own process -- the steps the separate
/// KCDMP_LauncherInjector.exe took (OpenProcess, VirtualAllocEx, WriteProcessMemory, CreateRemoteThread
/// on LoadLibrary), with no extra program for an antivirus or Smart App Control to remove or refuse
/// ("A MOD FILE WAS REMOVED: KCDMP_LauncherInjector.exe", reported constantly on fresh installs).
/// Checks first, failing closed: the process is the game the launcher started (its image path), the
/// DLL is there and, when Setup's manifest lists it, is the shipped file by sha256; x64 only.
/// KCD2 has no anti-cheat (docs/NATIVE-PLUGIN-findings.md).
/// </summary>
public static partial class GameInjector
{
    /// <summary>
    /// <paramref name="expectedGameExe"/>: the game the launcher started (null: not checked).
    /// <paramref name="expectedSha256"/>: the install manifest's hash for KCDMP.dll (null: a development
    /// build with no manifest; the file is not compared).
    /// </summary>
    public static InjectResult Inject(int pid, string dllPath, string? expectedGameExe, string? expectedSha256, int waitMs = 15_000)
    {
        string full;
        try { full = Path.GetFullPath(dllPath); }
        catch (Exception ex) { return new(InjectOutcome.DllMissing, 0, $"path={ex.GetType().Name}"); }
        string name = Path.GetFileName(full);
        if (!File.Exists(full)) return new(InjectOutcome.DllMissing, 2, $"file={name}");
        if (expectedSha256 is not null)
        {
            string got;
            try { got = Sha256Of(full); }
            catch (Exception ex) { return new(InjectOutcome.DllMissing, 0, $"file={name} unreadable={ex.GetType().Name}"); }
            if (!string.Equals(got, expectedSha256, StringComparison.OrdinalIgnoreCase))
                return new(InjectOutcome.DllNotShipped, 0, $"file={name} sha256={Short(got)} expected={Short(expectedSha256)}");
        }

        IntPtr process = OpenProcess(ProcessCreateThread | ProcessQueryInformation | ProcessQueryLimited | ProcessVmOperation | ProcessVmWrite | ProcessVmRead, false, (uint)pid);
        if (process == IntPtr.Zero)
        {
            int e = Marshal.GetLastPInvokeError();
            // ERROR_INVALID_PARAMETER: no such process (it exited).
            return e == 87 ? new(InjectOutcome.WrongProcess, e, $"pid={pid} gone") : new(InjectOutcome.OpenFailed, e, $"pid={pid}");
        }
        try
        {
            if (GetExitCodeProcess(process, out uint code) && code != StillActive)
                return new(InjectOutcome.WrongProcess, 0, $"pid={pid} exited");
            if (expectedGameExe is not null)
            {
                string? image = ImagePathOf(process);
                if (image is null || !SameExe(image, expectedGameExe))
                    return new(InjectOutcome.WrongProcess, 0, $"pid={pid} image={(image is null ? "?" : Path.GetFileName(image))}");
            }
            if (IsWow64Process(process, out int wow) && wow != 0) return new(InjectOutcome.Wow64, 0, $"pid={pid}");

            byte[] path = Encoding.Unicode.GetBytes(full + "\0");
            IntPtr remote = VirtualAllocEx(process, IntPtr.Zero, (nuint)path.Length, MemCommit | MemReserve, PageReadWrite);
            if (remote == IntPtr.Zero) return new(InjectOutcome.WriteFailed, Marshal.GetLastPInvokeError(), "VirtualAllocEx");
            try
            {
                if (!WriteProcessMemory(process, remote, path, (nuint)path.Length, out _))
                    return new(InjectOutcome.WriteFailed, Marshal.GetLastPInvokeError(), "WriteProcessMemory");
                // kernel32 is mapped at the same base in every x64 process of a boot: our LoadLibraryW is theirs.
                IntPtr loader = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
                if (loader == IntPtr.Zero) return new(InjectOutcome.ThreadFailed, Marshal.GetLastPInvokeError(), "GetProcAddress");
                IntPtr thread = CreateRemoteThread(process, IntPtr.Zero, 0, loader, remote, 0, out _);
                if (thread == IntPtr.Zero) return new(InjectOutcome.ThreadFailed, Marshal.GetLastPInvokeError(), "CreateRemoteThread");
                try
                {
                    WaitForSingleObject(thread, (uint)waitMs);
                    // LoadLibraryW's HMODULE truncated to 32 bits: zero = the load failed inside the game.
                    if (!GetExitCodeThread(thread, out uint module) || module == 0 || module == StillActive)
                        return new(InjectOutcome.LoadFailed, 0, $"pid={pid} file={name} module=0x{module:x}");
                    return new(InjectOutcome.Injected, 0, $"pid={pid} file={name} module=0x{module:x}");
                }
                finally { CloseHandle(thread); }
            }
            finally { VirtualFreeEx(process, remote, 0, MemRelease); }
        }
        finally { CloseHandle(process); }
    }

    public static string Sha256Of(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    /// <summary>The same path, or the same file reached another way (a junction, a link, a short name).</summary>
    internal static bool SameExe(string image, string expected)
    {
        try
        {
            if (string.Equals(Path.GetFullPath(image), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase)) return true;
            return Native.Identity(image) is { } a && Native.Identity(expected) is { } b && Native.SameFile(a, b);
        }
        catch (Exception) { return false; }
    }

    private static string Short(string h) => h.Length > 12 ? h[..12] : h;

    private static string? ImagePathOf(IntPtr process)
    {
        var buf = new char[1024];
        uint len = (uint)buf.Length;
        return QueryFullProcessImageNameW(process, 0, buf, ref len) ? new string(buf, 0, (int)len) : null;
    }

    private const uint ProcessCreateThread = 0x0002, ProcessVmOperation = 0x0008, ProcessVmRead = 0x0010, ProcessVmWrite = 0x0020;
    private const uint ProcessQueryInformation = 0x0400, ProcessQueryLimited = 0x1000;
    private const uint MemCommit = 0x1000, MemReserve = 0x2000, MemRelease = 0x8000, PageReadWrite = 0x04;
    private const uint StillActive = 259;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(IntPtr process, out uint code);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWow64Process(IntPtr process, out int wow64);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(IntPtr process, uint flags, [Out] char[] name, ref uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr VirtualAllocEx(IntPtr process, IntPtr address, nuint size, uint type, uint protect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VirtualFreeEx(IntPtr process, IntPtr address, nuint size, uint type);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, nuint size, out nuint written);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string name);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetProcAddress(IntPtr module, string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr CreateRemoteThread(IntPtr process, IntPtr security, nuint stack, IntPtr start, IntPtr param, uint flags, out uint threadId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(IntPtr handle, uint ms);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeThread(IntPtr thread, out uint code);
}
