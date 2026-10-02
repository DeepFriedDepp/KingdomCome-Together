// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Setup;

/// <summary>The two Steam apps setup cares about, read off real appmanifests (docs/WO-150A-workspace-map.md).</summary>
public static class SteamApp
{
    /// <summary>Kingdom Come: Deliverance II (installdir "KingdomComeDeliverance2").</summary>
    public const uint Game = 1771300;

    /// <summary>Kingdom Come: Deliverance II Modding tools (installdir "KCD2Mod"). The mod runs on this build.</summary>
    public const uint ModdingTools = 2429020;

    /// <summary>
    /// The Modding Tools' own size on disk, from a real appmanifest (SizeOnDisk
    /// 16,570,292,908 bytes, build 22816715, 2026-10-02), rounded up. The
    /// workspace adds nothing when it is linked: links take no space.
    /// </summary>
    public const long ModdingToolsBytes = 17L * 1024 * 1024 * 1024;

    /// <summary>Head-room on top: Steam stages a download beside the install before committing it.</summary>
    public const long ModdingToolsMarginBytes = 3L * 1024 * 1024 * 1024;
}

/// <summary>
/// One steamapps\appmanifest_&lt;id&gt;.acf. Only the fields setup needs are
/// read. "LastOwner" -- the account's SteamID -- is deliberately never read,
/// so it can never reach a log.
/// </summary>
public sealed record AppManifest(
    uint AppId,
    string LibraryRoot,
    string InstallDir,
    long StateFlags,
    long BytesToDownload,
    long BytesDownloaded,
    long BytesToStage,
    long BytesStaged)
{
    // Steam's EAppState bits, as they appear in "StateFlags".
    public const long FlagUpdateRequired = 2;
    public const long FlagFullyInstalled = 4;
    public const long FlagFilesMissing = 32;
    public const long FlagFilesCorrupt = 128;
    public const long FlagUpdateRunning = 256;
    public const long FlagUpdatePaused = 512;
    public const long FlagUpdateStarted = 1024;
    public const long FlagUninstalling = 2048;
    public const long FlagValidating = 131072;
    public const long FlagAddingFiles = 262144;
    public const long FlagPreallocating = 524288;
    public const long FlagDownloading = 1048576;
    public const long FlagStaging = 2097152;
    public const long FlagCommitting = 4194304;

    private const long BusyMask = FlagUpdateRunning | FlagUpdateStarted | FlagValidating | FlagAddingFiles |
                                  FlagPreallocating | FlagDownloading | FlagStaging | FlagCommitting | FlagUninstalling;

    public string InstallPath => Path.Combine(LibraryRoot, "steamapps", "common", InstallDir);

    /// <summary>Steam says the files are all there (an update may still be pending: the files that are there still run).</summary>
    public bool FullyInstalled => (StateFlags & FlagFullyInstalled) != 0;

    /// <summary>Steam is doing something to this app right now: downloading, staging, verifying.</summary>
    public bool Busy => (StateFlags & BusyMask) != 0;

    public bool Paused => (StateFlags & FlagUpdatePaused) != 0;

    /// <summary>
    /// 0..1 while Steam works on it, from the manifest's own counters: the
    /// download first, then staging (unpacking into place). Null when the
    /// counters say nothing (no download recorded yet).
    /// </summary>
    public double? Progress
    {
        get
        {
            if (BytesToDownload > 0 && BytesDownloaded < BytesToDownload)
                return Math.Clamp((double)BytesDownloaded / BytesToDownload, 0, 1) * 0.9;
            if (BytesToStage > 0)
                return 0.9 + Math.Clamp((double)BytesStaged / BytesToStage, 0, 1) * 0.1;
            if (BytesToDownload > 0)
                return 0.9;
            return null;
        }
    }

    public static string PathFor(string libraryRoot, uint appId) =>
        Path.Combine(libraryRoot, "steamapps", $"appmanifest_{appId}.acf");

    /// <summary>Null when the library has no manifest for the app, or it cannot be read.</summary>
    public static AppManifest? TryRead(string libraryRoot, uint appId)
    {
        string path = PathFor(libraryRoot, appId);
        string text;
        try
        {
            if (!File.Exists(path)) return null;
            // Steam rewrites this file while it downloads; share everything.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            text = reader.ReadToEnd();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        return Parse(libraryRoot, appId, text);
    }

    public static AppManifest? Parse(string libraryRoot, uint appId, string text)
    {
        var pairs = Vdf.Pairs(text);
        string? installDir = Vdf.Value(pairs, "installdir");
        if (string.IsNullOrWhiteSpace(installDir)) return null;
        return new AppManifest(appId, libraryRoot, installDir,
            Num(pairs, "StateFlags"), Num(pairs, "BytesToDownload"), Num(pairs, "BytesDownloaded"),
            Num(pairs, "BytesToStage"), Num(pairs, "BytesStaged"));
    }

    private static long Num(List<Vdf.Pair> pairs, string key) =>
        long.TryParse(Vdf.Value(pairs, key), out long v) ? v : 0;
}
