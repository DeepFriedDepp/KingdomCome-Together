// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.LogPack;

/// <summary>
/// WO-202: BRIEFING.md, the first file of every bundle, written for the maintainer: the crashes first (I.1: the mod's own crash reports,
/// the game's crash reporter, Windows' reports), then the session (length, role, version, connection), every mark with what this machine
/// was doing around it, every quest failure in plain words (II.4's WO202-QUESTFAIL), the desync scorecard (the measures this machine
/// logs), and what stood out (the most frequent errors against the earlier launch). Pure text work over the bundle's files: never fatal.
/// </summary>
public static class Briefing
{
    public sealed class Input
    {
        public Dictionary<string, string[]> Logs = new(StringComparer.Ordinal);   // entry -> lines
        public Dictionary<string, DateTime> Mtimes = new(StringComparer.Ordinal);  // entry -> the file's last write (this machine's clock), when known
    }

    public static Input Read(IReadOnlyList<SourceFile> sources)
    {
        var inp = new Input();
        foreach (var s in sources)
        {
            if (!(s.EntryName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || s.EntryName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                  || s.EntryName.EndsWith(".wer", StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                byte[]? b = s.Bytes;
                if (s.Path is not null && File.Exists(s.Path)) inp.Mtimes[s.EntryName] = File.GetLastWriteTime(s.Path);
                if (b is null && s.Path is not null && File.Exists(s.Path))
                {
                    using var fs = new FileStream(s.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var ms = new MemoryStream(); fs.CopyTo(ms); b = ms.ToArray();
                }
                if (b is null) continue;
                var text = Encoding.UTF8.GetString(b);
                inp.Logs[s.EntryName] = text.Replace("\r\n", "\n").Split('\n');
            }
            catch { }
        }
        return inp;
    }

    private static readonly Regex AgentTime = new(@"^(\d\d:\d\d:\d\d\.\d+) ", RegexOptions.Compiled);
    private static readonly Regex Mark = new(@"^(?<t>\d\d:\d\d:\d\d\.\d+) .*?MP-MARK (?<w>[A-Za-z0-9_]+)(?: note=(?<n>.*?))? -- the tester's marker", RegexOptions.Compiled);
    private static readonly Regex PeerMark = new(@"^(?<t>\d\d:\d\d:\d\d\.\d+) .*?MP-MARK ping from player (?<p>\d+): mark=(?<m>\S+)", RegexOptions.Compiled);
    private static readonly Regex Ping = new(@"\[ping\] (\d+) ms", RegexOptions.Compiled);
    private static readonly Regex QueueLag = new(@"\[ping\] \d+ ms .*?the game's queue is (\d+) ms behind(, the game is in the background)?", RegexOptions.Compiled);
    private static readonly Regex QueueOverflow = new(@"attempting to queue too many actions on entity '([^']+)'", RegexOptions.Compiled);

    public static string Build(Input inp)
    {
        var sb = new StringBuilder();
        sb.Append("# Kingdom Come: Together -- briefing\n\n");
        sb.Append("Written by the bug report itself (WO-202). Times are this machine's clock.\n\n");
        Crashes(inp, sb);
        Session(inp, sb);
        Marks(inp, sb);
        QuestFails(inp, sb);
        Scorecard(inp, sb);
        StoodOut(inp, sb);
        return sb.ToString();
    }

    private static IEnumerable<string> AgentLines(Input inp) => SessionAgentKeys(inp).SelectMany(k => inp.Logs[k]);

    // WO-204 Act V: the briefing is this launch's session only. The bundle keeps the earlier agent logs (agent.prev*.log), but one
    // counts only when it was still being written within 30 minutes before this launch's agent log began (an agent restarted inside
    // the same session); session D's host briefing was headed by a mark from two days before. Without the files' times, agent.log alone.
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    public static List<string> SessionAgentKeys(Input inp)
    {
        var keys = new List<string>();
        if (!inp.Logs.TryGetValue("agent.log", out var cur)) return keys;
        DateTime? start = LogStart(inp, "agent.log", cur);
        foreach (var k in new[] { "agent.prev2.log", "agent.prev.log" })
            if (inp.Logs.ContainsKey(k) && start is DateTime st && inp.Mtimes.TryGetValue(k, out var mt) && mt <= st.AddMinutes(1) && st - mt <= SessionGap)
                keys.Add(k);
        keys.Add("agent.log");
        return keys;
    }

    /// <summary>When a log began: its file's last write date with its first line's time of day (a day earlier when the log ran past
    /// midnight). Null without the file's time.</summary>
    public static DateTime? LogStart(Input inp, string key, string[] lines)
    {
        if (!inp.Mtimes.TryGetValue(key, out var mt)) return null;
        var first = lines.Select(x => AgentTime.Match(x)).FirstOrDefault(m => m.Success);
        if (first is null || !TimeSpan.TryParse(first.Groups[1].Value, CultureInfo.InvariantCulture, out var tod)) return null;
        var start = mt.Date + tod;
        if (start > mt.AddMinutes(1)) start = start.AddDays(-1);
        return start;
    }

    private static IEnumerable<string> NativeLines(Input inp) =>
        inp.Logs.Where(kv => kv.Key.StartsWith("kcdmp-native", StringComparison.OrdinalIgnoreCase)).OrderBy(kv => kv.Key, StringComparer.Ordinal).SelectMany(kv => kv.Value);

    // ---- I.1: the crashes, first ----
    private static void Crashes(Input inp, StringBuilder sb)
    {
        sb.Append("## Crashes\n\n");
        int n = 0;
        foreach (var kv in inp.Logs.Where(kv => kv.Key.StartsWith("crashes/", StringComparison.Ordinal) && kv.Key.EndsWith("/crash.txt", StringComparison.Ordinal))
                                   .OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            n++;
            var l = kv.Value;
            string Field(string k) => l.FirstOrDefault(x => x.StartsWith(k, StringComparison.Ordinal))?[k.Length..].Trim() ?? "?";
            sb.Append($"### {kv.Key[8..^10]}\n\n");
            sb.Append($"- **when:** {Field("time:")}\n- **what:** {Field("exception:")}\n- **where:** {Field("faulting:")}\n- **thread:** {Field("thread:")}\n");
            sb.Append($"- **mod code on the stack:** {Field("mod hooks on the stack:")}\n");
            var frames = l.SkipWhile(x => !x.StartsWith("the faulting thread's stack", StringComparison.Ordinal)).Skip(1).TakeWhile(x => x.StartsWith("  #", StringComparison.Ordinal)).Take(8);
            sb.Append("- **the stack's top:**\n");
            foreach (var f in frames) sb.Append("  - `").Append(f.Trim()).Append("`\n");
            // what the mod did in the last 5 s: the native ring's last lines, then the agent's last statements
            var ring = l.SkipWhile(x => !x.StartsWith("the last ", StringComparison.Ordinal) || !x.Contains("native log lines", StringComparison.Ordinal)).Skip(1)
                        .Where(x => x.StartsWith("[", StringComparison.Ordinal)).ToList();
            sb.Append("- **the mod's last native lines:**\n");
            foreach (var r in ring.TakeLast(8)) sb.Append("  - `").Append(Cut(r, 200)).Append("`\n");
            string agentPart = kv.Key[..^"crash.txt".Length] + "agent-statements.txt";
            if (inp.Logs.TryGetValue(agentPart, out var ap))
            {
                sb.Append("- **the last Lua statements the agent applied:**\n");
                foreach (var a in ap.Where(x => Regex.IsMatch(x, @"^\d\d:\d\d:\d\d")).TakeLast(6)) sb.Append("  - `").Append(Cut(a, 200)).Append("`\n");
            }
            sb.Append('\n');
        }
        foreach (var kv in inp.Logs.Where(kv => kv.Key.StartsWith("crashes/", StringComparison.Ordinal) && kv.Key.EndsWith("-agent/agent-statements.txt", StringComparison.Ordinal)))
        {
            n++;
            sb.Append($"### {kv.Key[8..]}\n\n- ").Append(Cut(kv.Value.Skip(1).FirstOrDefault() ?? "", 300)).Append("\n- ").Append(Cut(kv.Value.Skip(2).FirstOrDefault() ?? "", 300)).Append("\n\n");
        }
        foreach (var kv in inp.Logs.Where(kv => kv.Key.StartsWith("game-crash/bugsplat_", StringComparison.Ordinal)))
        {
            var mfa = kv.Value.FirstOrDefault(x => x.Contains("MFA:", StringComparison.Ordinal));
            var when = kv.Value.FirstOrDefault(x => x.Contains("Entered Unhandled Exception Filter", StringComparison.Ordinal));
            if (mfa is null && when is null) continue;
            n++;
            sb.Append($"- the game's own crash reporter ({kv.Key[11..]}): {Cut(when ?? "", 80)} -- {Cut(mfa ?? "no module", 120)}\n");
        }
        foreach (var kv in inp.Logs.Where(kv => kv.Key.StartsWith("windows/", StringComparison.Ordinal)))
        {
            var mods = kv.Value.Where(x => x.Contains("Faulting module", StringComparison.OrdinalIgnoreCase) || x.StartsWith("Sig[3].Value", StringComparison.Ordinal)
                                          || x.Contains("Exception code", StringComparison.OrdinalIgnoreCase)).Take(6).ToList();
            if (mods.Count == 0) continue;
            n++;
            sb.Append($"- Windows ({kv.Key[8..]}): ").Append(string.Join("; ", mods.Select(m => Cut(m.Trim(), 120)))).Append('\n');
        }
        foreach (var l in AgentLines(inp).Where(x => x.Contains("WO202-CRASH the game", StringComparison.Ordinal) && x.Contains("WITHOUT", StringComparison.Ordinal)))
        {
            n++;
            sb.Append("- the agent saw the game end without its quit line: `").Append(Cut(l, 240)).Append("`\n");
        }
        if (n == 0) sb.Append("No crash in this bundle (no crash report, no game crash file, no game end without its quit line).\n");
        sb.Append('\n');
    }

    // ---- the session ----
    private static void Session(Input inp, StringBuilder sb)
    {
        sb.Append("## The session\n\n");
        var agent = inp.Logs.TryGetValue("agent.log", out var a) ? a : Array.Empty<string>();
        string? first = agent.Select(x => AgentTime.Match(x)).FirstOrDefault(m => m.Success)?.Groups[1].Value;
        string? last = agent.Reverse().Select(x => AgentTime.Match(x)).FirstOrDefault(m => m.Success)?.Groups[1].Value;
        sb.Append($"- **this agent's log:** {first ?? "?"} to {last ?? "?"}\n");
        var roleLine = agent.LastOrDefault(x => x.Contains("NPC->player damage authority", StringComparison.Ordinal));
        string role = roleLine is null ? "?" : roleLine.Contains("now holds", StringComparison.Ordinal) ? "host" : "joiner";
        sb.Append($"- **role:** {role}\n");
        var build = AgentLines(inp).Select(x => Regex.Match(x, @" build=(\S+)")).LastOrDefault(m => m.Success)?.Groups[1].Value;
        sb.Append($"- **version:** {build ?? "?"}\n");
        var pings = AgentLines(inp).Select(x => Ping.Match(x)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        if (pings.Count > 0)
            sb.Append($"- **ping:** {pings.Count} samples, median {Pct(pings, 50)} ms, p99 {Pct(pings, 99)} ms, max {pings.Max()} ms\n");
        int struggling = AgentLines(inp).Concat(Kcd(inp)).Count(x => x.Contains("WO157-CONN struggling", StringComparison.Ordinal));
        int game = Kcd(inp).Count(x => x.Contains("WO202-CONN told", StringComparison.Ordinal));
        int holds = AgentLines(inp).Count(x => x.Contains("MP-BATCH-HOLD down", StringComparison.Ordinal));
        sb.Append($"- **connection trouble:** struggling {struggling} time(s); the game (not the network) late {game} time(s); the game stopped answering {holds} time(s)\n");
        int crashes = inp.Logs.Keys.Count(k => k.StartsWith("crashes/", StringComparison.Ordinal) && k.EndsWith("/crash.txt", StringComparison.Ordinal));
        sb.Append($"- **crashes:** {crashes} (above)\n\n");
    }

    private static IEnumerable<string> Kcd(Input inp) => inp.Logs.TryGetValue("kcd.log", out var k) ? k : Array.Empty<string>();

    // ---- every mark, with its context ----
    private static void Marks(Input inp, StringBuilder sb)
    {
        sb.Append("## Marks\n\n");
        int n = 0;
        foreach (var key in SessionAgentKeys(inp))
        {
            if (!inp.Logs.TryGetValue(key, out var lines)) continue;
            for (int i = 0; i < lines.Length; i++)
            {
                var m = Mark.Match(lines[i]);
                var pm = m.Success ? null : PeerMark.Match(lines[i]);
                if (!m.Success && (pm is null || !pm.Success)) continue;
                n++;
                string t = m.Success ? m.Groups["t"].Value : pm!.Groups["t"].Value;
                string who = m.Success ? "this player" : $"the partner (player {pm!.Groups["p"].Value})";
                string note = m.Success && m.Groups["n"].Success ? $" -- \"{m.Groups["n"].Value.Trim()}\"" : "";
                // WO-204 Act V: the partner's own note travels with his mark (W164 MarkNote) and is logged right after its ping
                if (!m.Success && PartnerNote(lines, i, pm!.Groups["m"].Value) is string pn) note = $" -- \"{pn}\"";
                sb.Append($"### {t} {who}{note}\n\n");
                double at = Secs(t);
                // the quest step: the last quest change this machine logged before the mark
                var step = lines.Take(i).Reverse().FirstOrDefault(x => x.Contains("WO137-CHANGE", StringComparison.Ordinal) || x.Contains("MP-W137", StringComparison.Ordinal) && x.Contains(" -> ", StringComparison.Ordinal));
                if (step is not null) sb.Append("- quest: `").Append(Cut(step, 220)).Append("`\n");
                // the NPCs the mark's own snapshot listed (MP-MARK-SNAP ... npc / event_actor)
                foreach (var s in lines.Skip(i).Take(80).Where(x => x.Contains("MP-MARK-SNAP", StringComparison.Ordinal) && (x.Contains(" npc ", StringComparison.Ordinal) || x.Contains("avatar", StringComparison.Ordinal))).Take(6))
                    sb.Append("- near: `").Append(Cut(s, 200)).Append("`\n");
                // toasts and the mod's warnings in the 10 s before
                foreach (var w in Window(lines, i, at, 10).Where(x => !x.Contains("STATS", StringComparison.Ordinal) && !x.Contains("STATUS", StringComparison.Ordinal)
                                                                    && !x.Contains("stats:", StringComparison.Ordinal)).Where(x => x.Contains("MP-TOAST", StringComparison.Ordinal) || x.Contains("FAULT", StringComparison.Ordinal)
                                                                    || x.Contains("[!]", StringComparison.Ordinal) || x.Contains("refused", StringComparison.Ordinal)
                                                                    || x.Contains("failed", StringComparison.OrdinalIgnoreCase)).Take(8))
                    sb.Append("- before: `").Append(Cut(w, 200)).Append("`\n");
                sb.Append('\n');
            }
        }
        if (n == 0) sb.Append("No mark in this bundle.\n\n");
    }

    private static readonly Regex NoteLine = new(@"MP-MARK note from player \d+: mark=(?<m>\S+) note=(?<n>.*)$", RegexOptions.Compiled);

    private static string? PartnerNote(string[] lines, int i, string markId)
    {
        for (int k = i + 1; k < lines.Length && k < i + 600; k++)
        {
            var n = NoteLine.Match(lines[k]);
            if (n.Success && n.Groups["m"].Value == markId) return n.Groups["n"].Value.Trim();
        }
        return null;
    }

    private static IEnumerable<string> Window(string[] lines, int i, double at, double secs)
    {
        for (int k = i - 1; k >= 0 && k > i - 4000; k--)
        {
            var m = AgentTime.Match(lines[k]);
            if (!m.Success) continue;
            double t = Secs(m.Groups[1].Value);
            if (at - t > secs || t > at) { if (at - t > secs) break; continue; }
            yield return lines[k];
        }
    }

    // ---- II.4: every quest failure in plain words ----
    private static void QuestFails(Input inp, StringBuilder sb)
    {
        sb.Append("## Quest failures\n\n");
        var fails = AgentLines(inp).Where(x => x.Contains("WO202-QUESTFAIL", StringComparison.Ordinal)).ToList();
        if (fails.Count == 0) { sb.Append("None traced on this machine.\n\n"); return; }
        foreach (var f in fails)
        {
            int bar = f.LastIndexOf(" | ", StringComparison.Ordinal);
            var t = AgentTime.Match(f);
            sb.Append("- ").Append(t.Success ? t.Groups[1].Value + " " : "").Append(bar > 0 ? f[(bar + 3)..] : Cut(f, 200));
            int w = f.IndexOf("| window: ", StringComparison.Ordinal);
            if (w > 0 && bar > w) sb.Append(" (").Append(Cut(f[(w + 10)..bar], 200)).Append(')');
            sb.Append('\n');
        }
        sb.Append('\n');
    }

    // ---- the desync scorecard ----
    public sealed record Score(string Measure, string Value, string Target);

    public static List<Score> Measure(Input inp)
    {
        var agent = AgentLines(inp).ToList();
        var native = NativeLines(inp).ToList();
        var o = new List<Score>();
        // WO-167's own counters (each agent run counts from 0): the last stats line of every agent log, summed
        int shownYes = 0, shownNo = 0;
        foreach (var key in SessionAgentKeys(inp))
        {
            if (!inp.Logs.TryGetValue(key, out var al)) continue;
            var st = al.LastOrDefault(x => x.Contains("MP-WO167-STATS", StringComparison.Ordinal) && x.Contains(" shown=", StringComparison.Ordinal));
            if (st is null) continue;
            if (int.TryParse(Rx(st, " shown"), out int y)) shownYes += y;
            if (int.TryParse(Rx(st, "no_anim"), out int nn)) shownNo += nn;
        }
        o.Add(new Score("host attacks animated on the joiner's copy", shownYes + shownNo == 0 ? "n/a (no host attack measured here)" : $"{shownYes} of {shownYes + shownNo} ({100.0 * shownYes / (shownYes + shownNo):F0} %)", ">= 95 %"));
        var swingMs = agent.Select(x => Regex.Match(x, @"WO202-SWING .*?delay_ms=(\d+)")).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        o.Add(new Score("delay from a host attack to its swing on the copy", swingMs.Count == 0 ? "n/a" : $"p90 {Pct(swingMs, 90)} ms ({swingMs.Count})", "p90 < 250 ms"));
        var ages = agent.Select(x => Regex.Match(x, @"WO187-FIGHTPOS role=joiner .*?stream_age_ms=(\d+)")).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        o.Add(new Score("fighting NPC position age", ages.Count == 0 ? "n/a" : $"median {Pct(ages, 50)} ms, p90 {Pct(ages, 90)} ms, p99 {Pct(ages, 99)} ms ({ages.Count})", "p90 < 200 ms"));
        int engineDeaths = Kcd(inp).Count(x => x.StartsWith("Soul died '", StringComparison.Ordinal));
        int announced = Kcd(inp).Count(x => x.Contains("witnessed alive->dead, announcing", StringComparison.Ordinal) || x.Contains("announced by dll", StringComparison.Ordinal));
        int notEngine = agent.Count(x => x.Contains("WO202-DEATH npc=", StringComparison.Ordinal) && x.Contains("NOT announced", StringComparison.Ordinal))
                        + native.Count(x => x.Contains("WO202-DEATH health 0", StringComparison.Ordinal));
        o.Add(new Score("deaths: the engine's own here / announced / held back as not the engine's", $"{engineDeaths} / {announced} / {notEngine}", "every announced death is the engine's"));
        int fell = agent.Count(x => x.Contains("WO202-DEATHFALL", StringComparison.Ordinal) && x.Contains("-> ragdoll down", StringComparison.Ordinal));
        int standing = agent.Count(x => x.Contains("WO202-DEATHFALL", StringComparison.Ordinal) && x.Contains("STILL UPRIGHT", StringComparison.Ordinal));
        o.Add(new Score("bodies a joiner's blow killed without the death reaction: laid down / still upright", $"{fell} / {standing}", "every one down"));
        var draws = agent.Count(x => x.Contains("WO202-DRAW", StringComparison.Ordinal) && x.Contains("played", StringComparison.Ordinal));
        o.Add(new Score("weapon draws played on copies", draws.ToString(CultureInfo.InvariantCulture), "every draw the host's NPC makes"));
        var w197 = agent.LastOrDefault(x => x.Contains("MP-W197-STATS", StringComparison.Ordinal));
        string recoils = w197 is null ? "n/a" : $"recoils {Rx(w197, "recoil_played")}, clinches {Rx(w197, "clinch_played")}";
        o.Add(new Score("recoils and clinches played", recoils, "every one the host's world plays"));
        var pings = agent.Select(x => Ping.Match(x)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        o.Add(new Score("message latency (the network ping)", pings.Count == 0 ? "n/a" : $"p99 {Pct(pings, 99)} ms", "p99 < 1000 ms"));
        // WO-204 Act V: the game's own queue lag, a line of its own; a backgrounded game's queue apart (a minimised game runs slow)
        var q = agent.Select(x => QueueLag.Match(x)).Where(m => m.Success).Select(m => (Ms: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Bg: m.Groups[2].Success)).ToList();
        var qf = q.Where(x => !x.Bg).Select(x => x.Ms).ToList();
        var qb = q.Where(x => x.Bg).Select(x => x.Ms).ToList();
        o.Add(new Score("the game's queue behind (game in front)", qf.Count == 0 ? "n/a" : $"p99 {Pct(qf, 99)} ms ({qf.Count})", "p99 < 1000 ms"));
        o.Add(new Score("the game's queue behind (game in the background)", qb.Count == 0 ? "n/a" : $"p99 {Pct(qb, 99)} ms ({qb.Count})", "(a backgrounded game, not the network)"));
        // WO-204 Act II / IV / III.1: the copies' bodies, the joiner's defence and blows, the conversations
        var overflows = Kcd(inp).Select(x => QueueOverflow.Match(x)).Where(m => m.Success).GroupBy(m => m.Groups[1].Value).OrderByDescending(g => g.Count()).ToList();
        int ovTotal = overflows.Sum(g => g.Count());
        o.Add(new Score("action-queue overflows on bodies (kcd.log)", ovTotal == 0 ? "0" : $"{ovTotal}: " + string.Join(", ", overflows.Take(4).Select(g => $"{g.Key} {g.Count()}")) + (overflows.Count > 4 ? ", ..." : ""), "0"));
        int tposes = agent.Concat(native).Count(x => x.Contains("WO204-TPOSE", StringComparison.Ordinal));
        o.Add(new Score("T-poses on copies (WO204-TPOSE)", tposes.ToString(CultureInfo.InvariantCulture), "0"));
        var drawMs = agent.Concat(Kcd(inp)).Select(x => Regex.Match(x, @"WO204-DRAW .*?delay_ms=(\d+)")).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        o.Add(new Score("weapon-draw delay on copies", drawMs.Count == 0 ? "n/a" : $"p90 {Pct(drawMs, 90)} ms ({drawMs.Count})", "p90 < 250 ms"));
        int dLocal = agent.Count(x => x.Contains("WO204-DEFENCE judged=local", StringComparison.Ordinal));
        int dHost = agent.Count(x => x.Contains("WO204-DEFENCE judged=host", StringComparison.Ordinal));
        o.Add(new Score("blows on the joiner judged by their own engine", dLocal + dHost == 0 ? "n/a" : $"{dLocal} of {dLocal + dHost} ({100.0 * dLocal / (dLocal + dHost):F0} %)", ">= 95 %"));
        int late = agent.Count(x => x.Contains("WO204-BLOW late", StringComparison.Ordinal));
        int blows = agent.Count(x => x.Contains("WO204-BLOW ", StringComparison.Ordinal));
        o.Add(new Score("the joiner's blows that arrived more than 1 s late", blows == 0 ? "n/a" : $"{late} of {blows}", "0"));
        int talks = agent.Count(x => x.Contains("WO203B-TALK this player's conversation", StringComparison.Ordinal));
        var w204 = agent.LastOrDefault(x => x.Contains("MP-W204-STATS", StringComparison.Ordinal));
        o.Add(new Score("conversations announced (barks kept out)", $"{talks}{(w204 is null ? "" : $" ({Rx(w204, "barks_kept_out")})")}", "real conversations only"));
        var seq = native.Where(x => x.Contains("WO194-SEQ", StringComparison.Ordinal)).ToList();
        int back = seq.Count(x => { var m = Regex.Match(x, @"time ([0-9.]+) -> ([0-9.]+) s"); return m.Success && double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) < double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); });
        int jumps = agent.Count(x => x.Contains("WO202-CLOCK", StringComparison.Ordinal) && x.Contains("jump forward", StringComparison.Ordinal));
        o.Add(new Score("shared cutscene seeks", $"{seq.Count} ({back} backward; {jumps} of them clock-v2 jumps)", "at most 2 per scene, never backward"));
        return o;
    }

    private static void Scorecard(Input inp, StringBuilder sb)
    {
        sb.Append("## Desync scorecard\n\n| measure | this machine | target |\n|---|---|---|\n");
        foreach (var s in Measure(inp)) sb.Append($"| {s.Measure} | {s.Value} | {s.Target} |\n");
        sb.Append('\n');
    }

    // ---- what stood out ----
    private static void StoodOut(Input inp, StringBuilder sb)
    {
        sb.Append("## What stood out\n\n");
        var now = Errors(Kcd(inp));
        var before = inp.Logs.Where(kv => kv.Key.StartsWith("logbackups/", StringComparison.Ordinal)).SelectMany(kv => kv.Value);
        var was = Errors(before);
        sb.Append("| errors and warnings (this launch's kcd.log) | count | the launch before |\n|---|---|---|\n");
        foreach (var kv in now.OrderByDescending(kv => kv.Value).Take(10))
            sb.Append($"| {kv.Key.Replace("|", "/")} | {kv.Value} | {(was.TryGetValue(kv.Key, out int b) ? b : 0)} |\n");
        int faults = NativeLines(inp).Count(x => x.Contains(" FAULT ", StringComparison.Ordinal) && !x.Contains("FAULT-SUM", StringComparison.Ordinal));
        sb.Append($"\nThe DLL's fault lines: {faults}.\n");
    }

    private static Dictionary<string, int> Errors(IEnumerable<string> lines)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var l in lines)
        {
            if (!l.StartsWith("[Error]", StringComparison.Ordinal) && !l.StartsWith("[Warning]", StringComparison.Ordinal)) continue;
            string k = Regex.Replace(Cut(l, 90), @"'[^']*'|\d+(\.\d+)?", "#");
            d[k] = d.TryGetValue(k, out int n) ? n + 1 : 1;
        }
        return d;
    }

    private static string Rx(string line, string key) => Regex.Match(line, key + @"=(\d+)") is { Success: true } m ? m.Groups[1].Value : "?";
    private static double Secs(string hms)
    {
        var p = hms.Split(':');
        return int.Parse(p[0], CultureInfo.InvariantCulture) * 3600 + int.Parse(p[1], CultureInfo.InvariantCulture) * 60 + double.Parse(p[2], CultureInfo.InvariantCulture);
    }
    private static int Pct(List<int> v, int p)
    {
        var s = v.OrderBy(x => x).ToList();
        return s[Math.Clamp((int)Math.Ceiling(p / 100.0 * s.Count) - 1, 0, s.Count - 1)];
    }
    private static string Cut(string s, int n) => (s.Length > n ? s[..n] + "..." : s).Replace("`", "'");
}
