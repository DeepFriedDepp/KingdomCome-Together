// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.LogPack;

/// <summary>
/// WO-210 0.3 / H.4: the instruments' counts in the briefing, one line each and a table of the worst bodies -- empty bodies and stale
/// postures from the animation probe (a copy's own family against its host NPC's, both written by the probe), and the native and agent
/// lines of the clinch loop, the deaths, the knockdowns, the riders, the conversation's votes and selections, and the owner's reseats.
/// </summary>
public static class W210Instruments
{
    private static readonly string[] Postures = ["lying", "sitting", "kneeling", "working"];
    private static readonly string[] Bases = ["standing", "moving", "combat"];

    public sealed class Stretch { public string Name = ""; public int N; public double Secs, Moved, Longest; }

    /// <summary>A copy whose FullBody holds nothing for more than 0.3 s within 60 m of the local player.</summary>
    public static List<Stretch> EmptyBodies(AnimStream s) => Runs(s, (smp, near) => smp.Fb is null && near, 0.3, needPlayer: true);

    /// <summary>A copy in a lying / sitting / kneeling / work family while the host NPC's family the owner applied is standing, moving or
    /// in a fight, for more than 0.5 s (the probe's own fields; an older probe has none and counts nothing here).</summary>
    public static List<Stretch> StalePostures(AnimStream s) =>
        Runs(s, (smp, _) => smp.Fam is string f && Postures.Contains(f) && smp.Hf is string h && Bases.Contains(h), 0.5, needPlayer: false);

    private static List<Stretch> Runs(AnimStream s, Func<AnimSample, bool, bool> pred, double minS, bool needPlayer)
    {
        var res = new List<Stretch>();
        foreach (var g in s.Bodies.Values.Where(b => b.Kind == "copy").GroupBy(b => b.Machine))
        {
            var player = s.Bodies.Values.FirstOrDefault(b => b.Machine == g.Key && b.Kind == "player")?.S;
            foreach (var ser in g)
            {
                var st = new Stretch { Name = ser.Name };
                long t0 = -1, tl = -1; double moved = 0; AnimSample? prev = null;
                void Close()
                {
                    if (t0 >= 0 && (tl - t0) / 1000.0 >= minS) { st.N++; st.Secs += (tl - t0) / 1000.0; st.Moved += moved; st.Longest = Math.Max(st.Longest, (tl - t0) / 1000.0); }
                    t0 = -1; moved = 0; prev = null;
                }
                foreach (var smp in ser.S)
                {
                    bool near = !needPlayer || player is null || Near(player, smp, 60f);
                    if (pred(smp, near))
                    {
                        if (t0 >= 0 && smp.T - tl > 1000) Close();
                        if (t0 < 0) t0 = smp.T;
                        if (prev is AnimSample p) moved += Math.Sqrt((smp.X - p.X) * (smp.X - p.X) + (smp.Y - p.Y) * (smp.Y - p.Y));
                        tl = smp.T; prev = smp;
                    }
                    else Close();
                }
                Close();
                if (st.N > 0) res.Add(st);
            }
        }
        return res.OrderByDescending(x => x.Secs).ToList();
    }

    private static bool Near(List<AnimSample> player, AnimSample smp, float r)
    {
        int lo = 0, hi = player.Count - 1;
        while (lo < hi) { int mid = (lo + hi) / 2; if (player[mid].T < smp.T) lo = mid + 1; else hi = mid; }
        if (player.Count == 0) return true;
        var p = player[lo];
        return Math.Sqrt((p.X - smp.X) * (p.X - smp.X) + (p.Y - smp.Y) * (p.Y - smp.Y)) <= r;
    }

    private static int Count(IEnumerable<string> lines, string needle) => lines.Count(x => x.Contains(needle, StringComparison.Ordinal));

    public static string Section(AnimStream? anim, IReadOnlyList<string> native, IReadOnlyList<string> agent) => Section(anim, native, agent, []);

    private static readonly Regex MarkTime = new(@"WO210-TIME mark=(?<id>\S+) this=(?<this>\S+) host=(?<host>\S+) gap_game_s=(?<gap>\S+) role=(?<role>\w+)", RegexOptions.Compiled);
    private static readonly Regex MarkSun = new(@"WO210-SUN mark=(?<id>\S+) sun=(?<sun>\d\d:\d\d)", RegexOptions.Compiled);

    /// <summary>H.5: every mark's clocks -- this machine's world time, the host's as this machine knows it, the gap, and the sun's time
    /// here (kcd.log's WO210-SUN under the same mark).</summary>
    public static List<(string Id, string This, string Host, string Gap, string Sun)> MarkClocks(IReadOnlyList<string> agent, IReadOnlyList<string> kcd)
    {
        var sun = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in kcd) { var m = MarkSun.Match(l); if (m.Success) sun[m.Groups["id"].Value] = m.Groups["sun"].Value; }
        var res = new List<(string, string, string, string, string)>();
        foreach (var l in agent)
        {
            var m = MarkTime.Match(l);
            if (!m.Success) continue;
            string id = m.Groups["id"].Value;
            res.Add((id, m.Groups["this"].Value, m.Groups["host"].Value, m.Groups["gap"].Value, sun.TryGetValue(id, out var s) ? s : "?"));
        }
        return res;
    }

    public static string Section(AnimStream? anim, IReadOnlyList<string> native, IReadOnlyList<string> agent, IReadOnlyList<string> kcd)
    {
        var sb = new StringBuilder("## WO-210 instruments\n\n");
        var empty = anim is null ? new List<Stretch>() : EmptyBodies(anim);
        var stale = anim is null ? new List<Stretch>() : StalePostures(anim);
        string Line(List<Stretch> l) => l.Count == 0 ? "none" : FormattableString.Invariant($"{l.Count} bod(ies), {l.Sum(x => x.N)} stretch(es), {l.Sum(x => x.Secs):0.#} s, {l.Sum(x => x.Moved):0.#} m moved");
        sb.Append("- **empty bodies** (a copy's whole body with no action over 0.3 s, within 60 m): ").Append(anim is null ? "no probe" : Line(empty)).Append('\n');
        sb.Append("- **stale postures** (a copy lying or sitting while its host NPC stands or walks, over 0.5 s): ").Append(anim is null ? "no probe" : Line(stale)).Append('\n');
        int reseats = Count(native, "WO210-STATE reseat"), unknown = native.Count(x => x.Contains("WO210-STATE reseat", StringComparison.Ordinal) && x.Contains("path=unknown", StringComparison.Ordinal));
        int handovers = Count(agent, "WO210-STATE handover");
        sb.Append(FormattableString.Invariant($"- **the owner's reseats:** {reseats} (path unknown: {unknown}); postures released the game's way: {handovers}\n"));
        int loops = Count(native, "WO210-CLINCH loop"), refusals = Count(native, "WO178-CLINCH refused"), clinchStarts = Count(native, "WO210-CLINCH start");
        sb.Append(FormattableString.Invariant($"- **clinches:** loops {loops}, refusal lines {refusals}, clinches played {clinchStarts}\n"));
        var deaths = native.Concat(agent).Where(x => x.Contains("WO173-HIT", StringComparison.Ordinal) && x.Contains("dead=yes", StringComparison.Ordinal)).ToList();
        int noReact = deaths.Count(x => x.Contains("victim-cannot-react", StringComparison.Ordinal));
        var checks = kcd.Where(x => x.Contains("WO210-DEATH check", StringComparison.Ordinal)).ToList();
        sb.Append(FormattableString.Invariant($"- **the joiner's killing blows:** {deaths.Count} (without the game's reaction: {noReact}); death falls (the last resort) {Count(agent, "WO202-DEATHFALL npc=")}; bodies finished the engine's way {native.Count(x => x.Contains("WO210-DEATH finish", StringComparison.Ordinal) && x.Contains("result=finished", StringComparison.Ordinal))}; death checks {checks.Count} (unlootable: {checks.Count(x => x.Contains("lootable=no", StringComparison.Ordinal))})\n"));
        sb.Append(FormattableString.Invariant($"- **knockdowns:** {Count(native, "WO210-KD fall")} fall(s), {Count(native, "WO210-KD up")} get-up(s); raw blows {Count(agent, "applied=raw")}\n"));
        var filtered = native.Where(x => x.Contains("WO210-HITLOCAL blow", StringComparison.Ordinal)).ToList();
        int oldWay = native.Count(x => x.Contains("PIPE: LocalHit", StringComparison.Ordinal) && x.Contains("by the player", StringComparison.Ordinal));
        sb.Append(FormattableString.Invariant($"- **this player's blows on copies** (E.2, judged on the host only): filtered here {filtered.Count} (impact played {filtered.Count(x => x.Contains("impact=played", StringComparison.Ordinal))}), sent to the host {Count(agent, "judge_there=1")}, still judged here (the old way) {oldWay}; the host: the joiner's numbers retired {Count(native, "correction=retired")}, blows its engine did not judge {native.Count(x => x.Contains("WO210-BLOW npc=", StringComparison.Ordinal) && x.Contains("not applied:", StringComparison.Ordinal))}\n"));
        sb.Append(FormattableString.Invariant($"- **riders:** mounted here {Count(kcd, "WO200-RIDER mount rider=")}, mounts given up {Count(kcd, "WO200-RIDER mount=gave-up")} (no horse here could carry him), put on the ground instead of the air {Count(kcd, "WO210-RIDER ground")}\n"));
        sb.Append(FormattableString.Invariant($"- **the host's recoils and paired hits played here** (E.7, the engine's own way): recoils {Count(native, "WO197-RECOIL kind=")}, paired hits {Count(native, "WO197-PAIR attacker=")}\n"));
        sb.Append(FormattableString.Invariant($"- **the joined conversation:** listener keys {Count(agent, "WO210-VOTE key")}, votes cast here {Count(agent, "WO204-CONV this player votes")}, votes shown here {Count(agent, "WO207-VOTE icon shown")}, the starter's rows sent {Count(agent, "WO210-HOVER sent")} / shown {Count(agent, "WO210-HOVER shown")}, picks shown {Count(agent, "WO210-PICK shown")}\n"));
        sb.Append(FormattableString.Invariant($"- **the clocks at the marks** (a gap over 2 game-minutes outside a sleep or wait): {Count(agent, "WO210-TIME gap")}\n"));
        var worst = stale.Take(8).ToList();
        if (worst.Count > 0)
        {
            sb.Append("\n| copy | stale stretches | seconds | moved while stale | longest |\n|---|---|---|---|---|\n");
            foreach (var w in worst) sb.Append(FormattableString.Invariant($"| {w.Name} | {w.N} | {w.Secs:0.#} s | {w.Moved:0.#} m | {w.Longest:0.#} s |\n"));
        }
        var worstEmpty = empty.Take(8).ToList();
        if (worstEmpty.Count > 0)
        {
            sb.Append("\n| copy | empty stretches | seconds |\n|---|---|---|\n");
            foreach (var w in worstEmpty) sb.Append(FormattableString.Invariant($"| {w.Name} | {w.N} | {w.Secs:0.#} s |\n"));
        }
        var clocks = MarkClocks(agent, kcd);
        if (clocks.Count > 0)
        {
            sb.Append("\n| mark | world time here | the host's | gap (game s) | the sun here |\n|---|---|---|---|---|\n");
            foreach (var c in clocks.TakeLast(12)) sb.Append($"| {c.Id} | {c.This} | {c.Host} | {c.Gap} | {c.Sun} |\n");
        }
        return sb.Append('\n').ToString();
    }
}
