// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using KcdMp.Setup;

// KcdMpSetup.exe <verb> [options]   (WO-150; see KcdMp.SetupHost.csproj for why it is windowless)
//
//   detect    --out <file> [--steam-root <dir>] [--mt-exe <exe>]
//             What Setup asks before it installs: DetectReport's key=value lines.
//   check-exe --exe <exe> --out <file>
//             Setup's Browse button: is this a Modding Tools KingdomCome.exe, and where is its root.
//   check     --out <file> [--steam-root <dir>] [--app-dir <dir>] [--mt-exe <exe>]
//             The whole checklist as text, and what the launcher would do next. Reads only;
//             the dry run for a machine (docs/WO-150-progress.md).
//   link      --game <root> --mt <root> [--kind symlink] [--status <file>]
//             Make the missing workspace links. Run elevated by the launcher's one UAC step.
//   tool      --mt <root> --out <file> [--delete-answer A|N] [--stall <s>] [--tool-exe <exe>]
//             Run Warhorse's WorkspaceSetup.exe exactly as the launcher does (no window, a pipe);
//             N keeps every existing file, for an evidence run over a complete workspace.
//
// Exit codes: 0 done / complete, 1 not complete, 2 bad arguments.
return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0) return 2;
    var opt = Options(args.Skip(1));
    string? Get(string k) => opt.TryGetValue(k, out var v) ? v : null;

    try
    {
        switch (args[0].ToLowerInvariant())
        {
            case "detect":
            {
                var snap = SetupProbe.Take(RealSetupHost.Instance, new SetupProbeOptions
                {
                    SteamRootOverride = Get("steam-root"),
                    ExplicitModdingToolsExe = Get("mt-exe"),
                });
                return Write(Get("out"), DetectReport.Build(snap)) ? 0 : 2;
            }
            case "check-exe":
            {
                var exe = Get("exe");
                bool ok = GameLocator.IsModdingToolsBuild(exe);
                var root = ok ? GameLocator.InstallRootOf(exe) : null;
                var text = $"mt_build={(ok ? 1 : 0)}\r\nmt_root={root}\r\n";
                return Write(Get("out"), text) && ok && root is not null ? 0 : 1;
            }
            case "check":
            {
                var appDir = Get("app-dir");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var snap = SetupProbe.Take(RealSetupHost.Instance, new SetupProbeOptions
                {
                    SteamRootOverride = Get("steam-root"),
                    ExplicitModdingToolsExe = Get("mt-exe"),
                    AppDir = appDir,
                    OwnsModFolder = OwnsModFolder,
                });
                var steps = SetupChecklist.Evaluate(snap, SetupActivity.Idle);
                long checkMs = sw.ElapsedMilliseconds;
                var sb = new StringBuilder();
                sb.Append($"checked_in_ms={checkMs}\r\n");
                foreach (var s in steps)
                    sb.Append($"{(int)s.Id}. [{s.Status}] {s.Title}: {s.Detail}{(s.Action != StepAction.None ? $"  -> would {s.Action}" : "")}\r\n");
                sb.Append($"ready={(SetupChecklist.IsReady(steps) ? 1 : 0)}\r\n");
                if (snap.Workspace is { } w)
                    sb.Append($"workspace: {w.OkCount}/{w.Entries.Count} in place ({w.KindSummary()})\r\n");
                return Write(Get("out"), Redact.Text(sb.ToString())) ? (SetupChecklist.IsReady(steps) ? 0 : 1) : 2;
            }
            case "tool":
            {
                // Warhorse's WorkspaceSetup.exe through the launcher's own runner: no window, answers through a pipe.
                var mt = Get("mt");
                var tool = Get("tool-exe") ?? (mt is null ? null : WorkspaceToolRunner.ToolPath(mt));
                var answer = (Get("delete-answer") ?? "A").ToUpperInvariant();
                var stall = TimeSpan.FromSeconds(int.TryParse(Get("stall"), out var st) ? st : 20);
                if (tool is null || answer is not ("A" or "N")) return 2;
                var sb = new StringBuilder();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var run = WorkspaceToolRunner.RunAsync(tool, line => { lock (sb) sb.Append($"tool> {line}\r\n"); },
                                                       stall, TimeSpan.FromMinutes(15), default, answer).GetAwaiter().GetResult();
                sb.Insert(0, $"result={run.Result}\r\nreason={run.Reason}\r\nexit_code={run.ExitCode}\r\nanswers_sent={run.AnswersSent}\r\nelapsed_ms={sw.ElapsedMilliseconds}\r\n");
                Write(Get("out"), Redact.Text(sb.ToString()));
                return run.Result == ToolRunResult.Finished ? 0 : 1;
            }
            case "link":
            {
                var game = Get("game");
                var mt = Get("mt");
                var status = Get("status");
                // Only ever between a real game install and a real Modding Tools install: never a general link maker.
                if (!GameLocator.GameFilesPresent(game) || mt is null || GameLocator.FindModdingToolsExe(mt) is null)
                {
                    Write(status, "result=refused\r\nreason=not a game and a Modding Tools install\r\n");
                    return 2;
                }
                var report = Workspace.Verify(game!, mt);
                bool symOnly = string.Equals(Get("kind"), "symlink", StringComparison.OrdinalIgnoreCase);
                var run = WorkspaceLinker.Link(report, symlinksOnly: symOnly);
                var after = Workspace.Verify(game!, mt);
                var sb = new StringBuilder();
                sb.Append($"result={(after.Complete ? "linked" : "incomplete")}\r\n");
                sb.Append($"linked={run.Linked}\r\nhardlinks={run.HardLinks}\r\nsymlinks={run.Symlinks}\r\nfailed={run.Failed}\r\n");
                sb.Append($"in_place={after.OkCount}/{after.Entries.Count}\r\n");
                foreach (var f in run.Outcomes.Where(o => !o.Ok).Take(10))
                    sb.Append($"not_linked={f.Rel}: {f.Note}\r\n");
                Write(status, Redact.Text(sb.ToString()));
                return after.Complete ? 0 : 1;
            }
            default:
                return 2;
        }
    }
    catch (Exception ex)
    {
        Write(Get("out") ?? Get("status"), $"error={ex.GetType().Name}\r\n");
        return 2;
    }
}

static Dictionary<string, string> Options(IEnumerable<string> rest)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    string? key = null;
    foreach (var a in rest)
    {
        if (a.StartsWith("--", StringComparison.Ordinal)) { key = a[2..]; d[key] = ""; }
        else if (key is not null) { d[key] = a; key = null; }
    }
    return d;
}

static bool Write(string? path, string text)
{
    if (string.IsNullOrWhiteSpace(path)) return false;
    try { File.WriteAllText(path, text, new UTF8Encoding(false)); return true; }
    catch { return false; }
}

static bool OwnsModFolder(string target)
{
    try
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\KCDMP");
        return key?.GetValue("ModsPath") is string p && SetupPaths.SamePath(p, target);
    }
    catch { return false; }
}
