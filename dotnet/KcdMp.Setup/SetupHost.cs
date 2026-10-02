// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;

namespace KcdMp.Setup;

/// <summary>
/// The parts of the machine setup reads that are not plain files: the
/// registry, the process list, free disk space. Behind an interface so the
/// unit tests can run the whole checklist against a fake Steam library in a
/// scratch folder without the real machine leaking in. The file system itself
/// is not abstracted: the fakes are real folders.
/// </summary>
public interface ISetupHost
{
    /// <summary>Steam's install folder from the registry (HKCU SteamPath, then HKLM InstallPath), or null.</summary>
    string? SteamRootFromRegistry();

    bool IsProcessRunning(string processName);

    /// <summary>
    /// True/false when Steam's registry says who is signed in; null when it
    /// cannot tell. The current client leaves HKCU\...\ActiveProcess\ActiveUser
    /// at 0 while running and signed in (observed on the maintainer's machine,
    /// 2026-10-02), so 0 means "unknown", never "signed out". The value itself
    /// (an account id) is never returned.
    /// </summary>
    bool? SteamSignedIn();

    /// <summary>Free bytes on the volume holding <paramref name="path"/>, or null when it cannot be read.</summary>
    long? FreeBytes(string path);

    /// <summary>The .NET 6 runtime Warhorse's WorkspaceSetup.exe is built against (net6.0, no roll-forward).</summary>
    bool HasDotNet6Runtime();
}

public sealed class RealSetupHost : ISetupHost
{
    public static readonly RealSetupHost Instance = new();

    public string? SteamRootFromRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                if (key?.GetValue("SteamPath") is string sp && Usable(sp, out var p)) return p;
            foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry32, Microsoft.Win32.RegistryView.Registry64 })
            {
                using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view);
                using var key = hklm.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (key?.GetValue("InstallPath") is string ip && Usable(ip, out var p)) return p;
            }
        }
        catch { }
        return null;
    }

    private static bool Usable(string raw, out string path)
    {
        path = SetupPaths.Normalize(raw);
        return path.Length > 0 && Directory.Exists(path);
    }

    public bool IsProcessRunning(string processName)
    {
        try
        {
            var procs = Process.GetProcessesByName(processName);
            foreach (var p in procs) p.Dispose();
            return procs.Length > 0;
        }
        catch { return false; }
    }

    public bool? SteamSignedIn()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            if (key?.GetValue("ActiveUser") is int user && user != 0) return true;
        }
        catch { }
        return null;
    }

    public long? FreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return null;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch { return null; }
    }

    public bool HasDotNet6Runtime()
    {
        foreach (var baseDir in DotNetRoots())
        {
            try
            {
                var shared = Path.Combine(baseDir, "shared", "Microsoft.NETCore.App");
                if (Directory.Exists(shared) &&
                    Directory.EnumerateDirectories(shared, "6.*").Any())
                    return true;
            }
            catch { }
        }
        return false;
    }

    private static IEnumerable<string> DotNetRoots()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(env)) yield return env;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
    }
}

public static class SetupPaths
{
    /// <summary>Backslashes, no trailing separator (Steam writes "c:/program files (x86)/steam").</summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var p = path.Trim().Replace('/', '\\');
        while (p.Length > 3 && p.EndsWith('\\')) p = p[..^1];
        return p;
    }

    public static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }
}
