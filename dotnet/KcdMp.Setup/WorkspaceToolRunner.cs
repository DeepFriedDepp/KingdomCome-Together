// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;
using System.Text;

namespace KcdMp.Setup;

public enum ToolRunResult
{
    /// <summary>The tool ran to its end (its exit code is not trusted: re-verify the workspace).</summary>
    Finished,
    /// <summary>It read its answers from the console, not from the pipe (Console.ReadKey throws on a pipe). Stopped.</summary>
    CannotDrive,
    /// <summary>No output for the stall window while still running -- waiting on something no pipe can answer. Stopped.</summary>
    Stalled,
    /// <summary>Not started: missing, no .NET 6, or Windows would not start it.</summary>
    NotStarted,
}

public sealed record ToolRunOutcome(ToolRunResult Result, int? ExitCode, string Reason, int AnswersSent);

/// <summary>
/// Runs Warhorse's &lt;MT&gt;\Tools\ModdingWorkspaceSetup\WorkspaceSetup.exe the
/// only way WO-150 allows: a background process with no window, its standard
/// input, output and error redirected to us. No keystroke goes into any
/// window, and no window is ever created to type into.
///
/// It answers the tool's two prompts through the pipe -- "S" to
/// "[C]opy packs or [S]ymlink them?", "A" once to "Delete File
/// [Y]es/[N]o/Yes to [A]ll:" -- and stops the tool when it cannot be driven
/// that way: it read the console directly (Console.ReadKey throws "Cannot
/// read keys when ... console input has been redirected"), or it went quiet
/// for the stall window.
///
/// The shipped tool (1.0.0.0, read 2026-10-02) reads every answer with
/// Console.ReadKey, so on a real machine this ends in CannotDrive at the first
/// prompt, before the tool has changed anything, and the caller links the
/// files itself (<see cref="WorkspaceLinker"/>). It is still tried first so
/// that a future version of the tool that reads standard input is used as
/// Warhorse intends.
/// </summary>
public static class WorkspaceToolRunner
{
    public const string CopyOrLinkPrompt = "[C]opy packs or [S]ymlink";
    public const string DeletePrompt = "Delete File [Y]es/[N]o/Yes to [A]ll";

    public static string ToolPath(string moddingToolsRoot) =>
        Path.Combine(moddingToolsRoot, "Tools", "ModdingWorkspaceSetup", "WorkspaceSetup.exe");

    /// <param name="onLine">Every line the tool prints, already redacted. Goes to the progress view and the log.</param>
    /// <param name="deleteAnswer">
    /// The answer to the delete prompt: "A" (yes to all, what setup needs to replace stale files),
    /// or "N" for an evidence run over a workspace that is already complete, so that even a tool
    /// that could be driven would change nothing (docs/WO-150-progress.md, Stage B).
    /// </param>
    public static async Task<ToolRunOutcome> RunAsync(string toolExe, Action<string> onLine, TimeSpan stall,
                                                      TimeSpan overall, CancellationToken ct = default, string deleteAnswer = "A")
    {
        if (deleteAnswer is not ("A" or "N")) throw new ArgumentException("A or N", nameof(deleteAnswer));
        if (!File.Exists(toolExe)) return new ToolRunOutcome(ToolRunResult.NotStarted, null, "the tool is not in the Modding Tools", 0);

        var psi = new ProcessStartInfo
        {
            FileName = toolExe,
            WorkingDirectory = Path.GetDirectoryName(toolExe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // A missing .NET runtime must not raise the host's "install .NET" message box.
        psi.Environment["DOTNET_DISABLE_GUI_ERRORS"] = "1";

        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
        }
        catch (Exception ex)
        {
            return new ToolRunOutcome(ToolRunResult.NotStarted, null, $"Windows would not start it ({ex.GetType().Name})", 0);
        }

        using (proc)
        {
            var gate = new object();
            var lastOutput = DateTime.UtcNow;
            var outTail = new StringBuilder();
            var errText = new StringBuilder();
            int answers = 0;
            bool answeredCopy = false, answeredDelete = false;

            void Answer(string s)
            {
                try
                {
                    proc.StandardInput.Write(s + "\r\n");
                    proc.StandardInput.Flush();
                    answers++;
                }
                catch { /* the tool already died; the exit path reports why */ }
            }

            async Task Pump(StreamReader reader, bool isError)
            {
                var line = new StringBuilder();
                var buf = new char[512];
                while (true)
                {
                    int n;
                    try { n = await reader.ReadAsync(buf.AsMemory(), CancellationToken.None); }
                    catch { break; }
                    if (n <= 0) break;
                    lock (gate)
                    {
                        lastOutput = DateTime.UtcNow;
                        for (int i = 0; i < n; i++)
                        {
                            char c = buf[i];
                            if (c == '\n')
                            {
                                var text = line.ToString().TrimEnd('\r');
                                line.Clear();
                                if (text.Length > 0) onLine(Redact.Text(text));
                                if (isError) errText.AppendLine(text);
                                // "N" is per file: each delete prompt, written with WriteLine, gets its own answer.
                                if (!isError && deleteAnswer == "N" && text.Contains(DeletePrompt, StringComparison.Ordinal))
                                {
                                    onLine("(answered N: keep the existing file)");
                                    Answer("N");
                                }
                                continue;
                            }
                            line.Append(c);
                        }
                        if (!isError)
                        {
                            outTail.Append(buf, 0, n);
                            if (outTail.Length > 4096) outTail.Remove(0, outTail.Length - 4096);
                            var tail = outTail.ToString();
                            // Prompts are written with Console.WriteLine; match on the text so far either way.
                            if (!answeredCopy && tail.Contains(CopyOrLinkPrompt, StringComparison.Ordinal))
                            {
                                answeredCopy = true;
                                onLine("(answered S: symlink)");
                                Answer("S");
                            }
                            if (deleteAnswer == "A" && !answeredDelete && tail.Contains(DeletePrompt, StringComparison.Ordinal))
                            {
                                answeredDelete = true;
                                onLine("(answered A: yes to all)");
                                Answer("A");
                            }
                        }
                    }
                }
                lock (gate) { if (line.Length > 0) onLine(Redact.Text(line.ToString())); }
            }

            var pumps = Task.WhenAll(Pump(proc.StandardOutput, false), Pump(proc.StandardError, true));
            var started = DateTime.UtcNow;

            while (!proc.HasExited)
            {
                if (ct.IsCancellationRequested) { Kill(proc); return new ToolRunOutcome(ToolRunResult.Stalled, null, "cancelled", answers); }
                bool readKey;
                DateTime quietSince;
                lock (gate)
                {
                    readKey = LooksLikeReadKeyOnPipe(errText.ToString());
                    quietSince = lastOutput;
                }
                if (readKey)
                {
                    Kill(proc);
                    return new ToolRunOutcome(ToolRunResult.CannotDrive, null, "it reads its answers from the console, not from a pipe", answers);
                }
                if (DateTime.UtcNow - quietSince > stall)
                {
                    Kill(proc);
                    return new ToolRunOutcome(ToolRunResult.Stalled, null, $"no progress for {stall.TotalSeconds:0} s", answers);
                }
                if (DateTime.UtcNow - started > overall)
                {
                    Kill(proc);
                    return new ToolRunOutcome(ToolRunResult.Stalled, null, $"still running after {overall.TotalMinutes:0} min", answers);
                }
                try { await Task.Delay(100, ct); } catch (OperationCanceledException) { }
            }

            await Task.WhenAny(pumps, Task.Delay(2000, CancellationToken.None));
            int code = proc.ExitCode;
            string err;
            lock (gate) err = errText.ToString();
            if (LooksLikeReadKeyOnPipe(err))
                return new ToolRunOutcome(ToolRunResult.CannotDrive, code, "it reads its answers from the console, not from a pipe", answers);
            return new ToolRunOutcome(ToolRunResult.Finished, code, $"exited with code {code}", answers);
        }
    }

    /// <summary>.NET's message when Console.ReadKey meets a redirected input (any .NET since Core 1.0).</summary>
    internal static bool LooksLikeReadKeyOnPipe(string stderr) =>
        stderr.Contains("Cannot read keys", StringComparison.Ordinal) ||
        (stderr.Contains("InvalidOperationException", StringComparison.Ordinal) && stderr.Contains("ReadKey", StringComparison.Ordinal));

    private static void Kill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        try { p.WaitForExit(3000); } catch { }
    }
}
