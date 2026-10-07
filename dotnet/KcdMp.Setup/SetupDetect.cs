// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Setup;

/// <summary>Steam's install folder and every library it knows about.</summary>
public sealed record SteamInstall(string? Root, IReadOnlyList<string> Libraries)
{
    public bool Found => Root is not null;

    /// <summary>
    /// <paramref name="rootOverride"/> replaces the registry lookup (Setup's
    /// /STEAMROOT, the tests' fake libraries); a non-empty override that does
    /// not exist means "no Steam", so both "not installed" pages stay reachable
    /// from a fixture. A missing or malformed libraryfolders.vdf degrades to
    /// "just the root"; a library on a drive that is not connected is skipped.
    /// </summary>
    public static SteamInstall Locate(ISetupHost host, string? rootOverride)
    {
        string? root;
        if (rootOverride is not null)
        {
            var p = SetupPaths.Normalize(rootOverride);
            root = p.Length > 0 && Directory.Exists(p) ? p : null;
        }
        else root = host.SteamRootFromRegistry();

        if (root is null) return new SteamInstall(null, Array.Empty<string>());

        var libs = new List<string> { root };
        try
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (var pair in Vdf.Pairs(File.ReadAllText(vdf)))
                {
                    if (!string.Equals(pair.Key, "path", StringComparison.OrdinalIgnoreCase)) continue;
                    var lib = SetupPaths.Normalize(pair.Value);
                    if (lib.Length == 0 || libs.Any(l => SetupPaths.SamePath(l, lib))) continue;
                    if (Directory.Exists(lib)) libs.Add(lib);
                }
            }
        }
        catch { /* unreadable vdf: the root alone */ }
        return new SteamInstall(root, libs);
    }

    /// <summary>The first library holding a manifest for the app.</summary>
    public AppManifest? Manifest(uint appId)
    {
        foreach (var lib in Libraries)
        {
            var m = AppManifest.TryRead(lib, appId);
            if (m is not null) return m;
        }
        return null;
    }
}

/// <summary>Telling the Modding Tools build from retail, and finding install roots.</summary>
public static class GameLocator
{
    public const string ExeName = "KingdomCome.exe";
    public const string KnownMtConfig = "Win64ReleaseSteamLTO_DLL";

    /// <summary>
    /// Both builds ship KingdomCome.exe and WHGame.dll, so neither name tells
    /// them apart. The Modding Tools build links its engine modules separately
    /// (45 DLLs beside the exe) where retail is monolithic; Framework.dll and
    /// CrySystem.dll are the two the plugin needs (the IAT hook and the rttr
    /// reflection ABI). The same test the installer and the launcher used
    /// before WO-150 -- now the only copy of it.
    /// </summary>
    public static bool IsModdingToolsBuild(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        try
        {
            if (!File.Exists(exePath)) return false;
            var dir = Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? "";
            return File.Exists(Path.Combine(dir, "Framework.dll")) && File.Exists(Path.Combine(dir, "CrySystem.dll"));
        }
        catch { return false; }
    }

    /// <summary>
    /// &lt;root&gt;\Bin\&lt;config&gt;\KingdomCome.exe -> &lt;root&gt;. Found by
    /// layout (the folder above "Bin"), falling back to the first ancestor that
    /// holds an Engine folder. Not by Data: a fresh Modding Tools install has
    /// no game data until its workspace is linked, and the old installer's
    /// Data+Engine test could not see an unlinked install for that reason.
    /// </summary>
    public static string? InstallRootOf(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(exePath));
            for (int i = 0; i < 4 && !string.IsNullOrEmpty(dir); i++)
            {
                if (string.Equals(Path.GetFileName(dir), "Bin", StringComparison.OrdinalIgnoreCase))
                    return Path.GetDirectoryName(dir);
                dir = Path.GetDirectoryName(dir);
            }
            dir = Path.GetDirectoryName(Path.GetFullPath(exePath));
            for (int i = 0; i < 4 && !string.IsNullOrEmpty(dir); i++)
            {
                if (Directory.Exists(Path.Combine(dir, "Engine"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { }
        return null;
    }

    /// <summary>The Modding Tools exe under an install folder: the known config first, then any Bin\* that passes.</summary>
    public static string? FindModdingToolsExe(string installPath)
    {
        try
        {
            var known = Path.Combine(installPath, "Bin", KnownMtConfig, ExeName);
            if (IsModdingToolsBuild(known)) return known;
            var bin = Path.Combine(installPath, "Bin");
            if (!Directory.Exists(bin)) return null;
            foreach (var dir in Directory.EnumerateDirectories(bin))
            {
                var exe = Path.Combine(dir, ExeName);
                if (IsModdingToolsBuild(exe)) return exe;
            }
        }
        catch { }
        return null;
    }

    /// <summary>The game's own data is there: at least one pak in Data.</summary>
    public static bool GameFilesPresent(string? gameRoot)
    {
        if (string.IsNullOrEmpty(gameRoot)) return false;
        try
        {
            var data = Path.Combine(gameRoot, "Data");
            return Directory.Exists(data) && Directory.EnumerateFiles(data, "*.pak").Any();
        }
        catch { return false; }
    }
}

public sealed class SetupProbeOptions
{
    /// <summary>Replaces the registry's Steam path (null = use the registry). See <see cref="SteamInstall.Locate"/>.</summary>
    public string? SteamRootOverride { get; init; }

    /// <summary>A Modding Tools exe the player chose (the launcher's GamePath, Setup's Browse). Used when it passes the build test.</summary>
    public string? ExplicitModdingToolsExe { get; init; }

    /// <summary>The installed launcher's folder (holds install-manifest.txt and the staged mod). Null skips the mod check.</summary>
    public string? AppDir { get; init; }

    /// <summary>The kdcmp folder in the Modding Tools was put there by us (HKCU\Software\KCDMP\ModsPath names it).</summary>
    public Func<string, bool>? OwnsModFolder { get; init; }

    public bool VerifyWorkspace { get; init; } = true;
}

/// <summary>Everything the checklist is decided from, read in one pass (well under a second: manifests, link identities, file sizes).</summary>
public sealed class SetupSnapshot
{
    public required SteamInstall Steam { get; init; }
    public bool SteamRunning { get; init; }
    public bool? SteamSignedIn { get; init; }
    public bool GameProcessRunning { get; init; }

    public AppManifest? GameManifest { get; init; }
    /// <summary>The game's install folder, set only when its data is actually on disk.</summary>
    public string? GameRoot { get; init; }

    public AppManifest? ModdingToolsManifest { get; init; }
    public string? ModdingToolsExe { get; init; }
    public string? ModdingToolsRoot { get; init; }

    /// <summary>The volume the Modding Tools would be downloaded to by default (the game's library, else Steam's own), and its free space.</summary>
    public string? DownloadVolume { get; init; }
    public long? DownloadVolumeFreeBytes { get; init; }

    public WorkspaceReport? Workspace { get; init; }
    public ModReport? Mod { get; init; }
    /// <summary>WO-157: Microsoft's Visual C++ 2013 runtime (x64) is installed.</summary>
    public bool VcRuntime2013 { get; init; } = true;
    /// <summary>WO-157: its redistributable in a Steam library (Steamworks Shared), or null.</summary>
    public string? VcRedist2013 { get; init; }

    public bool GameInstalled => GameRoot is not null;
    public bool ModdingToolsInstalled => ModdingToolsRoot is not null;
    public bool WorkspaceLinked => Workspace is { Complete: true };
    public string? ModsTarget => ModdingToolsRoot is null ? null : ModInstall.TargetDir(ModdingToolsRoot);
}

public static class SetupProbe
{
    public static SetupSnapshot Take(ISetupHost host, SetupProbeOptions options)
    {
        var steam = SteamInstall.Locate(host, options.SteamRootOverride);

        var gameManifest = steam.Manifest(SteamApp.Game);
        string? gameRoot = null;
        if (gameManifest is not null && GameLocator.GameFilesPresent(gameManifest.InstallPath))
            gameRoot = gameManifest.InstallPath;

        var mtManifest = steam.Manifest(SteamApp.ModdingTools);
        string? mtExe = null;
        if (GameLocator.IsModdingToolsBuild(options.ExplicitModdingToolsExe) &&
            GameLocator.InstallRootOf(options.ExplicitModdingToolsExe) is not null)
            mtExe = Path.GetFullPath(options.ExplicitModdingToolsExe!);
        else
        {
            foreach (var lib in steam.Libraries)
            {
                var m = AppManifest.TryRead(lib, SteamApp.ModdingTools);
                if (m is null) continue;
                mtExe = GameLocator.FindModdingToolsExe(m.InstallPath);
                if (mtExe is not null) break;
            }
        }
        string? mtRoot = mtExe is null ? null : GameLocator.InstallRootOf(mtExe);
        if (mtRoot is null) mtExe = null;

        string? volume = gameManifest?.LibraryRoot ?? steam.Root;
        long? free = volume is null ? null : host.FreeBytes(volume);

        WorkspaceReport? workspace = null;
        if (options.VerifyWorkspace && gameRoot is not null && mtRoot is not null)
            workspace = Workspace.Verify(gameRoot, mtRoot);

        ModReport? mod = null;
        if (options.AppDir is not null && mtRoot is not null)
        {
            var target = ModInstall.TargetDir(mtRoot);
            mod = ModInstall.Check(options.AppDir, mtRoot, options.OwnsModFolder?.Invoke(target) ?? false);
        }

        return new SetupSnapshot
        {
            Steam = steam,
            SteamRunning = steam.Found && host.IsProcessRunning("steam"),
            SteamSignedIn = host.SteamSignedIn(),
            GameProcessRunning = host.IsProcessRunning("KingdomCome"),
            GameManifest = gameManifest,
            GameRoot = gameRoot,
            ModdingToolsManifest = mtManifest,
            ModdingToolsExe = mtExe,
            ModdingToolsRoot = mtRoot,
            DownloadVolume = volume is null ? null : Path.GetPathRoot(volume),
            DownloadVolumeFreeBytes = free,
            Workspace = workspace,
            Mod = mod,
            VcRuntime2013 = host.HasVcRuntime2013(),
            VcRedist2013 = VcRuntime2013.FindRedist(new[] { steam.Root }.Concat(steam.Libraries)),
        };
    }
}
