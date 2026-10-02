// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;

namespace KcdMp.Setup.Tests;

public class ModInstallTests
{
    [Fact]
    public void ADevelopmentBuildHasNothingToCheck()
    {
        using var f = new FakeSteam("dev").WithModdingTools();
        Directory.CreateDirectory(f.AppDir);
        var r = ModInstall.Check(f.AppDir, f.MtRoot, ownsModFolder: false);
        Assert.True(r.DevelopmentBuild);
        Assert.True(r.Ok);
    }

    [Fact]
    public void HeldBackModIsPlacedFromTheStagedCopyAndThenVerifies()
    {
        using var f = new FakeSteam("place").WithModdingTools().WithInstall();
        var before = ModInstall.Check(f.AppDir, f.MtRoot, ownsModFolder: false);
        Assert.False(before.Placed);
        Assert.True(before.StagedAvailable);
        Assert.False(before.ForeignFolder);   // nothing is there yet
        Assert.True(ModInstall.Place(f.AppDir, f.MtRoot).Ok);
        Assert.True(ModInstall.Check(f.AppDir, f.MtRoot, ownsModFolder: true).Ok);
    }

    [Fact]
    public void PlacingPrunesLooseSourcesButKeepsTheKeysPak()
    {
        using var f = new FakeSteam("prune").WithModdingTools().WithInstall().WithModPlaced();
        var target = ModInstall.TargetDir(f.MtRoot);
        Directory.CreateDirectory(Path.Combine(target, "Data", "Libs", "Tables"));
        File.WriteAllText(Path.Combine(target, "Data", "Libs", "Tables", "loose.xml"), "breaks the game");
        File.WriteAllText(Path.Combine(target, "Data", "kdcmp_keys.pak"), "keys");
        File.WriteAllText(Path.Combine(target, "Data", "kdcmp.pak"), "an older pak");
        Assert.True(ModInstall.Place(f.AppDir, f.MtRoot).Ok);
        Assert.False(Directory.Exists(Path.Combine(target, "Data", "Libs")));
        Assert.Equal("keys", File.ReadAllText(Path.Combine(target, "Data", "kdcmp_keys.pak")));
        Assert.True(ModInstall.Check(f.AppDir, f.MtRoot, true).Ok);
    }

    [Fact]
    public void PruningNeverFollowsAJunction()
    {
        using var f = new FakeSteam("junction").WithModdingTools().WithInstall().WithModPlaced();
        var outside = Path.Combine(f.Root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "precious.txt"), "keep me");
        var link = Path.Combine(ModInstall.TargetDir(f.MtRoot), "linked");
        using (var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false }))
            p!.WaitForExit();
        Assert.True(Directory.Exists(link));
        Assert.True(ModInstall.Place(f.AppDir, f.MtRoot).Ok);
        Assert.False(Directory.Exists(link));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(outside, "precious.txt")));
    }

    [Fact]
    public void SomebodyElsesKdcmpIsForeignAndLeftAlone()
    {
        using var f = new FakeSteam("foreign").WithModdingTools().WithInstall();
        var target = ModInstall.TargetDir(f.MtRoot);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "mod.manifest"), "another mod");
        var r = ModInstall.Check(f.AppDir, f.MtRoot, ownsModFolder: false);
        Assert.True(r.ForeignFolder);
        Assert.False(r.Ok);
        Assert.False(ModInstall.Check(f.AppDir, f.MtRoot, ownsModFolder: true).ForeignFolder);
    }

    [Fact]
    public void AHandBuiltPakWarnsButNeverBlocksAndIsNeverOverwritten()
    {
        // The maintainer's workflow: Build-And-Install-Mod.ps1 drops a fresh pak over the installed one.
        using var f = new FakeSteam("devpak").WithGame().WithModdingTools().Linked().WithInstall().WithModPlaced();
        var pak = Path.Combine(ModInstall.TargetDir(f.MtRoot), "Data", "kdcmp.pak");
        File.WriteAllText(pak, "a pak built by hand, different bytes");
        var r = ModInstall.Check(f.AppDir, f.MtRoot, ownsModFolder: false);   // not even the registry marker
        Assert.True(r.Ok);
        Assert.False(r.ForeignFolder);
        Assert.Single(r.Warnings);
        var steps = SetupChecklist.Evaluate(f.Snapshot(ownsMod: false), SetupActivity.Idle);
        Assert.True(SetupChecklist.IsReady(steps));
        Assert.Contains("differ from what Setup installed", steps.Single(s => s.Id == StepId.Mod).Detail);
        Assert.Equal("a pak built by hand, different bytes", File.ReadAllText(pak));
    }

    [Fact]
    public void AFailedInstallVerdictIsNotComplete()
    {
        using var f = new FakeSteam("verdict").WithModdingTools().WithInstall(verdictPass: false).WithModPlaced();
        Assert.False(ModInstall.Check(f.AppDir, f.MtRoot, true).Ok);
    }
}

public class ChecklistTests
{
    private static IReadOnlyList<StepView> Eval(FakeSteam f, FakeHost? host = null, SetupActivity? a = null) =>
        SetupChecklist.Evaluate(f.Snapshot(host), a ?? SetupActivity.Idle);

    private static StepView Step(IReadOnlyList<StepView> s, StepId id) => s.Single(x => x.Id == id);

    [Fact]
    public void EverythingInPlaceIsReadyWithNothingToDo()
    {
        using var f = new FakeSteam("ready").WithGame().WithModdingTools().Linked().WithInstall().WithModPlaced();
        var steps = Eval(f);
        Assert.True(SetupChecklist.IsReady(steps), string.Join("\n", steps.Select(s => $"{s.Id} {s.Status} {s.Detail}")));
        Assert.All(steps, s => Assert.Equal(StepAction.None, s.Action));
    }

    [Fact]
    public void SteamClosedDoesNotHoldUpAFinishedSetup()
    {
        using var f = new FakeSteam("nosteam-ok").WithGame().WithModdingTools().Linked().WithInstall().WithModPlaced();
        var steps = Eval(f, new FakeHost { SteamRunning = false });
        Assert.True(SetupChecklist.IsReady(steps));
        Assert.Contains("Not needed right now", Step(steps, StepId.Steam).Detail);
    }

    [Fact]
    public void NoSteamAtAllWaitsEverythingOnSteam()
    {
        var snap = SetupProbe.Take(new FakeHost(), new SetupProbeOptions { SteamRootOverride = @"Z:\none" });
        var steps = SetupChecklist.Evaluate(snap, SetupActivity.Idle);
        Assert.Equal(StepStatus.NeedsYou, Step(steps, StepId.Steam).Status);
        Assert.Equal(StepStatus.Waiting, Step(steps, StepId.Game).Status);
        Assert.Equal(StepStatus.Waiting, Step(steps, StepId.ModdingTools).Status);
        Assert.False(SetupChecklist.IsReady(steps));
    }

    [Fact]
    public void MissingGameOffersSteamsInstallWindow()
    {
        using var f = new FakeSteam("nogame").WithModdingTools();
        var steps = Eval(f);
        Assert.Equal(StepAction.InstallGame, Step(steps, StepId.Game).Action);
        var opened = Eval(f, a: SetupActivity.Idle with { GameInstallOpened = true });
        Assert.Contains("install window is open", Step(opened, StepId.Game).Detail);
        Assert.Equal(StepStatus.Waiting, Step(steps, StepId.Workspace).Status);
    }

    [Fact]
    public void ModdingToolsDownloadShowsProgress()
    {
        using var f = new FakeSteam("downloading").WithGame()
            .WithManifestOnly(SteamApp.ModdingTools, "KCD2Mod", AppManifest.FlagUpdateRequired | AppManifest.FlagUpdateStarted | AppManifest.FlagDownloading, 1000, 250);
        var mt = Step(Eval(f), StepId.ModdingTools);
        Assert.Equal(StepStatus.Working, mt.Status);
        Assert.Equal(0.225, mt.Progress!.Value, 3);
        Assert.Contains("(23%)", mt.Detail);
        Assert.Equal(StepStatus.Done, Step(Eval(f), StepId.DiskSpace).Status);   // already downloading: no space question
    }

    [Fact]
    public void TooLittleSpaceAsksBeforeInstallingTheModdingTools()
    {
        using var f = new FakeSteam("nospace").WithGame();
        var steps = Eval(f, new FakeHost { Free = 5L * 1024 * 1024 * 1024 });
        Assert.Equal(StepStatus.NeedsYou, Step(steps, StepId.DiskSpace).Status);
        Assert.Contains("Free up 15 GB", Step(steps, StepId.DiskSpace).Detail);
        Assert.Equal(StepStatus.Waiting, Step(steps, StepId.ModdingTools).Status);
    }

    [Fact]
    public void InstalledButNotLinkedIsNotReady()
    {
        using var f = new FakeSteam("unlinked").WithGame().WithModdingTools().WithInstall();
        var steps = Eval(f);
        Assert.Equal(StepStatus.Done, Step(steps, StepId.ModdingTools).Status);
        var ws = Step(steps, StepId.Workspace);
        Assert.Equal(StepStatus.Working, ws.Status);
        Assert.Equal(StepAction.LinkWorkspace, ws.Action);   // the launcher starts this by itself
        Assert.Equal(StepStatus.Waiting, Step(steps, StepId.Mod).Status);
    }

    [Fact]
    public void ARunningGameBlocksLinking()
    {
        using var f = new FakeSteam("gamerunning").WithGame().WithModdingTools();
        var ws = Step(Eval(f, new FakeHost { GameRunning = true }), StepId.Workspace);
        Assert.Equal(StepStatus.NeedsYou, ws.Status);
        Assert.StartsWith("Close the game first", ws.Detail);
    }

    [Fact]
    public void PermissionStatesSayPlainlyWhatWindowsWants()
    {
        using var f = new FakeSteam("perm").WithGame().WithModdingTools();
        var asking = Step(Eval(f, a: SetupActivity.Idle with { AwaitingPermission = true }), StepId.Workspace);
        Assert.StartsWith("Windows needs your permission to link the game's files into the Modding Tools", asking.Detail);
        var declined = Step(Eval(f, a: SetupActivity.Idle with { PermissionDeclined = true }), StepId.Workspace);
        Assert.Equal(StepAction.AskPermission, declined.Action);
    }

    [Fact]
    public void EveryNeedsYouStepSaysWhatToDoInOneSentenceOrTwo()
    {
        using var a = new FakeSteam("s1");
        using var b = new FakeSteam("s2").WithManifestOnly(SteamApp.Game, "KingdomComeDeliverance2", 4).WithModdingTools();
        foreach (var steps in new[] { Eval(a), Eval(b), Eval(a, new FakeHost { SteamRunning = false }) })
            foreach (var s in steps.Where(s => s.Status == StepStatus.NeedsYou))
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Detail));
                Assert.True(s.Detail.Count(c => c == '.') <= 3, s.Detail);
            }
    }

    [Fact]
    public void ChecksRunWellUnderASecond()
    {
        using var f = new FakeSteam("timing").WithGame().WithModdingTools().Linked().WithInstall().WithModPlaced();
        Eval(f);
        var sw = Stopwatch.StartNew();
        Eval(f);
        Assert.True(sw.ElapsedMilliseconds < 500, $"{sw.ElapsedMilliseconds} ms");
    }
}

/// <summary>
/// WO-150 Part 3: the installer's four cases, on fake Steam libraries. The
/// installer acts on DetectReport's place_mod and nothing else, so these are
/// the decisions Setup makes; tools\Test-InstallerDetect.ps1 proves the
/// compiled Setup reads them the same way.
/// </summary>
public class InstallerCaseTests
{
    private static Dictionary<string, string> Detect(FakeSteam f) => DetectReport.Parse(DetectReport.Build(
        SetupProbe.Take(new FakeHost(), new SetupProbeOptions { SteamRootOverride = f.SteamRoot })));

    [Fact]
    public void Case1_NothingInstalled_LauncherAndAgentInstall_ModHeldBack()
    {
        using var f = new FakeSteam("case1");
        var d = Detect(f);
        Assert.Equal("0", d["mt_found"]);
        Assert.Equal("0", d["place_mod"]);
        Assert.Contains("The launcher will install them", d["reason"]);
    }

    [Fact]
    public void Case2_ModdingToolsInstalledNotLinked_ModHeldBack()
    {
        using var f = new FakeSteam("case2").WithGame().WithModdingTools();
        var d = Detect(f);
        Assert.Equal("1", d["mt_found"]);
        Assert.Equal("unlinked", d["workspace"]);
        Assert.Equal("0", d["place_mod"]);
        Assert.Contains("not linked", d["reason"]);
    }

    [Fact]
    public void Case3_LinkedNoMod_SetupPlacesTheMod()
    {
        using var f = new FakeSteam("case3").WithGame().WithModdingTools().Linked();
        var d = Detect(f);
        Assert.Equal("linked", d["workspace"]);
        Assert.Equal("1", d["place_mod"]);
        Assert.Equal(Path.Combine(f.MtRoot, "Mods", "kdcmp"), d["mods_dir"]);
    }

    [Fact]
    public void Case4_EverythingInPlace_SetupInstallsAsToday_LauncherOpensNormally()
    {
        using var f = new FakeSteam("case4").WithGame().WithModdingTools().Linked(FakeSteam.Linking.Copies).WithInstall().WithModPlaced();
        var d = Detect(f);
        Assert.Equal("1", d["place_mod"]);
        Assert.True(SetupChecklist.IsReady(SetupChecklist.Evaluate(f.Snapshot(), SetupActivity.Idle)));
    }

    [Fact]
    public void ModdingToolsWithoutTheGameIsHeldBackToo()
    {
        using var f = new FakeSteam("nogame").WithModdingTools();
        var d = Detect(f);
        Assert.Equal("unknown", d["workspace"]);
        Assert.Equal("0", d["place_mod"]);
        Assert.Contains("Deliverance II itself was not found", d["reason"]);
    }
}
