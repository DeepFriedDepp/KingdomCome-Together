// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;

namespace KcdMp.Setup.Tests;

/// <summary>
/// The synthetic runs WO-150 asks for, against a fake Steam library and a fake
/// setup tool that prints the real prompts: the pipe route, the
/// stall-and-fall-back route, the link fallback, a refused link, a
/// different-drive case.
/// </summary>
public class FlowTests
{
    private static string FakeToolExe => Path.Combine(AppContext.BaseDirectory, "faketool", "FakeWorkspaceSetup.exe");

    /// <summary>
    /// A copy of the fake tool for this fake library. The runner starts the exe with no arguments
    /// (the real tool takes none and asks Steam), so the fake reads its folders from args.txt beside it.
    /// </summary>
    private static string ToolFor(FakeSteam f, string mode, string link = "hard")
    {
        var dir = Path.Combine(f.Root, "tool-" + mode);
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(FakeToolExe)!))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
        File.WriteAllText(Path.Combine(dir, "args.txt"), $"--game\n{f.GameRoot}\n--mt\n{f.MtRoot}\n--mode\n{mode}\n--link\n{link}\n");
        return Path.Combine(dir, "FakeWorkspaceSetup.exe");
    }

    private static WorkspaceFlowOptions Options(List<string> log, string? tool, ILinkPrimitives? io = null,
                                                Func<CancellationToken, Task<ElevatedResult>>? elevated = null, double stallSeconds = 3) => new()
    {
        Host = new FakeHost(),
        ToolExe = tool,
        TryWarhorseTool = tool is not null,
        ToolStall = TimeSpan.FromSeconds(stallSeconds),
        ToolOverall = TimeSpan.FromSeconds(60),
        Primitives = io,
        RunElevated = elevated,
        Log = log.Add,
    };

    [Fact]
    public async Task PipeRoute_APipeDrivableToolIsAnsweredAndDoesTheWork()
    {
        using var f = new FakeSteam("pipe").WithGame().WithModdingTools().Linked(FakeSteam.Linking.Copies);
        // two stale copies, so the tool meets its delete prompt and must be answered "A" once
        foreach (var rel in new[] { @"Data\Tables.pak", @"Localization\English_xml.pak" })
            File.WriteAllText(Path.Combine(f.MtRoot, rel), "an old copy");
        var log = new List<string>();
        var outcome = await WorkspaceSetupFlow.RunAsync(f.GameRoot, f.MtRoot, Options(log, ToolFor(f, "readline")));
        Assert.Equal(WorkspaceFlowResult.Linked, outcome.Result);
        Assert.Equal("Warhorse's tool", outcome.Route);
        Assert.Contains(log, l => l.Contains("(answered S: symlink)"));
        Assert.Single(log, l => l.Contains("(answered A: yes to all)"));
        // and the tool's identity line reached the log only redacted
        Assert.Contains(log, l => l.Contains("Caching Steam ID") && l.Contains("<steam-id>"));
        Assert.DoesNotContain(log, l => l.Contains("76561190000000001"));
    }

    [Fact]
    public async Task ReadKeyRoute_TheShippedToolsBehaviourIsDetectedAndTheLauncherLinksItself()
    {
        using var f = new FakeSteam("readkey").WithGame().WithModdingTools();
        var log = new List<string>();
        var sw = Stopwatch.StartNew();
        var outcome = await WorkspaceSetupFlow.RunAsync(f.GameRoot, f.MtRoot, Options(log, ToolFor(f, "readkey"), stallSeconds: 20));
        Assert.Equal(WorkspaceFlowResult.Linked, outcome.Result);
        Assert.Equal("hard links", outcome.Route);
        Assert.Contains(log, l => l.Contains("tool CannotDrive"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), "a ReadKey tool is recognised at once, not after the stall window");
    }

    [Fact]
    public async Task StallRoute_AToolThatGoesQuietIsStoppedAndTheLauncherLinksItself()
    {
        using var f = new FakeSteam("stall").WithGame().WithModdingTools();
        var log = new List<string>();
        var tool = ToolFor(f, "stall");
        var outcome = await WorkspaceSetupFlow.RunAsync(f.GameRoot, f.MtRoot, Options(log, tool, stallSeconds: 2));
        Assert.Equal(WorkspaceFlowResult.Linked, outcome.Result);
        Assert.Contains(log, l => l.Contains("tool Stalled"));
        // the stalled tool was really stopped
        Assert.Empty(Process.GetProcessesByName("FakeWorkspaceSetup").Where(p => SamePath(p, tool)));
    }

    [Fact]
    public async Task DifferentDrive_ElevationIsAskedOnceAndFinishesTheJob()
    {
        using var f = new FakeSteam("elevate").WithGame().WithModdingTools();
        var log = new List<string>();
        int asked = 0;
        var io = new ScriptedPrimitives { HardLinkError = Native.ERROR_NOT_SAME_DEVICE, SymlinkError = Native.ERROR_PRIVILEGE_NOT_HELD };
        var outcome = await WorkspaceSetupFlow.RunAsync(f.GameRoot, f.MtRoot, Options(log, null, io, async _ =>
        {
            asked++;
            // stands in for the elevated helper: it links the remaining files (hard links here: one drive)
            WorkspaceLinker.Link(Workspace.Verify(f.GameRoot, f.MtRoot));
            await Task.Yield();
            return ElevatedResult.Finished;
        }));
        Assert.Equal(1, asked);
        Assert.Equal(WorkspaceFlowResult.Linked, outcome.Result);
        Assert.Contains(log, l => l.Contains("hard links are not possible between these folders, so symlinks"));
        Assert.Contains(log, l => l.Contains("one UAC prompt"));
    }

    [Fact]
    public async Task RefusedLink_DecliningThePromptLeavesAPlainSentence()
    {
        using var f = new FakeSteam("declined").WithGame().WithModdingTools();
        var io = new ScriptedPrimitives { HardLinkError = Native.ERROR_NOT_SAME_DEVICE, SymlinkError = Native.ERROR_PRIVILEGE_NOT_HELD };
        var outcome = await WorkspaceSetupFlow.RunAsync(f.GameRoot, f.MtRoot,
            Options(new List<string>(), null, io, _ => Task.FromResult(ElevatedResult.Declined)));
        Assert.Equal(WorkspaceFlowResult.PermissionDeclined, outcome.Result);
        Assert.Equal("Windows needs your permission to link the game's files into the Modding Tools.", outcome.Sentence);
    }

    [Fact]
    public async Task EvidenceRun_AnsweringNKeepsACompleteWorkspaceExactlyAsItWas()
    {
        // Stage B's real-tool run answers N: even a tool that CAN be driven then changes nothing.
        using var f = new FakeSteam("answern").WithGame().WithModdingTools().Linked(FakeSteam.Linking.Copies);
        string Listing() => string.Join("\n", Directory.GetFiles(f.MtRoot, "*.pak", SearchOption.AllDirectories).OrderBy(p => p)
            .Select(p => { var i = new FileInfo(p); return $"{p}|{i.Length}|{i.LastWriteTimeUtc.Ticks}|{i.CreationTimeUtc.Ticks}"; }));
        var before = Listing();
        var lines = new List<string>();
        var run = await WorkspaceToolRunner.RunAsync(ToolFor(f, "readline"), lines.Add, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60), default, "N");
        Assert.Equal(before, Listing());
        Assert.Equal(FakeSteam.GamePaks.Length, lines.Count(l => l.Contains("(answered N")));
        Assert.Contains(lines, l => l.Contains("Skipping file"));
        Assert.Equal(1 + FakeSteam.GamePaks.Length, run.AnswersSent);
    }

    [Fact]
    public async Task AlreadyLinked_NothingRunsAtAll()
    {
        using var f = new FakeSteam("noop").WithGame().WithModdingTools().Linked(FakeSteam.Linking.Copies);
        var log = new List<string>();
        var outcome = await WorkspaceSetupFlow.RunAsync(f.GameRoot, f.MtRoot, Options(log, ToolFor(f, "readline")));
        Assert.Equal("none needed", outcome.Route);
        Assert.DoesNotContain(log, l => l.Contains("running Warhorse"));
    }

    [Fact]
    public async Task ToolSkippedWithoutDotNet6()
    {
        using var f = new FakeSteam("nodotnet").WithGame().WithModdingTools();
        File.WriteAllText(WorkspaceToolRunner.ToolPath(f.MtRoot), "not really an exe");
        var log = new List<string>();
        var outcome = await WorkspaceSetupFlow.RunAsync(f.GameRoot, f.MtRoot, new WorkspaceFlowOptions
        {
            Host = new FakeHost { DotNet6 = false }, Log = log.Add,
        });
        Assert.Equal(WorkspaceFlowResult.Linked, outcome.Result);
        Assert.Contains(log, l => l.Contains("needs the .NET 6 runtime"));
    }

    [Fact]
    public void TheElevatedStepIsWindowlessAndAsksThroughUac()
    {
        var psi = ElevatedLinker.StartInfo(@"C:\App\KcdMpSetup.exe", @"D:\G", @"E:\M", @"C:\App\s.txt");
        Assert.Equal("runas", psi.Verb);
        Assert.True(psi.UseShellExecute);
        Assert.Equal(ProcessWindowStyle.Hidden, psi.WindowStyle);
        Assert.Equal("link --game \"D:\\G\" --mt \"E:\\M\" --kind symlink --status \"C:\\App\\s.txt\"", psi.Arguments);
    }

    [Fact]
    public void ReadKeyOnAPipeIsRecognisedFromDotNetsOwnMessage()
    {
        Assert.True(WorkspaceToolRunner.LooksLikeReadKeyOnPipe(
            "Unhandled exception. System.InvalidOperationException: Cannot read keys when either application does not have a console or when console input has been redirected. Try Console.Read.\n   at System.ConsolePal.ReadKey(Boolean intercept)"));
        Assert.False(WorkspaceToolRunner.LooksLikeReadKeyOnPipe("System.IO.IOException: A required privilege is not held by the client."));
    }

    private static bool SamePath(Process p, string exe)
    {
        try { return string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}
