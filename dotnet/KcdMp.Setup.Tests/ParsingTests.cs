// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Setup.Tests;

public class VdfTests
{
    [Fact]
    public void LibraryFoldersPathsAreUnescaped()
    {
        var text = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n" +
                   "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"1771300\"\t\t\"96422090071\"\n\t\t}\n\t}\n}\n";
        var paths = Vdf.Pairs(text).Where(p => p.Key == "path").Select(p => p.Value).ToList();
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, paths);
        Assert.Equal(3, Vdf.Pairs(text).First(p => p.Key == "1771300").Depth);
    }

    [Fact]
    public void TruncatedFileKeepsWhatCameBefore()
    {
        var pairs = Vdf.Pairs("\"AppState\"\n{\n\t\"appid\"\t\"2429020\"\n\t\"installdir\"\t\"KCD2");
        Assert.Equal("2429020", Vdf.Value(pairs, "appid"));
        Assert.Null(Vdf.Value(pairs, "installdir"));
    }

    [Fact]
    public void GarbageDoesNotThrow()
    {
        // Whatever comes out of garbage is harmless: a "path" that is not a folder is skipped
        // (DetectionTests.MalformedLibraryFileDegradesToTheRoot).
        Vdf.Pairs("{{{ this is not a vdf \" \" \"path\" oops");
        Vdf.Pairs("\"unterminated");
        Assert.Empty(Vdf.Pairs(""));
    }

    [Fact]
    public void CommentsAndQuotesInsideValues()
    {
        var pairs = Vdf.Pairs("// header\n\"name\" \"a \\\"quoted\\\" name\" // trailing\n");
        Assert.Equal("a \"quoted\" name", Vdf.Value(pairs, "NAME"));
    }
}

public class AppManifestTests
{
    // The shape of the maintainer's real appmanifest_2429020.acf, with the account id replaced.
    private const string Real = "\"AppState\"\n{\n\t\"appid\"\t\t\"2429020\"\n\t\"universe\"\t\t\"1\"\n\t\"name\"\t\t\"Kingdom Come: Deliverance II Modding tools\"\n" +
        "\t\"StateFlags\"\t\t\"4\"\n\t\"installdir\"\t\t\"KCD2Mod\"\n\t\"SizeOnDisk\"\t\t\"16570292908\"\n\t\"LastOwner\"\t\t\"76561190000000001\"\n" +
        "\t\"BytesToDownload\"\t\t\"8850914320\"\n\t\"BytesDownloaded\"\t\t\"8850914320\"\n\t\"BytesToStage\"\t\t\"16570292908\"\n\t\"BytesStaged\"\t\t\"16570292908\"\n" +
        "\t\"InstalledDepots\"\n\t{\n\t\t\"2429021\"\n\t\t{\n\t\t\t\"manifest\"\t\t\"2246809240466786225\"\n\t\t\t\"size\"\t\t\"16570292908\"\n\t\t}\n\t}\n}\n";

    [Fact]
    public void InstalledManifestReadsAsInstalledAndIdle()
    {
        var m = AppManifest.Parse(@"D:\SteamLibrary", SteamApp.ModdingTools, Real)!;
        Assert.Equal("KCD2Mod", m.InstallDir);
        Assert.Equal(@"D:\SteamLibrary\steamapps\common\KCD2Mod", m.InstallPath);
        Assert.True(m.FullyInstalled);
        Assert.False(m.Busy);
    }

    [Fact]
    public void DownloadInProgressShowsProgressFromTheCounters()
    {
        var text = Real.Replace("\"StateFlags\"\t\t\"4\"", "\"StateFlags\"\t\t\"1026\"")
                       .Replace("\"BytesDownloaded\"\t\t\"8850914320\"", "\"BytesDownloaded\"\t\t\"4425457160\"")
                       .Replace("\"BytesStaged\"\t\t\"16570292908\"", "\"BytesStaged\"\t\t\"0\"");
        var m = AppManifest.Parse("L", SteamApp.ModdingTools, text)!;
        Assert.True(m.Busy);
        Assert.False(m.FullyInstalled);
        Assert.Equal(0.45, m.Progress!.Value, 2);
    }

    [Fact]
    public void ManifestWithoutInstallDirIsNotAManifest() =>
        Assert.Null(AppManifest.Parse("L", 1, "\"AppState\" { \"appid\" \"1\" }"));

    [Fact]
    public void TheAccountIdIsNeverRead()
    {
        // LastOwner is the player's SteamID: no property may carry it anywhere.
        var m = AppManifest.Parse("L", SteamApp.ModdingTools, Real)!;
        Assert.DoesNotContain("76561190000000001", m.ToString());
        Assert.DoesNotContain(typeof(AppManifest).GetProperties(), p => p.Name.Contains("Owner"));
    }
}

public class RedactTests
{
    [Fact]
    public void ToolOutputLosesSteamIdUserAndProfile()
    {
        var s = Redact.Text(
            "SteamInternal_SetMinidumpSteamID:  Caching Steam ID:  76561190000000001 [API loaded no]\n" +
            @"Game installed in:   C:\Users\Alex\Games\KingdomComeDeliverance2" + "\n" +
            "user Alex on ALEX-DESKTOP, [U:1:12345], steamid:9988776655",
            userName: "Alex", machineName: "ALEX-DESKTOP", profile: @"C:\Users\Alex");
        Assert.DoesNotContain("76561190000000001", s);
        Assert.DoesNotContain("Alex", s, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("12345", s);
        Assert.DoesNotContain("9988776655", s);
        Assert.Contains(@"%USERPROFILE%\Games\KingdomComeDeliverance2", s);
        Assert.Contains("<steam-id>", s);
        Assert.Contains("<pc>", s);
    }

    [Fact]
    public void OtherProfilesAndForwardSlashesToo()
    {
        var s = Redact.Text(@"c:/users/someone/AppData and D:\Users\other\x", null, null, null);
        Assert.DoesNotContain("someone", s);
        Assert.DoesNotContain("other", s);
    }

    [Fact]
    public void SteamLibraryPathsStayReadable()
    {
        var s = Redact.Text(@"Modding tools installed in:   D:\SteamLibrary\steamapps\common\KCD2Mod", "Alex", "PC1", @"C:\Users\Alex");
        Assert.Equal(@"Modding tools installed in:   D:\SteamLibrary\steamapps\common\KCD2Mod", s);
    }

    [Fact]
    public void ShortNamesAreNotSprayedOverText() =>
        Assert.Equal("Data paks", Redact.Text("Data paks", "Da", null, null));
}
