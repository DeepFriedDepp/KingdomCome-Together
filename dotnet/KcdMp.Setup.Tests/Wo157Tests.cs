// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;

namespace KcdMp.Setup.Tests;

/// <summary>WO-157: the in-launcher DLL load (no injector exe) and the Visual C++ 2013 runtime step.</summary>
public class Wo157Tests
{
    private static StepView Step(IReadOnlyList<StepView> s, StepId id) => s.Single(x => x.Id == id);

    // ------------------------------------------------------------ the Visual C++ 2013 runtime (2.3)

    [Fact]
    public void VcRuntimePresentIsDoneAndReadyStands()
    {
        using var f = new FakeSteam("vc-ok").WithGame().WithModdingTools().Linked().WithInstall().WithModPlaced();
        var steps = SetupChecklist.Evaluate(f.Snapshot(new FakeHost { VcRuntime = true }), SetupActivity.Idle);
        var vc = Step(steps, StepId.VcRuntime);
        Assert.Equal(StepStatus.Done, vc.Status);
        Assert.True(vc.Optional);
        Assert.Contains("Visual C++ 2013", vc.Title);
        Assert.True(SetupChecklist.IsReady(steps));
        Assert.False(SetupChecklist.OptionalNeedsYou(steps));
    }

    [Fact]
    public void VcRuntimeMissingWithSteamsRedistOffersTheInstallButNeverBlocks()
    {
        using var f = new FakeSteam("vc-redist").WithGame().WithModdingTools().Linked().WithInstall().WithModPlaced();
        string redist = Path.Combine([f.Library, .. VcRuntime2013.RedistRelPath]);
        Directory.CreateDirectory(Path.GetDirectoryName(redist)!);
        File.WriteAllText(redist, "fake");
        var snap = f.Snapshot(new FakeHost { VcRuntime = false });
        Assert.False(snap.VcRuntime2013);
        Assert.Equal(Path.GetFullPath(redist), snap.VcRedist2013);
        var steps = SetupChecklist.Evaluate(snap, SetupActivity.Idle);
        var vc = Step(steps, StepId.VcRuntime);
        Assert.Equal(StepStatus.NeedsYou, vc.Status);
        Assert.Equal(StepAction.InstallVcRuntime, vc.Action);
        Assert.Contains("MSVCP120.dll", vc.Detail);
        Assert.Contains("asks once for permission", vc.Detail);
        Assert.True(SetupChecklist.IsReady(steps));          // Host and Join are not held back
        Assert.True(SetupChecklist.OptionalNeedsYou(steps)); // the checklist is shown once
        Assert.Equal(StepStatus.Done, Step(steps, StepId.Ready).Status);
        var working = SetupChecklist.Evaluate(snap, SetupActivity.Idle with { InstallingVcRuntime = true });
        Assert.Equal(StepStatus.Working, Step(working, StepId.VcRuntime).Status);
    }

    [Fact]
    public void VcRuntimeMissingWithoutRedistPointsToMicrosoft()
    {
        using var f = new FakeSteam("vc-none").WithGame().WithModdingTools().Linked().WithInstall().WithModPlaced();
        var snap = f.Snapshot(new FakeHost { VcRuntime = false });
        Assert.Null(snap.VcRedist2013);
        var vc = Step(SetupChecklist.Evaluate(snap, SetupActivity.Idle), StepId.VcRuntime);
        Assert.Equal(StepAction.OpenVcDownload, vc.Action);
        Assert.StartsWith("https://learn.microsoft.com/", VcRuntime2013.DownloadPage);
    }

    [Fact]
    public void VcRuntimeDetectionReadsTheSystemFolder()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "scratch", "vc-sys-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            Assert.False(VcRuntime2013.PresentIn(dir));
            File.WriteAllText(Path.Combine(dir, "MSVCP120.DLL"), "x");
            Assert.True(VcRuntime2013.PresentIn(dir));
        }
        finally { Directory.Delete(dir, true); }
        Assert.True(VcRuntime2013.InstalledByExitCode(0) && VcRuntime2013.InstalledByExitCode(3010) && VcRuntime2013.InstalledByExitCode(1638));
        Assert.False(VcRuntime2013.InstalledByExitCode(1602));   // the player cancelled
    }

    // ------------------------------------------------------------ the DLL load from the launcher (2.1)

    private static Process StartTarget()
    {
        // A harmless x64 process of our own to load a DLL into (Windows' ping waiting 60 s).
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n 60 127.0.0.1")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        var p = Process.Start(psi)!;
        // Its first line: the process has finished starting (a load into a process still starting can fail).
        p.StandardOutput.ReadLine();
        return p;
    }

    private static string AnyDll => Path.Combine(Environment.SystemDirectory, "version.dll");

    [Fact]
    public void InjectLoadsTheDllIntoTheExpectedProcess()
    {
        using var p = StartTarget();
        try
        {
            var r = GameInjector.Inject(p.Id, AnyDll, p.MainModule!.FileName, GameInjector.Sha256Of(AnyDll));
            Assert.True(r.Ok, r.LogLine);
            Assert.StartsWith("MP-INJECT injected", r.LogLine);
            p.Refresh();
            Assert.Contains(p.Modules.Cast<ProcessModule>(), m => string.Equals(m.ModuleName, "version.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally { try { p.Kill(); } catch { } }
    }

    [Fact]
    public void InjectRefusesAnotherProgram()
    {
        using var p = StartTarget();
        try
        {
            var r = GameInjector.Inject(p.Id, AnyDll, Path.Combine(Environment.SystemDirectory, "notepad.exe"), null);
            Assert.Equal(InjectOutcome.WrongProcess, r.Outcome);
            Assert.Contains("image=PING.EXE", r.LogLine, StringComparison.OrdinalIgnoreCase);
            p.Refresh();
            Assert.DoesNotContain(p.Modules.Cast<ProcessModule>(), m => string.Equals(m.ModuleName, "version.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally { try { p.Kill(); } catch { } }
    }

    [Fact]
    public void InjectRefusesADllThatIsNotTheShippedOne()
    {
        using var p = StartTarget();
        try
        {
            var r = GameInjector.Inject(p.Id, AnyDll, p.MainModule!.FileName, new string('0', 64));
            Assert.Equal(InjectOutcome.DllNotShipped, r.Outcome);
            Assert.Contains("Run Setup again", r.Message);
        }
        finally { try { p.Kill(); } catch { } }
    }

    [Fact]
    public void InjectRefusesAMissingDllAndAGoneProcess()
    {
        var missing = GameInjector.Inject(Environment.ProcessId, Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".dll"), null, null);
        Assert.Equal(InjectOutcome.DllMissing, missing.Outcome);
        Assert.Contains("Protection history", missing.Message);

        using var p = StartTarget();
        int pid = p.Id;
        p.Kill();
        p.WaitForExit();
        var gone = GameInjector.Inject(pid, AnyDll, null, null);
        Assert.Equal(InjectOutcome.WrongProcess, gone.Outcome);
    }
}
