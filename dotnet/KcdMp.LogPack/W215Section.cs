// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.LogPack;

/// <summary>
/// WO-215 G: one line per contract of the animation substrate, in plain words, at the end of the Link section -- how the copies walked
/// (the game's own walk, the writer's corrections, the single-frame jumps), empty bodies, activities a copy could not show, and the
/// dormant and ghost bodies kept out. Read from the native log (WO215-STATUS, MP-NPCPULL, WO215-* lines), the agent's and the animation
/// probe's session file (anim/kcdmp-anim-session.jsonl, the whole session one body at a time).
/// </summary>
public static class W215Section
{
    private static readonly Regex Status = new(@"WO215-STATUS w215 .*?routed_frames=(\d+) orders=(\d+) faces=(\d+) route_corrections=(\d+)", RegexOptions.Compiled);
    private static readonly Regex Meter = new(@"meter windows=(\d+) within15=(\d+)", RegexOptions.Compiled);
    private static readonly Regex Census = new(@"D\.1 census dormant_now=(\d+) went_dormant=(\d+) woke=(\d+)", RegexOptions.Compiled);
    private static readonly Regex Writer = new(@"writer_frames off=(\d+) beast=(\d+) parented=(\d+) air=(\d+) fight=(\d+) refused=(\d+)", RegexOptions.Compiled);
    private static readonly Regex Pull = new(@"MP-NPCPULL npc=\S+ .*?max_cm=([\d.]+)", RegexOptions.Compiled);
    private static readonly Regex Missing = new(@"WO215-ACTIVITY-MISSING npc=(\S+) host=(\S+)", RegexOptions.Compiled);

    public static string Lines(AnimStream? session, IReadOnlyList<string> native, IReadOnlyList<string> agent, IReadOnlyList<string> kcd, bool noSessionNote = false)
    {
        var sb = new StringBuilder();
        sb.Append("- **the copies' bodies** (WO-215):\n");
        string? last = native.LastOrDefault(l => l.Contains("WO215-STATUS w215", StringComparison.Ordinal));
        // A: the walk
        int pulls = 0;
        foreach (var l in native)
        {
            if (!l.Contains("MP-NPCPULL", StringComparison.Ordinal)) continue;
            var m = Pull.Match(l);
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double cm) && cm > 50) pulls++;
        }
        if (last is not null && Status.Match(last) is { Success: true } st)
        {
            long routed = long.Parse(st.Groups[1].Value, CultureInfo.InvariantCulture), corr = long.Parse(st.Groups[4].Value, CultureInfo.InvariantCulture);
            string meter = Meter.Match(last) is { Success: true } mm && long.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture) > 0
                ? $"; their pace matched the host's within 15 % in {Pct(long.Parse(mm.Groups[2].Value, CultureInfo.InvariantCulture), long.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture))} of the half-second windows near you"
                : "";
            sb.Append($"  - walking: copies walked by the game's own walk ({routed:N0} body-frames, {st.Groups[2].Value} walk orders, {st.Groups[3].Value} stops facing the host's way)")
              .Append($"; the writer pulled a copy back {corr} time(s){meter}; single-frame jumps over 50 cm: {pulls} window(s)\n");
            if (Writer.Match(last) is { Success: true } w)
                sb.Append($"  - left to the old writer (body-frames): fighting {w.Groups[5].Value}, horses and animals {w.Groups[2].Value}, riders {w.Groups[3].Value}, falls {w.Groups[4].Value}, refused by the game {w.Groups[6].Value}\n");
        }
        else sb.Append($"  - walking: no route status in this log (an older build, or a host); single-frame jumps over 50 cm: {pulls} window(s)\n");
        // B: empty bodies over the whole session (the session probe), else nothing to say here (W210's own line says the detail window)
        if (session is not null)
        {
            var empty = W210Instruments.EmptyBodies(session);
            var miss = W210Instruments.ActivityMissing(session);
            sb.Append($"  - empty bodies over the whole session (the session probe): {empty.Count} copy(ies), {empty.Sum(e => e.N)} stretch(es), {empty.Sum(e => e.Secs):F1} s\n");
            sb.Append($"  - activities missing (a copy standing while its host NPC sits, lies, kneels or works, over 1 s): {miss.Count} copy(ies), {miss.Sum(e => e.Secs):F1} s")
              .Append(miss.Count > 0 ? " -- " + string.Join(", ", miss.OrderByDescending(e => e.Secs).Take(5).Select(e => $"{e.Name} {e.Secs:F0} s")) : "").Append('\n');
        }
        else sb.Append(noSessionNote
            ? "  - " + SessionRule.NoSessionFile + " (the session probe was off in this launch; an earlier session's file was left out): empty bodies and missing activities over the whole session not measured\n"
            : "  - the session probe's file is not in this bundle (empty bodies and missing activities over the whole session not measured)\n");
        // C: the owner's named refusals
        var refused = native.Select(l => Missing.Match(l)).Where(m => m.Success).GroupBy(m => m.Groups[1].Value).ToList();
        sb.Append($"  - activities this machine refused to show (named, the copy's own idle instead): {refused.Count} copy(ies)")
          .Append(refused.Count > 0 ? " -- " + string.Join(", ", refused.Take(6).Select(g => $"{g.Key} ({g.First().Groups[2].Value}) x{g.Count()}")) : "").Append('\n');
        // D / E: dormant and ghost bodies
        int dormantHere = native.Count(l => l.Contains("WO215-CENSUS", StringComparison.Ordinal) && l.Contains(" dormant (", StringComparison.Ordinal));
        int parked = kcd.Count(l => l.Contains("WO215-DORMANT", StringComparison.Ordinal));
        int ghosts = agent.Count(l => l.Contains("MP-W215 ghost ", StringComparison.Ordinal));
        int ghostFights = kcd.Count(l => l.Contains("WO215-GHOSTFIGHT", StringComparison.Ordinal));
        string census = last is not null && Census.Match(last) is { Success: true } c ? $" (now {c.Groups[1].Value}, woke {c.Groups[3].Value})" : "";
        sb.Append($"  - dormant bodies kept out of the stream: {dormantHere} here{census}; copies parked because their host NPC was dormant: {parked}; ghosts (the game could not set up their fight) parked: {ghosts}; fights against ghosts only, ended: {ghostFights}\n");
        return sb.ToString();
    }

    private static string Pct(long a, long b) => b > 0 ? (100.0 * a / b).ToString("F0", CultureInfo.InvariantCulture) + " %" : "-";
}
