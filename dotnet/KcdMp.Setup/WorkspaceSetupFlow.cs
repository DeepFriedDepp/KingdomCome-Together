// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.ComponentModel;
using System.Diagnostics;

namespace KcdMp.Setup;

public enum WorkspaceFlowResult { Linked, PermissionDeclined, InUse, Failed }

public sealed record WorkspaceFlowOutcome(WorkspaceFlowResult Result, string Sentence, string Route, WorkspaceReport After);

public enum ElevatedResult { Declined, Finished, CouldNotStart }

public sealed class WorkspaceFlowOptions
{
    public ISetupHost Host { get; init; } = RealSetupHost.Instance;
    public bool TryWarhorseTool { get; init; } = true;
    /// <summary>Overrides where the tool is looked for (tests point it at a fake tool).</summary>
    public string? ToolExe { get; init; }
    public TimeSpan ToolStall { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan ToolOverall { get; init; } = TimeSpan.FromMinutes(15);
    public ILinkPrimitives? Primitives { get; init; }
    /// <summary>Runs the elevated helper over the remaining files (one UAC prompt). Null: elevation is not available.</summary>
    public Func<CancellationToken, Task<ElevatedResult>>? RunElevated { get; init; }
    /// <summary>Called before the UAC prompt appears, so the checklist can show why first.</summary>
    public Action? BeforeElevation { get; init; }
    /// <summary>One line per step and outcome, already redacted. The launcher's log.</summary>
    public Action<string> Log { get; init; } = _ => { };
    public IProgress<LinkProgress>? Progress { get; init; }
}

/// <summary>
/// Checklist step 5, start to finish: Warhorse's tool through a pipe first,
/// then the launcher's own links, then -- only if Windows refuses a symlink --
/// one elevated run. Judged every time by re-reading the workspace, never by
/// any tool's exit code.
/// </summary>
public static class WorkspaceSetupFlow
{
    public static async Task<WorkspaceFlowOutcome> RunAsync(string gameRoot, string mtRoot, WorkspaceFlowOptions o, CancellationToken ct = default)
    {
        var report = Workspace.Verify(gameRoot, mtRoot);
        o.Log($"workspace: {report.OkCount}/{report.Entries.Count} in place before linking");
        if (report.Complete)
            return new WorkspaceFlowOutcome(WorkspaceFlowResult.Linked, "Already linked.", "none needed", report);

        // 1. Warhorse's own tool, driven through its redirected input -- or not at all.
        if (o.TryWarhorseTool)
        {
            var tool = o.ToolExe ?? WorkspaceToolRunner.ToolPath(mtRoot);
            string? skip = !File.Exists(tool) ? "the tool is not in this Modding Tools install"
                         : o.ToolExe is null && !o.Host.HasDotNet6Runtime() ? "it needs the .NET 6 runtime, which is not installed"
                         : o.ToolExe is null && !o.Host.IsProcessRunning("steam") ? "it needs Steam running"
                         : null;
            if (skip is not null) o.Log($"workspace: Warhorse's tool skipped: {skip}");
            else
            {
                o.Log("workspace: running Warhorse's WorkspaceSetup.exe in the background (no window, answers through a pipe)");
                var run = await WorkspaceToolRunner.RunAsync(tool, line => o.Log("  tool> " + line), o.ToolStall, o.ToolOverall, ct);
                report = Workspace.Verify(gameRoot, mtRoot);
                o.Log($"workspace: tool {run.Result} ({run.Reason}); {report.OkCount}/{report.Entries.Count} in place after it");
                if (report.Complete)
                    return new WorkspaceFlowOutcome(WorkspaceFlowResult.Linked, "Linked by Warhorse's setup tool.", "Warhorse's tool", report);
            }
        }

        // 2. The same links, made by the launcher.
        var linked = WorkspaceLinker.Link(report, o.Primitives, o.Progress, ct);
        o.Log($"workspace: launcher linked {linked.Linked} ({linked.HardLinks} hard links, {linked.Symlinks} symlinks), {linked.Failed} not linked" +
              (linked.FellBackToSymlinks ? "; hard links are not possible between these folders, so symlinks" : "") +
              (linked.NeedsElevation ? "; Windows refused a symlink without permission" : ""));
        foreach (var f in linked.Outcomes.Where(x => !x.Ok).Take(5))
            o.Log($"  not linked: {f.Rel}: {f.Note}");
        report = Workspace.Verify(gameRoot, mtRoot);
        if (report.Complete)
            return new WorkspaceFlowOutcome(WorkspaceFlowResult.Linked, $"Linked ({report.KindSummary()}).", Route(linked), report);

        if (linked.InUse)
            return new WorkspaceFlowOutcome(WorkspaceFlowResult.InUse, "Close the game first: its files can't be linked while it is running.", Route(linked), report);

        // 3. One UAC prompt, for the symlinks Windows would not make without it.
        if (linked.NeedsElevation)
        {
            if (o.RunElevated is null)
                return new WorkspaceFlowOutcome(WorkspaceFlowResult.PermissionDeclined,
                    "Windows needs permission to link the game's files into the Modding Tools.", Route(linked), report);
            o.Log("workspace: asking Windows for permission (one UAC prompt) to make the remaining links as symlinks");
            o.BeforeElevation?.Invoke();
            var elevated = await o.RunElevated(ct);
            report = Workspace.Verify(gameRoot, mtRoot);
            o.Log($"workspace: elevated helper {elevated}; {report.OkCount}/{report.Entries.Count} in place after it");
            if (report.Complete)
                return new WorkspaceFlowOutcome(WorkspaceFlowResult.Linked, $"Linked with your permission ({report.KindSummary()}).", "symlinks (elevated)", report);
            if (elevated == ElevatedResult.Declined)
                return new WorkspaceFlowOutcome(WorkspaceFlowResult.PermissionDeclined,
                    "Windows needs your permission to link the game's files into the Modding Tools.", "symlinks (permission declined)", report);
        }

        int left = report.Entries.Count - report.OkCount;
        var err = linked.Outcomes.FirstOrDefault(x => !x.Ok)?.Error ?? 0;
        return new WorkspaceFlowOutcome(WorkspaceFlowResult.Failed,
            $"{left} game file{(left == 1 ? "" : "s")} couldn't be linked{(err != 0 ? $" (Windows error {err})" : "")}. Click Try again; if it keeps failing, send the logs with Report a bug.",
            Route(linked), report);
    }

    private static string Route(LinkRun r) =>
        r.Symlinks > 0 && r.HardLinks > 0 ? "hard links and symlinks" : r.Symlinks > 0 ? "symlinks" : "hard links";
}

/// <summary>
/// The one elevated step: KcdMpSetup.exe link, started through UAC. The
/// helper is a windowless (WinExe) build, so even elevated nothing appears
/// but Windows' own prompt; it re-derives which files to link from the two
/// install roots itself and touches nothing but those *.pak paths.
/// </summary>
public static class ElevatedLinker
{
    public const int ErrorCancelled = 1223;   // the player chose No in the UAC prompt

    public static ProcessStartInfo StartInfo(string helperExe, string gameRoot, string mtRoot, string statusFile) => new()
    {
        FileName = helperExe,
        Arguments = $"link --game \"{gameRoot}\" --mt \"{mtRoot}\" --status \"{statusFile}\"",
        UseShellExecute = true,
        Verb = "runas",
        WindowStyle = ProcessWindowStyle.Hidden,
        WorkingDirectory = Path.GetDirectoryName(helperExe) ?? "",
    };

    public static async Task<ElevatedResult> RunAsync(string helperExe, string gameRoot, string mtRoot, string statusFile, CancellationToken ct)
    {
        try
        {
            using var p = Process.Start(StartInfo(helperExe, gameRoot, mtRoot, statusFile));
            if (p is null) return ElevatedResult.CouldNotStart;
            await p.WaitForExitAsync(ct);
            return ElevatedResult.Finished;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled) { return ElevatedResult.Declined; }
        catch (Exception) { return ElevatedResult.CouldNotStart; }
    }
}
