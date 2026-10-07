// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Security.Cryptography;

namespace KcdMp.Setup.Tests;

/// <summary>
/// A fake Steam install on a scratch folder: real folders, real appmanifests,
/// tiny fake paks laid out the way the real game's are (Data, Localization,
/// Data\Levels\&lt;level&gt;). Everything lives under the test's own output
/// folder in the repo; nothing outside it is touched.
/// </summary>
public sealed class FakeSteam : IDisposable
{
    public string Root { get; }
    public string SteamRoot => Path.Combine(Root, "Steam");
    public string Library => Path.Combine(Root, "Lib");
    public string GameRoot => Path.Combine(Library, "steamapps", "common", "KingdomComeDeliverance2");
    public string MtRoot => Path.Combine(Library, "steamapps", "common", "KCD2Mod");
    public string MtExe => Path.Combine(MtRoot, "Bin", GameLocator.KnownMtConfig, GameLocator.ExeName);
    public string AppDir => Path.Combine(Root, "App");

    public static readonly string[] GamePaks =
    [
        @"Data\Tables.pak", @"Data\Scripts.pak", @"Data\Animations.pak",
        @"Localization\English_xml.pak", @"Localization\english-part0.pak",
        @"Data\Levels\klaster\level.pak", @"Data\Levels\trosecko\terrain.pak", @"Data\Levels\trosecko\svo-part0.pak",
    ];

    public FakeSteam(string name)
    {
        Root = Path.Combine(AppContext.BaseDirectory, "scratch", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(SteamRoot, "steamapps"));
        Directory.CreateDirectory(Path.Combine(Library, "steamapps"));
        File.WriteAllText(Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n" +
            $"\t\"0\"\n\t{{\n\t\t\"path\"\t\t\"{Esc(SteamRoot)}\"\n\t}}\n" +
            $"\t\"1\"\n\t{{\n\t\t\"path\"\t\t\"{Esc(Library)}\"\n\t\t\"apps\"\n\t\t{{\n\t\t\t\"1771300\"\t\t\"1\"\n\t\t}}\n\t}}\n}}\n");
    }

    private static string Esc(string p) => p.Replace("\\", "\\\\");

    public FakeSteam WithGame(long stateFlags = 4)
    {
        WriteManifest(SteamApp.Game, "Kingdom Come: Deliverance II", "KingdomComeDeliverance2", stateFlags);
        var t = new DateTime(2026, 7, 27, 11, 0, 28, DateTimeKind.Utc);
        foreach (var rel in GamePaks)
        {
            var p = Path.Combine(GameRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "pak " + rel);
            File.SetLastWriteTimeUtc(p, t);
        }
        Directory.CreateDirectory(Path.Combine(GameRoot, "Bin", "Win64MasterMasterSteamPGO"));
        File.WriteAllText(Path.Combine(GameRoot, "Bin", "Win64MasterMasterSteamPGO", "KingdomCome.exe"), "");
        return this;
    }

    public FakeSteam WithManifestOnly(uint appId, string installDir, long stateFlags, long toDownload = 0, long downloaded = 0)
    {
        WriteManifest(appId, "x", installDir, stateFlags, toDownload, downloaded);
        return this;
    }

    /// <summary>A fresh Modding Tools install: no Data folder at all until the workspace is linked.</summary>
    public FakeSteam WithModdingTools(long stateFlags = 4, bool withToolFolder = true)
    {
        WriteManifest(SteamApp.ModdingTools, "Kingdom Come: Deliverance II Modding tools", "KCD2Mod", stateFlags);
        var bin = Path.GetDirectoryName(MtExe)!;
        Directory.CreateDirectory(bin);
        foreach (var f in new[] { "KingdomCome.exe", "Framework.dll", "CrySystem.dll", "WHGame.dll" })
            File.WriteAllText(Path.Combine(bin, f), "");
        Directory.CreateDirectory(Path.Combine(MtRoot, "Engine"));
        if (withToolFolder) Directory.CreateDirectory(Path.Combine(MtRoot, "Tools", "ModdingWorkspaceSetup"));
        return this;
    }

    public enum Linking { HardLinks, Copies }

    public FakeSteam Linked(Linking how = Linking.HardLinks)
    {
        foreach (var rel in Workspace.ExpectedFiles(GameRoot))
        {
            var src = Path.Combine(GameRoot, rel);
            var dst = Path.Combine(MtRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            if (how == Linking.Copies) File.Copy(src, dst, overwrite: true);
            else Assert.Equal(0, Native.CreateHardLink(dst, src));
        }
        return this;
    }

    /// <summary>The installed launcher's folder: install-manifest.txt, the verdict, the staged mod.</summary>
    public FakeSteam WithInstall(bool verdictPass = true, bool staged = true)
    {
        Directory.CreateDirectory(AppDir);
        File.WriteAllText(Path.Combine(AppDir, "KCDMP_launcher.exe"), "launcher");
        var modManifest = "mod.manifest contents";
        var modPak = "kdcmp.pak contents";
        if (staged)
        {
            Directory.CreateDirectory(Path.Combine(AppDir, "mod", "kdcmp", "Data"));
            File.WriteAllText(Path.Combine(AppDir, "mod", "kdcmp", "mod.manifest"), modManifest);
            File.WriteAllText(Path.Combine(AppDir, "mod", "kdcmp", "Data", "kdcmp.pak"), modPak);
        }
        var lines = new List<string> { "# KCDMP install manifest v2" };
        lines.Add($"APP|KCDMP_launcher.exe|{"launcher".Length}|{Sha("launcher")}");
        if (staged)
        {
            lines.Add($"APP|mod\\kdcmp\\mod.manifest|{modManifest.Length}|{Sha(modManifest)}");
            lines.Add($"APP|mod\\kdcmp\\Data\\kdcmp.pak|{modPak.Length}|{Sha(modPak)}");
        }
        lines.Add($"MOD|Data\\kdcmp.pak|{modPak.Length}|{Sha(modPak)}");
        lines.Add($"MOD|mod.manifest|{modManifest.Length}|{Sha(modManifest)}");
        File.WriteAllLines(Path.Combine(AppDir, ModInstall.ManifestName), lines);
        File.WriteAllText(Path.Combine(AppDir, ModInstall.VerdictName), verdictPass ? "PASS  4 component(s) verified\n" : "FAIL  1 component(s)\n");
        return this;
    }

    public FakeSteam WithModPlaced()
    {
        Assert.True(ModInstall.Place(AppDir, MtRoot).Ok);
        return this;
    }

    private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)));

    private void WriteManifest(uint appId, string name, string installDir, long flags, long toDownload = 0, long downloaded = 0)
    {
        File.WriteAllText(AppManifest.PathFor(Library, appId),
            $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{appId}\"\n\t\"name\"\t\t\"{name}\"\n\t\"StateFlags\"\t\t\"{flags}\"\n" +
            $"\t\"installdir\"\t\t\"{installDir}\"\n\t\"LastOwner\"\t\t\"76561190000000001\"\n" +
            $"\t\"BytesToDownload\"\t\t\"{toDownload}\"\n\t\"BytesDownloaded\"\t\t\"{downloaded}\"\n}}\n");
    }

    public SetupSnapshot Snapshot(FakeHost? host = null, bool ownsMod = true, string? explicitExe = null) =>
        SetupProbe.Take(host ?? new FakeHost(), new SetupProbeOptions
        {
            SteamRootOverride = SteamRoot,
            AppDir = Directory.Exists(AppDir) ? AppDir : null,
            OwnsModFolder = _ => ownsMod,
            ExplicitModdingToolsExe = explicitExe,
        });

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}

public sealed class FakeHost : ISetupHost
{
    public bool SteamRunning { get; set; } = true;
    public bool GameRunning { get; set; }
    public bool? SignedIn { get; set; }
    public long? Free { get; set; } = 100L * 1024 * 1024 * 1024;
    public bool DotNet6 { get; set; } = true;
    public bool VcRuntime { get; set; } = true;   // WO-157

    public string? SteamRootFromRegistry() => null;
    public bool IsProcessRunning(string name) =>
        name.Equals("steam", StringComparison.OrdinalIgnoreCase) ? SteamRunning :
        name.Equals("KingdomCome", StringComparison.OrdinalIgnoreCase) && GameRunning;
    public bool? SteamSignedIn() => SignedIn;
    public long? FreeBytes(string path) => Free;
    public bool HasDotNet6Runtime() => DotNet6;
    public bool HasVcRuntime2013() => VcRuntime;
}
