// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.LogPack;

/// <summary>
/// WO-218 F.4: the briefing opens with this machine's queue -- how far behind the partner's world this game was shown (the agent's
/// MP-W207-Q lag, once a second with a partner): p50, p99, the worst, and a plain verdict; then WO-218's contracts as lines at the end
/// of the Link section: the pipe's batch (MP-PIPE: batches, requests a batch, failures), what the queue's pressure shed, and what one
/// frame's pipe turn served on the DLL (WO218-PIPE).
/// </summary>
public static class W218Section
{
    public const string Heading = "## The queue";

    private static readonly Regex Lag = new(@"MP-W207-Q lag_ms=(\d+)", RegexOptions.Compiled);
    private static readonly Regex ActLag = new(@" act_lag_ms=(\d+)", RegexOptions.Compiled);
    private static readonly Regex Role = new(@"MP-WO122 cfg .*?role=(host|joiner)", RegexOptions.Compiled);
    private static readonly Regex Num = new(@"(?<k>[a-z_0-9]+)=(?<v>-?[\d.]+)", RegexOptions.Compiled);

    private static Dictionary<string, double> Fields(string s)
    {
        var d = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (Match m in Num.Matches(s))
            if (double.TryParse(m.Groups["v"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) d.TryAdd(m.Groups["k"].Value, v);
        return d;
    }

    private static long Q(List<long> v, double q) => v.Count == 0 ? 0 : v[Math.Min(v.Count - 1, (int)Math.Round(q * (v.Count - 1)))];

    /// <summary>The queue's numbers from the agent's lines (pure; tested): samples, p50, p99, worst (ms), the share over 1 s.</summary>
    public static (int N, long P50, long P99, long Worst, double Over1sPct) QueueOf(IEnumerable<string> agent)
    {
        var v = new List<long>();
        foreach (var l in agent)
        {
            var m = Lag.Match(l);
            if (!m.Success || !long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long x)) continue;
            var a = ActLag.Match(l);   // WO-218: the activity lane beside the processor -- the queue is the later of both
            if (a.Success && long.TryParse(a.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long y) && y > x) x = y;
            v.Add(x);
        }
        v.Sort();
        if (v.Count == 0) return (0, 0, 0, 0, 0);
        return (v.Count, Q(v, 0.50), Q(v, 0.99), v[^1], 100.0 * v.Count(x => x > 1000) / v.Count);
    }

    /// <summary>The verdict in plain words (pure; tested).</summary>
    public static string Verdict(int n, long p99, long worst)
    {
        if (n == 0) return "no queue numbers in this bundle (no partner was joined, or an agent before 0.63.1)";
        if (p99 <= 250 && worst <= 1000) return "on time: the partner's world reached this game within a quarter of a second";
        if (p99 <= 1000) return string.Create(CultureInfo.InvariantCulture, $"mostly on time, late at moments (the worst {worst / 1000.0:F1} s)");
        return string.Create(CultureInfo.InvariantCulture,
            $"BEHIND: this game showed the partner's world up to {worst / 1000.0:F0} s late -- read every other number in this briefing with that in mind");
    }

    /// <summary>The opening section: the queue, first.</summary>
    public static string Section(IReadOnlyList<string> agent)
    {
        var sb = new StringBuilder();
        sb.Append(Heading).Append("\n\n");
        string role = agent.Select(l => Role.Match(l)).LastOrDefault(m => m.Success)?.Groups[1].Value ?? "this player";
        var (n, p50, p99, worst, over) = QueueOf(agent);
        sb.Append($"**{Verdict(n, p99, worst)}**\n\n");
        if (n > 0)
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $"- the {role}'s queue (how late the partner's world was shown here), {n} samples: p50 {p50} ms, p99 {p99} ms, worst {worst} ms; over 1 s {over:F1} % of the time\n\n"));
        return sb.ToString();
    }

    /// <summary>WO-218's contracts, one line each, at the end of the Link section.</summary>
    public static string Lines(IReadOnlyList<string> native, IReadOnlyList<string> agent)
    {
        var sb = new StringBuilder();
        sb.Append("- **the pipe and the queue** (WO-218):\n");
        var pipes = agent.Where(l => l.Contains("MP-PIPE on=", StringComparison.Ordinal)).ToList();
        if (pipes.Count == 0)
        {
            sb.Append("  - no WO-218 pipe line in this log (an older build)\n");
            return sb.ToString();
        }
        double batches = 0, subs = 0, failed = 0, merged = 0, rttMax = 0, subsMax = 0;
        foreach (var l in pipes)
        {
            var f = Fields(l[..(l.IndexOf(" | ", StringComparison.Ordinal) is int i and >= 0 ? i : l.Length)]);
            batches += f.GetValueOrDefault("batches"); subs += f.GetValueOrDefault("subs"); failed += f.GetValueOrDefault("failed");
            merged += f.GetValueOrDefault("merged"); rttMax = Math.Max(rttMax, f.GetValueOrDefault("rtt_ms_max")); subsMax = Math.Max(subsMax, f.GetValueOrDefault("subs_max"));
        }
        var last = Fields(pipes[^1]);
        string onOff = last.GetValueOrDefault("on") > 0 ? (last.GetValueOrDefault("dll") > 0 ? "on" : "on, but the DLL serves no batches (an older DLL?)") : "off";
        sb.Append(string.Create(CultureInfo.InvariantCulture,
            $"  - the pipe's batch ({onOff}): {batches:F0} batches carried {subs:F0} requests ({(batches > 0 ? subs / batches : 0):F1} a batch, at most {subsMax:F0}); the slowest answer {rttMax:F0} ms; {failed:F0} requests failed; {merged:F0} NPC states merged while waiting\n"));
        var shed = agent.LastOrDefault(l => l.Contains("MP-PIPE on=", StringComparison.Ordinal) && l.Contains("| shed", StringComparison.Ordinal));
        if (shed is not null)
        {
            var s = Fields(shed[shed.IndexOf("| shed", StringComparison.Ordinal)..]);
            double total = s.GetValueOrDefault("superseded_far") + s.GetValueOrDefault("far_rate") + s.GetValueOrDefault("mid");
            sb.Append(total > 0
                ? string.Create(CultureInfo.InvariantCulture, $"  - the queue's pressure shed {total:F0} states of far bodies ({s.GetValueOrDefault("superseded_far"):F0} superseded, {s.GetValueOrDefault("far_rate"):F0} by rate, {s.GetValueOrDefault("mid"):F0} at 20-40 m) in {s.GetValueOrDefault("on"):F0} stretch(es); asked again {s.GetValueOrDefault("asks"):F0} time(s); no event is ever shed\n")
                : "  - the queue's pressure shed nothing\n");
        }
        var dll = native.Where(l => l.Contains("WO218-PIPE window_s=", StringComparison.Ordinal)).Select(Fields).ToList();
        if (dll.Count > 0)
        {
            double mean = dll.Average(d => d.GetValueOrDefault("subs_per_frame_mean") + d.GetValueOrDefault("singles_per_frame_mean"));
            double max = dll.Max(d => d.GetValueOrDefault("subs_per_frame_max") + d.GetValueOrDefault("singles_per_frame_max"));
            double faulted = dll.Sum(d => d.GetValueOrDefault("faulted"));
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $"  - the DLL's pipe turn: {mean:F1} requests a frame on average, at most {max:F0}; {faulted:F0} batched request(s) faulted\n"));
        }
        return sb.ToString();
    }
}
