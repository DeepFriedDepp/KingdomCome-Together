// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Setup;

/// <summary>
/// WO-157 (2.3): Microsoft's Visual C++ 2013 runtime (x64). The Modding Tools' trace server
/// (trace_server.run.exe) needs it; without it Windows says "MSVCP120.dll was not found" when the
/// Modding Tools start (a tester's screenshot). Neither the game nor the mod needs it, so its
/// checklist step never holds Host or Join back.
/// </summary>
public static class VcRuntime2013
{
    /// <summary>The file Windows reported missing; it lives in System32 for the x64 runtime.</summary>
    public const string ProbeFile = "msvcp120.dll";

    /// <summary>Microsoft's page for the supported downloads, where Visual Studio 2013 (VC++ 12.0) is listed.</summary>
    public const string DownloadPage = "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist";

    /// <summary>Steam ships the redistributable with many games, in every library it uses for them.</summary>
    public static readonly string[] RedistRelPath =
        ["steamapps", "common", "Steamworks Shared", "_CommonRedist", "vcredist", "2013", "vcredist_x64.exe"];

    /// <summary>Silent install, no reboot; Microsoft's installer asks Windows for permission itself (one UAC prompt).</summary>
    public const string InstallArguments = "/install /quiet /norestart";

    /// <summary>Exit codes that leave the runtime installed: done, done (restart pending), a newer one already there.</summary>
    public static bool InstalledByExitCode(int code) => code is 0 or 3010 or 1638;

    public static bool PresentIn(string systemDirectory)
    {
        try { return File.Exists(Path.Combine(systemDirectory, ProbeFile)); }
        catch (Exception) { return false; }
    }

    /// <summary>The first library (Steam's own folder first) that holds the x64 redistributable, or null.</summary>
    public static string? FindRedist(IEnumerable<string?> libraries)
    {
        foreach (var lib in libraries)
        {
            if (string.IsNullOrWhiteSpace(lib)) continue;
            try
            {
                var p = Path.Combine([lib, .. RedistRelPath]);
                if (File.Exists(p)) return Path.GetFullPath(p);
            }
            catch (Exception) { }
        }
        return null;
    }
}
