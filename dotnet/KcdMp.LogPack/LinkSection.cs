// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.LogPack;

/// <summary>
/// WO-213 A.6 / G: the briefing's first section -- the link to the partner, from the agent's (and on the host the relay's) MP-LINK lines:
/// what our stream sends per kind, Steam's own send rate against its settings and what waits in it, how often the host's positions and
/// NPC states arrive (and the fighting NPCs'), the scheduler's counts, and every gap over a second with what came just before it.
/// </summary>
public static class LinkSection
{
    public const string Heading = "## Link";

    private static readonly Regex Time = new(@"^(\d\d:\d\d:\d\d)", RegexOptions.Compiled);
    private static readonly Regex Kv = new(@"(\w+)=([^\s|]+)", RegexOptions.Compiled);

    private static Dictionary<string, string> Fields(string line)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Kv.Matches(line)) d.TryAdd(m.Groups[1].Value, m.Groups[2].Value);
        return d;
    }

    private static double Num(Dictionary<string, string> f, string k) =>
        f.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : double.NaN;

    private static double Q(List<double> v, double q)
    {
        if (v.Count == 0) return double.NaN;
        v.Sort();
        return v[Math.Min(v.Count - 1, (int)Math.Round(q * (v.Count - 1)))];
    }

    private static string F0(double x) => double.IsNaN(x) ? "-" : x.ToString("F0", CultureInfo.InvariantCulture);

    public static string Section(IReadOnlyList<string> agent, IReadOnlyList<string> relay)
    {
        var sb = new StringBuilder();
        sb.Append(Heading).Append("\n\n");
        var kinds = agent.Where(l => l.Contains("MP-LINK kinds who=", StringComparison.Ordinal)).ToList();
        if (kinds.Count == 0)
        {
            sb.Append("No link numbers in this bundle (an agent before 0.64.0, or no connection).\n\n");
            return sb.ToString();
        }
        // ---- the stream, per kind (the whole session: each window's bytes/s times its length)
        var outB = new Dictionary<string, double>(StringComparer.Ordinal);
        var inB = new Dictionary<string, double>(StringComparer.Ordinal);
        double secs = 0;
        foreach (var l in kinds)
        {
            var f = Fields(l);
            double w = Num(f, "window_s");
            if (double.IsNaN(w)) continue;
            secs += w;
            foreach (var (key, d) in new[] { ("out", outB), ("in", inB) })
            {
                int at = l.IndexOf(" " + key + "=", StringComparison.Ordinal);
                if (at < 0) continue;
                string list = l[(at + key.Length + 2)..].Split(' ')[0];
                foreach (var part in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    int c = part.LastIndexOf(':');
                    if (c <= 0) continue;
                    var bm = part[(c + 1)..].Split('/');
                    if (double.TryParse(bm[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double bps))
                        d[part[..c]] = d.GetValueOrDefault(part[..c]) + bps * w;
                }
            }
        }
        string Top(Dictionary<string, double> d) => d.Count == 0 ? "-" :
            string.Join(", ", d.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key} {(kv.Value / Math.Max(1, secs)).ToString("F0", CultureInfo.InvariantCulture)} B/s"));
        double outTot = outB.Values.Sum() / Math.Max(1, secs), inTot = inB.Values.Sum() / Math.Max(1, secs);
        sb.Append($"- **our stream** ({F0(secs)} s): out {F0(outTot)} B/s, in {F0(inTot)} B/s\n");
        sb.Append($"  - out: {Top(outB)}\n  - in: {Top(inB)}\n");

        // ---- Steam's own numbers (this agent's link; on the host the relay's lines name each client)
        var steam = agent.Concat(relay).Where(l => l.Contains("MP-LINK steam who=", StringComparison.Ordinal)).Select(Fields).ToList();
        var cfg = agent.Concat(relay).LastOrDefault(l => l.Contains("MP-LINK steam-config", StringComparison.Ordinal));
        if (steam.Count > 0)
        {
            var rate = steam.Select(f => Num(f, "send_rate")).Where(x => !double.IsNaN(x)).ToList();
            var outBps = steam.Select(f => Num(f, "out_Bps")).Where(x => !double.IsNaN(x)).ToList();
            var pend = steam.Select(f => Num(f, "pend_rel") + Num(f, "pend_unrel")).Where(x => !double.IsNaN(x)).ToList();
            var qms = steam.Select(f => Num(f, "queue_ms")).Where(x => !double.IsNaN(x) && x < 1e9).ToList();
            var ping = steam.Select(f => Num(f, "ping")).Where(x => !double.IsNaN(x) && x >= 0).ToList();
            var ql = steam.Select(f => Math.Min(Num(f, "q_local"), Num(f, "q_remote"))).Where(x => !double.IsNaN(x) && x >= 0).ToList();
            sb.Append($"- **Steam**: send rate p50 {F0(Q(rate, 0.5))} B/s (min {F0(rate.Count > 0 ? rate.Min() : double.NaN)}); our out p50 {F0(Q(outBps, 0.5))} / max {F0(outBps.Count > 0 ? outBps.Max() : double.NaN)} B/s; " +
                      $"waiting in Steam p99 {F0(Q(pend, 0.99))} / max {F0(pend.Count > 0 ? pend.Max() : double.NaN)} B, queue p99 {F0(Q(qms, 0.99))} ms; ping p50 {F0(Q(ping, 0.5))} / p99 {F0(Q(ping, 0.99))} ms; " +
                      $"quality min {(ql.Count > 0 ? ql.Min().ToString("F2", CultureInfo.InvariantCulture) : "-")}\n");
        }
        else sb.Append("- **Steam**: not a Steam link (no MP-LINK steam lines)\n");
        if (cfg is not null) sb.Append($"  - settings: `{cfg[(cfg.IndexOf("MP-LINK", StringComparison.Ordinal))..].Trim()}`\n");

        // ---- arrivals (the receiver's view, at the socket)
        var arr = agent.Where(l => l.Contains("MP-LINK arrival who=", StringComparison.Ordinal)).Select(Fields).ToList();
        foreach (var kind in new[] { "pos", "npc-state" })
        {
            var k = arr.Where(f => f.GetValueOrDefault("kind") == kind).ToList();
            if (k.Count == 0) continue;
            var p50 = k.Select(f => Num(f, "ia_p50")).ToList(); var p90 = k.Select(f => Num(f, "ia_p90")).ToList();
            var a90 = k.Select(f => Num(f, "age_p90")).Where(x => !double.IsNaN(x)).ToList();
            double gaps = k.Sum(f => double.IsNaN(Num(f, "gaps_over_1s")) ? 0 : Num(f, "gaps_over_1s"));
            sb.Append($"- **{(kind == "pos" ? "the partner's positions" : "NPC states")} arriving**: every {F0(Q(p50, 0.5))} ms (p50 of windows), p90 {F0(Q(p90, 0.5))} ms; " +
                      $"age past the fastest p90 {F0(Q(a90, 0.5))} ms (worst window {F0(a90.Count > 0 ? a90.Max() : double.NaN)}); a moving body silent over 1 s: {gaps:F0}\n");
        }
        var bands = agent.Where(l => l.Contains("MP-LINK npc-bands who=", StringComparison.Ordinal)).ToList();
        if (bands.Count > 0)
        {
            var hz = new List<double>();
            foreach (var l in bands)
            {
                var m = Regex.Match(l, @" fight=(\d+):p50=([\d.]+)Hz");
                if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0
                    && double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) hz.Add(v);
            }
            sb.Append(hz.Count > 0 ? $"- **fighting NPCs** (within 15 m of a player, drawn, engaged or with an opponent): {Q(hz, 0.5).ToString("F1", CultureInfo.InvariantCulture)} Hz p50 over {hz.Count} windows (worst window {hz.Min().ToString("F1", CultureInfo.InvariantCulture)} Hz)\n"
                                   : "- **fighting NPCs**: none in this session\n");
            sb.Append(BandTable(bands, agent.Where(l => l.Contains("MP-LINK npc-moving who=", StringComparison.Ordinal)).ToList()));   // WO-214 G
        }
        // ---- the scheduler (this agent's way out; on the host the relay's per client)
        var outs = agent.Concat(relay).Where(l => (l.Contains("MP-LINK out who=", StringComparison.Ordinal) || l.Contains("MP-LINK relay client=", StringComparison.Ordinal)) && l.Contains(" sched=on", StringComparison.Ordinal)).Select(Fields).ToList();
        if (outs.Count > 0)
        {
            double Max(string k) => outs.Select(f => Num(f, k)).Where(x => !double.IsNaN(x)).DefaultIfEmpty(double.NaN).Max();
            sb.Append($"- **the scheduler**: state replaced before it went (latest wins) {F0(Max("coalesced"))}, dropped stale {F0(Max("stale_dropped"))}, repeats {F0(Max("repeats"))}; " +
                      $"state age at send p99 worst {F0(Max("state_age_p99"))} ms; events waiting at most {F0(Max("events_waiting"))}; held for the link's queue {F0(Max("pending_waits"))}, for the budget {F0(Max("budget_waits"))}\n");
        }
        else sb.Append("- **the scheduler**: off (or an older build)\n");

        // ---- gaps over a second, and what came just before each
        var gapLines = new List<(string T, string Kind, double Ms)>();
        foreach (var l in agent.Where(l => l.Contains("MP-LINK arrival who=", StringComparison.Ordinal)))
        {
            var f = Fields(l);
            if (f.GetValueOrDefault("kind") is not ("pos" or "npc-state")) continue;   // a stream's gaps (a moving body silent over 1 s); events are sporadic
            if (Num(f, "gaps_over_1s") > 0 && Time.Match(l) is { Success: true } tm) gapLines.Add((tm.Groups[1].Value, f.GetValueOrDefault("kind") ?? "?", Num(f, "gap_max_ms")));
        }
        if (gapLines.Count == 0) sb.Append("- **gaps over 1 s**: none\n");
        else
        {
            sb.Append($"- **gaps over 1 s**: {gapLines.Count} window(s)\n");
            var marks = new[] { "Gameplay started", "load hold", "mod emitter silent", "menu open", "paused", "HOLD on", "[ping]", "MP-JOIN", "toast", "relay" };
            foreach (var g in gapLines.Take(12))
            {
                int idx = agent.ToList().FindIndex(l => l.StartsWith(g.T, StringComparison.Ordinal) && l.Contains("MP-LINK arrival", StringComparison.Ordinal));
                string before = "-";
                if (idx > 0)
                    before = string.Join(" / ", agent.Take(idx).Reverse().Take(4000).Where(l => marks.Any(m => l.Contains(m, StringComparison.Ordinal)) && !l.Contains("MP-LINK", StringComparison.Ordinal))
                        .Take(2).Select(l => l.Length > 140 ? l[..140] : l));
                sb.Append($"  - {g.T} {g.Kind} longest {F0(g.Ms)} ms; before it: {before}\n");
            }
        }
        sb.Append('\n');
        return sb.ToString();
    }

    private static readonly string[] BandNames = ["fight", "40m", "150m", "far"];
    private static readonly string[] BandWords = ["fighting, within 15 m", "within 40 m", "within 150 m", "beyond 150 m"];

    /// <summary>
    /// WO-214 G: the band table in plain words -- per band, every NPC's rate at this machine (MP-LINK npc-bands: mostly idle bodies, which
    /// only the 2 s heartbeat sends) beside the walking ones' (MP-LINK npc-moving; an older build has none), and the verdict line: "walking
    /// NPCs within 40 m reached you N times a second".
    /// </summary>
    public static string BandTable(IReadOnlyList<string> bandLines, IReadOnlyList<string> movingLines)
    {
        var sb = new StringBuilder();
        static (List<double> P50, List<double> P10, int Bodies) Read(IReadOnlyList<string> lines, string band)
        {
            var p50 = new List<double>(); var p10 = new List<double>(); int bodies = 0;
            foreach (var l in lines)
            {
                var m = Regex.Match(l, " " + Regex.Escape(band) + @"=(\d+):p50=([\d.]+)Hz:p10=([\d.]+)Hz");
                if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n == 0) continue;
                if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
                    || !double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double b)) continue;
                p50.Add(a); p10.Add(b); bodies += n;
            }
            return (p50, p10, bodies);
        }
        var near = Read(movingLines, "40m");
        if (movingLines.Count > 0)
            sb.Append("- **walking NPCs**: ").Append(near.P50.Count == 0 ? "no walking NPC within 40 m was measured"
                : FormattableString.Invariant($"walking NPCs within 40 m reached you {Q(near.P50, 0.5):F0} times a second (the slowest tenth {Q(near.P10, 0.1):F0}; {near.Bodies} body-windows)")).Append('\n');
        sb.Append("\n| band | NPCs per window (p50) | every NPC, Hz p50 | walking NPCs, Hz p50 / slowest tenth |\n|---|---|---|---|\n");
        for (int b = 0; b < BandNames.Length; b++)
        {
            var all = Read(bandLines, BandNames[b]);
            var mov = Read(movingLines, BandNames[b]);
            if (all.P50.Count == 0 && mov.P50.Count == 0) continue;
            int perWin = all.P50.Count == 0 ? 0 : (int)Math.Round((double)all.Bodies / all.P50.Count);
            string walking = movingLines.Count == 0 ? "(not measured: an older build)" : mov.P50.Count == 0 ? "none walked"
                : FormattableString.Invariant($"{Q(mov.P50, 0.5):F1} / {Q(mov.P10, 0.1):F1} ({mov.Bodies} body-windows)");
            string every = all.P50.Count == 0 ? "-" : Q(all.P50, 0.5).ToString("F1", CultureInfo.InvariantCulture);
            sb.Append($"| {BandWords[b]} | {perWin} | {every} | {walking} |\n");
        }
        sb.Append('\n');
        return sb.ToString();
    }
}
