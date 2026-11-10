// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.LogPack;

/// <summary>
/// WO-216 I: the rest of the body, one line per contract in plain words at the end of the Link section -- how the copies walked (the host's
/// own requests replayed, the corrections by cause), the brains running on the joiner (the contract: none), the outfits matched, the
/// riders on their horses, the poses played (looks, falls and get-ups, combat rows through the owner), the fights the players were not in,
/// and the census's dormant bodies' distances from the host. Read from the native log (WO216-STATUS, WO216-*, WO215-CENSUS) and the agent's.
/// </summary>
public static class W216Section
{
    private static readonly Regex Num = new(@"(?<k>[a-z_0-9]+)=(?<v>-?[\d.]+)", RegexOptions.Compiled);
    private static readonly Regex Dist = new(@"WO215-CENSUS npc=\S+ dormant .*?dist_host_m=(-?\d+)", RegexOptions.Compiled);

    private static Dictionary<string, double> Fields(string? section)
    {
        var d = new Dictionary<string, double>(StringComparer.Ordinal);
        if (section is null) return d;
        foreach (Match m in Num.Matches(section))
            if (double.TryParse(m.Groups["v"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) d.TryAdd(m.Groups["k"].Value, v);
        return d;
    }

    private static string? Part(string? line, string start, string? end)
    {
        if (line is null) return null;
        int i = line.IndexOf(start, StringComparison.Ordinal);
        if (i < 0) return null;
        int j = end is null ? -1 : line.IndexOf(end, i + start.Length, StringComparison.Ordinal);
        return j > 0 ? line[i..j] : line[i..];
    }

    private static string N(Dictionary<string, double> d, string k) => d.TryGetValue(k, out double v) ? v.ToString("0.##", CultureInfo.InvariantCulture) : "-";
    private static double V(Dictionary<string, double> d, string k) => d.TryGetValue(k, out double v) ? v : 0;

    public static string Lines(IReadOnlyList<string> native, IReadOnlyList<string> agent)
    {
        var sb = new StringBuilder();
        sb.Append("- **the rest of the body** (WO-216):\n");
        string? st = native.LastOrDefault(l => l.Contains("WO216-STATUS w216", StringComparison.Ordinal));
        if (st is null)
        {
            sb.Append("  - no WO-216 status in this log (an older build)\n");
            return sb.ToString();
        }
        var a1 = Fields(Part(st, "A.1 ", "| A.2"));
        var a0 = Fields(Part(st, "A.0 ", "| A.1"));
        var a2 = Fields(Part(st, "A.2 ", "| A.3"));
        var b = Fields(Part(st, "B no_brains=", "| D "));
        var dd = Fields(Part(st, "D outfits=", "| F.4/G"));
        var f = Fields(Part(st, "F.4/G ", null));
        var e = Fields(Part(st, "E horse_replay_frames", "| B "));
        bool joiner = V(a1, "lines_in") > 0 || V(b, "role") == 2;
        if (joiner)
        {
            double corr = V(a1, "route_corrections");
            sb.Append($"  - walking: the copies made their host NPC's own movement request {N(a1, "replay_orders")} time(s) ({N(a1, "replay_body_s")} body-seconds under it); ")
              .Append($"the stream pulled one back {corr:0} time(s), {N(a1, "per_body_min")} a body-minute (drift {N(a1, "drift")}, the link's stalls {N(a1, "stall")}, ")
              .Append($"no way here {N(a1, "no_path")}, refused {N(a1, "refused")}); a copy's way beside its host's: {N(a2, "parity_mean_m")} m on average\n");
            double samples = V(b, "samples"), nonzero = V(b, "nonzero");
            string last = Part(st, "last=", null)?.Replace("last=", "") ?? "-";
            sb.Append($"  - brains running here: {(nonzero == 0 ? "none" : $"{nonzero:0} of {samples:0} once-a-second checks found one")} ")
              .Append($"(the join froze {N(b, "sweep_paused")} and hid {N(b, "sweep_hidden")} bodies the host does not stream; {N(b, "local_spawns")} bodies this game spawned on its own, hidden at once)")
              .Append(nonzero > 0 ? $" -- the last: {last}" : "").Append('\n');
            var ag = Fields(agent.LastOrDefault(l => l.Contains("MP-W216-STATS", StringComparison.Ordinal)));
            sb.Append($"  - outfits: {N(ag, "dressed")} copies dressed as their host NPC, {N(ag, "dress_matched")} of {N(ag, "dress_checked")} checked matched slot for slot\n");
            sb.Append($"  - riders: horse copies moved by their host horse's own request for {N(e, "horse_replay_frames")} frames; riders on their horses: {native.Count(l => l.Contains("WO200-RIDER mount=ok", StringComparison.Ordinal))} mount(s) the engine's way\n");
            int falls = native.Count(l => l.Contains("WO210-KD fall", StringComparison.Ordinal)), ups = native.Count(l => l.Contains("WO210-KD up", StringComparison.Ordinal));
            sb.Append($"  - poses: combat rows played through the owner {N(f, "played")} (refused {N(f, "refused")}); falls {falls}, get-ups {ups}; ")
              .Append($"looks applied {agent.Count(l => l.Contains("MP-W216 look copy", StringComparison.Ordinal))}\n");
            sb.Append($"  - the activity line: {N(ag, "line_activity_applied")} activities and {N(ag, "line_hands_applied")} hands applied from it; the old rows retired {N(ag, "rows_retired")}\n");
        }
        else
        {
            sb.Append($"  - the host's own requests read: {N(a0, "active")} changes ({N(a0, "goal")} with their goal, {N(a0, "exact")} with an exact end, {N(a0, "pre_path")} on a path given with them)\n");
            sb.Append($"  - outfits: {N(dd, "changed")} bodies' worn sets sent\n");
        }
        var dist = native.Select(l => Dist.Match(l)).Where(m => m.Success).Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Where(x => x >= 0).OrderBy(x => x).ToList();
        if (dist.Count > 0)
            sb.Append($"  - dormant bodies kept out of the stream: {dist.Count}, from the host {dist[dist.Count / 2]:0} m p50 / {dist[Math.Min(dist.Count - 1, (int)(dist.Count * 0.9))]:0} m p90 / {dist[0]:0} m nearest\n");
        return sb.ToString();
    }
}
