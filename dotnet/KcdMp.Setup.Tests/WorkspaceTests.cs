// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Setup.Tests;

public class DetectionTests
{
    [Fact]
    public void FindsGameAndModdingToolsInASecondLibrary()
    {
        using var f = new FakeSteam("detect").WithGame().WithModdingTools();
        var s = f.Snapshot();
        Assert.Equal(2, s.Steam.Libraries.Count);
        Assert.Equal(f.GameRoot, s.GameRoot);
        Assert.Equal(f.MtExe, s.ModdingToolsExe);
        Assert.Equal(f.MtRoot, s.ModdingToolsRoot);
        Assert.Equal(Path.Combine(f.MtRoot, "Mods", "kdcmp"), s.ModsTarget);
    }

    [Fact]
    public void AFreshModdingToolsInstallWithoutDataIsStillFound()
    {
        // The pre-WO-150 installer looked for Data+Engine and could not see this case.
        using var f = new FakeSteam("fresh").WithModdingTools();
        Assert.False(Directory.Exists(Path.Combine(f.MtRoot, "Data")));
        Assert.Equal(f.MtRoot, f.Snapshot().ModdingToolsRoot);
    }

    [Fact]
    public void RetailLayoutIsNotTheModdingTools()
    {
        using var f = new FakeSteam("retail").WithGame();
        f.WithManifestOnly(SteamApp.ModdingTools, "KingdomComeDeliverance2", 4);   // a manifest pointing at retail files
        var s = f.Snapshot();
        Assert.NotNull(s.ModdingToolsManifest);
        Assert.Null(s.ModdingToolsRoot);
    }

    [Fact]
    public void AManifestWithoutFilesIsRegisteredButNotInstalled()
    {
        using var f = new FakeSteam("ghost").WithManifestOnly(SteamApp.Game, "KingdomComeDeliverance2", 4);
        var s = f.Snapshot();
        Assert.NotNull(s.GameManifest);
        Assert.False(s.GameInstalled);
    }

    [Fact]
    public void ANonexistentSteamRootMeansNoSteam()
    {
        var s = SetupProbe.Take(new FakeHost(), new SetupProbeOptions { SteamRootOverride = @"Z:\no\such\steam" });
        Assert.False(s.Steam.Found);
        Assert.False(s.SteamRunning);
    }

    [Fact]
    public void MalformedLibraryFileDegradesToTheRoot()
    {
        using var f = new FakeSteam("malformed");
        File.WriteAllText(Path.Combine(f.SteamRoot, "steamapps", "libraryfolders.vdf"), "{{{ not a vdf \" \" \"path\" oops");
        Assert.Single(f.Snapshot().Steam.Libraries);
    }

    [Fact]
    public void AnExplicitModdingToolsExeWinsWhenItPasses()
    {
        using var f = new FakeSteam("explicit").WithModdingTools();
        using var g = new FakeSteam("explicit-other").WithModdingTools();
        var s = f.Snapshot(explicitExe: g.MtExe);
        Assert.Equal(g.MtRoot, s.ModdingToolsRoot);
        // and a retail exe given explicitly is ignored rather than trusted
        var r = f.Snapshot(explicitExe: Path.Combine(f.Root, "nope", "KingdomCome.exe"));
        Assert.Equal(f.MtRoot, r.ModdingToolsRoot);
    }

    [Fact]
    public void InstallRootIsTheFolderAboveBin()
    {
        Assert.Equal(@"D:\Lib\steamapps\common\KCD2Mod",
            GameLocator.InstallRootOf(@"D:\Lib\steamapps\common\KCD2Mod\Bin\Win64ReleaseSteamLTO_DLL\KingdomCome.exe"));
    }
}

public class WorkspaceTests
{
    [Fact]
    public void ExpectedFilesAreTheToolsThreeMirrors()
    {
        using var f = new FakeSteam("expected").WithGame();
        File.WriteAllText(Path.Combine(f.GameRoot, "Data", "notapak.txt"), "x");
        Directory.CreateDirectory(Path.Combine(f.GameRoot, "Data", "Levels", "klaster", "sub"));
        File.WriteAllText(Path.Combine(f.GameRoot, "Data", "Levels", "klaster", "sub", "deep.pak"), "x");   // the tool does not recurse
        var rels = Workspace.ExpectedFiles(f.GameRoot);
        Assert.Equal(FakeSteam.GamePaks.OrderBy(x => x), rels.OrderBy(x => x));
    }

    [Fact]
    public void UnlinkedHardLinkedCopiedAndStaleAreToldApart()
    {
        using var f = new FakeSteam("states").WithGame().WithModdingTools();
        var r0 = Workspace.Verify(f.GameRoot, f.MtRoot);
        Assert.All(r0.Entries, e => Assert.Equal(EntryState.Missing, e.State));
        Assert.False(r0.Complete);

        f.Linked(FakeSteam.Linking.HardLinks);
        var r1 = Workspace.Verify(f.GameRoot, f.MtRoot);
        Assert.True(r1.Complete);
        Assert.All(r1.Entries, e => Assert.Equal(LinkKind.HardLink, e.Kind));
        Assert.All(r1.Entries, e => Assert.Equal(2u, e.LinkCount));

        using var g = new FakeSteam("copies").WithGame().WithModdingTools().Linked(FakeSteam.Linking.Copies);
        var r2 = Workspace.Verify(g.GameRoot, g.MtRoot);
        Assert.True(r2.Complete);
        Assert.Equal($"{FakeSteam.GamePaks.Length} copies", r2.KindSummary());

        // A game update changes the pak: the old copy is now stale.
        var game = Path.Combine(g.GameRoot, @"Data\Tables.pak");
        File.WriteAllText(game, "pak Data\\Tables.pak v2 (longer)");
        var r3 = Workspace.Verify(g.GameRoot, g.MtRoot);
        Assert.Equal(EntryState.Stale, r3.Entries.Single(e => e.Rel == @"Data\Tables.pak").State);
        Assert.False(r3.Complete);
    }

    [Fact]
    public void ABrokenSymlinkIsNotLinked()
    {
        using var f = new FakeSteam("broken").WithGame().WithModdingTools();
        var target = Path.Combine(f.MtRoot, @"Data\Tables.pak");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        int err = Native.CreateFileSymlink(target, Path.Combine(f.Root, "gone.pak"));
        if (err == Native.ERROR_PRIVILEGE_NOT_HELD) return;   // this machine cannot make symlinks unelevated: nothing to check here
        Assert.Equal(0, err);
        Assert.Equal(EntryState.BrokenLink, Workspace.Verify(f.GameRoot, f.MtRoot).Entries.Single(e => e.Rel == @"Data\Tables.pak").State);
    }

    [Fact]
    public void AStaleHardLinkLeftByAnUpdateIsStale()
    {
        using var f = new FakeSteam("stalehard").WithGame().WithModdingTools().Linked();
        // Steam replaces a changed file (new file, same name): the Modding Tools' hard link keeps the old one.
        var game = Path.Combine(f.GameRoot, @"Data\Scripts.pak");
        File.Delete(game);
        File.WriteAllText(game, "pak Data\\Scripts.pak updated");
        var e = Workspace.Verify(f.GameRoot, f.MtRoot).Entries.Single(x => x.Rel == @"Data\Scripts.pak");
        Assert.Equal(EntryState.Stale, e.State);
    }

    [Fact]
    public void VerifyIsFastEnoughForStartup()
    {
        using var f = new FakeSteam("fast").WithGame().WithModdingTools().Linked();
        Workspace.Verify(f.GameRoot, f.MtRoot);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) Workspace.Verify(f.GameRoot, f.MtRoot);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"10 verifies took {sw.ElapsedMilliseconds} ms");
    }
}

public class LinkerTests
{
    [Fact]
    public void SameDriveMakesHardLinksAndLeavesNoTemporaryFiles()
    {
        using var f = new FakeSteam("hard").WithGame().WithModdingTools();
        var run = WorkspaceLinker.Link(Workspace.Verify(f.GameRoot, f.MtRoot));
        Assert.Equal(FakeSteam.GamePaks.Length, run.HardLinks);
        Assert.False(run.NeedsElevation);
        Assert.True(Workspace.Verify(f.GameRoot, f.MtRoot).Complete);
        Assert.Empty(Directory.GetFiles(f.MtRoot, "*" + WorkspaceLinker.TempSuffix, SearchOption.AllDirectories));
    }

    [Fact]
    public void AStaleReadOnlyCopyIsReplacedAndExtraFilesAreLeftAlone()
    {
        using var f = new FakeSteam("replace").WithGame().WithModdingTools().Linked(FakeSteam.Linking.Copies);
        var mt = Path.Combine(f.MtRoot, @"Data\Tables.pak");
        File.WriteAllText(mt, "an old, different copy");
        new FileInfo(mt).IsReadOnly = true;
        var extra = Path.Combine(f.MtRoot, @"Data\SomebodysOwn.pak");
        File.WriteAllText(extra, "not the game's");

        var run = WorkspaceLinker.Link(Workspace.Verify(f.GameRoot, f.MtRoot));
        Assert.Equal(1, run.Linked);
        Assert.True(Workspace.Verify(f.GameRoot, f.MtRoot).Complete);
        Assert.Equal("not the game's", File.ReadAllText(extra));
    }

    [Fact]
    public void DifferentDrivesFallBackToSymlinksAndAskForPermissionWhenRefused()
    {
        using var f = new FakeSteam("crossdrive").WithGame().WithModdingTools();
        var io = new ScriptedPrimitives { HardLinkError = Native.ERROR_NOT_SAME_DEVICE };   // as two drives answer
        var run = WorkspaceLinker.Link(Workspace.Verify(f.GameRoot, f.MtRoot), io);
        Assert.True(run.FellBackToSymlinks);
        Assert.Equal(1, io.HardLinkCalls);   // asked once, not per file
        if (io.RealSymlinkRefused)
        {
            // This machine has no Developer Mode and the tests are not elevated: a real refusal.
            Assert.True(run.NeedsElevation);
            Assert.Equal(1, io.SymlinkCalls);   // the rest wait for the one elevated run
            Assert.All(run.Outcomes, o => Assert.False(o.Ok));
        }
        else
        {
            Assert.Equal(FakeSteam.GamePaks.Length, run.Symlinks);
            Assert.True(Workspace.Verify(f.GameRoot, f.MtRoot).Complete);
        }
    }

    [Fact]
    public void ARefusedSymlinkIsReportedAsNeedingPermission()
    {
        using var f = new FakeSteam("refused").WithGame().WithModdingTools();
        var io = new ScriptedPrimitives { HardLinkError = Native.ERROR_NOT_SAME_DEVICE, SymlinkError = Native.ERROR_PRIVILEGE_NOT_HELD };
        var run = WorkspaceLinker.Link(Workspace.Verify(f.GameRoot, f.MtRoot), io);
        Assert.True(run.NeedsElevation);
        Assert.Equal(0, run.Linked);
        Assert.False(Directory.Exists(Path.Combine(f.MtRoot, "Data")) &&
                     Directory.GetFiles(Path.Combine(f.MtRoot, "Data"), "*", SearchOption.AllDirectories).Length > 0);
    }

    [Fact]
    public void AFileHeldByTheGameIsReportedAsInUse()
    {
        using var f = new FakeSteam("inuse").WithGame().WithModdingTools();
        var io = new ScriptedPrimitives { MoveError = Native.ERROR_SHARING_VIOLATION };
        var run = WorkspaceLinker.Link(Workspace.Verify(f.GameRoot, f.MtRoot), io);
        Assert.True(run.InUse);
        Assert.Empty(Directory.GetFiles(f.MtRoot, "*" + WorkspaceLinker.TempSuffix, SearchOption.AllDirectories));
    }
}

/// <summary>Real Win32 calls, except the ones a test scripts to fail.</summary>
public sealed class ScriptedPrimitives : ILinkPrimitives
{
    public int? HardLinkError { get; init; }
    public int? SymlinkError { get; init; }
    public int? MoveError { get; init; }
    public int HardLinkCalls, SymlinkCalls;
    public bool RealSymlinkRefused;

    public int CreateHardLink(string newFile, string existingFile)
    {
        HardLinkCalls++;
        return HardLinkError ?? Native.CreateHardLink(newFile, existingFile);
    }

    public int CreateSymlink(string link, string target)
    {
        SymlinkCalls++;
        if (SymlinkError is int e) return e;
        int r = Native.CreateFileSymlink(link, target);
        if (r == Native.ERROR_PRIVILEGE_NOT_HELD) RealSymlinkRefused = true;
        return r;
    }

    public int MoveReplace(string from, string to) => MoveError ?? Native.MoveReplace(from, to);
}
