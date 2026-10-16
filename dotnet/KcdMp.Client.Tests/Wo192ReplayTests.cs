// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-192: offline replays of the 0.60.9 session (the testers' bundles, never committed, unpacked in logs/w192/host and logs/w192/joiner;
/// a replay passes with a note when they are not there). Every result is printed (the findings' numbers are these lines).
///   1  every snapshot refused at 19:50-19:53 and 20:04-20:09 would be asked at once; the 20:09 rejoin restores a snapshot of at most one
///      host save before the reload (it restored one of 19:53, four host saves back)
///   4a the partner's position updates reached the DLL (its "moved to" lines), and each was an entity move only
///   4b the shared skips where the joiner woke before the host, and the hold each gets
/// </summary>
public class Wo192ReplayTests
{
    private readonly ITestOutputHelper _out;
    public Wo192ReplayTests(ITestOutputHelper o) { _out = o; }

    private static string? Dir(string bundle)
    {
        string? d = AppContext.BaseDirectory;
        while (d is not null && !Directory.Exists(Path.Combine(d, "dotnet", "KcdMp.Client"))) d = Path.GetDirectoryName(d);
        if (d is null) return null;
        string p = Path.Combine(d, "logs", "w192", bundle);
        return File.Exists(Path.Combine(p, "agent.log")) ? p : null;
    }

    private static readonly Regex Head = new(@"^(\d\d):(\d\d):(\d\d\.\d\d\d) m=\d+ off=(\S+) ", RegexOptions.Compiled);

    /// <summary>(wall seconds of this machine, the relay offset in s -- relay minus this machine; NaN unknown).</summary>
    private static (double T, double Off) Stamp(string l)
    {
        var m = Head.Match(l);
        if (!m.Success) return (-1, double.NaN);
        double t = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        string o = m.Groups[4].Value.Replace("-+", "", StringComparison.Ordinal);
        return (t, double.TryParse(o, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) ? ms / 1000.0 : double.NaN);
    }

    private static string Hms(double s) => TimeSpan.FromSeconds(s).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    [Fact]
    public void Part1_every_refused_snapshot_is_asked_at_once_and_the_rejoin_loses_at_most_one_host_save()
    {
        string? j = Dir("joiner"), h = Dir("host");
        if (j is null || h is null) { _out.WriteLine("the 0.60.9 bundles are not unpacked in logs/w192 -- skipped"); return; }
        string[] jl = File.ReadAllLines(Path.Combine(j, "agent.log"));
        string[] hl = File.ReadAllLines(Path.Combine(h, "agent.log"));

        // the refusals as logged: WO-189 A3's positioning gate on every ask (host saves, the ESC menu, the host's reload)
        var refused = jl.Where(l => l.Contains("a scene is still positioning the host's copies", StringComparison.Ordinal)
                                    && (l.Contains("WO184-SNAP", StringComparison.Ordinal) || l.Contains("final snapshot was NOT paired", StringComparison.Ordinal)))
                        .Select(l => (T: Stamp(l).T, Line: l)).ToList();
        _out.WriteLine($"as logged: {refused.Count} snapshot asks refused by the positioning gate, {Hms(refused.First().T)} .. {Hms(refused.Last().T)}");
        Assert.Contains(refused, r => Hms(r.T).StartsWith("19:50", StringComparison.Ordinal));
        Assert.Contains(refused, r => Hms(r.T).StartsWith("20:04", StringComparison.Ordinal));
        // each refusal came after the engine's own checks passed (TakeAsync reads pending and CanSave first): WO-192's two blocks are clear there
        foreach (var r in refused)
        {
            string block = Wo192Rules.AskBlock(savePending: false, inWorld: true, joining: false, rewinding: false);
            Assert.Equal("", block);
        }
        _out.WriteLine($"WO-192: all {refused.Count} asked at once (no block: no save pending, in the host's world; the scene is a WO192-SAVE line)");
        Assert.DoesNotContain(jl, l => l.Contains("WO189-POSITIONING", StringComparison.Ordinal));   // D's 20 s bound never fired
        _out.WriteLine("WO-189 D's 20 s bound: no WO189-POSITIONING line in the joiner's log (it never fired)");

        // the model: every host save the joiner hears while in the host's world (not loading, joining, or in the host's reload) pairs ~2.5 s later
        double off = jl.Select(l => Stamp(l).Off).Where(o => !double.IsNaN(o)).OrderBy(o => o).ElementAt(jl.Count(l => !double.IsNaN(Stamp(l).Off)) / 2);
        var saves = new List<(uint Seq, double JT)>();
        var rx = new Regex(@"-> WorldSaved seq=(\d+) md5=");
        foreach (string l in hl) { var m = rx.Match(l); if (m.Success) saves.Add((uint.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Stamp(l).T - off)); }
        // the joiner's world: in from "ready sent for join", out from "the host started a load" until the next "ready sent"
        var edges = jl.Select(l => (T: Stamp(l).T, In: l.Contains("MP-JOIN joiner: ready sent for join", StringComparison.Ordinal),
                                    Out: l.Contains("MP-HENRY joiner: the host started a load", StringComparison.Ordinal)))
                      .Where(e => e.In || e.Out).ToList();
        bool InWorld(double t) { bool inw = false; foreach (var e in edges) { if (e.T > t) break; inw = e.In; } return inw; }
        var reload = edges.Last(e => e.Out && Hms(e.T).StartsWith("20:08", StringComparison.Ordinal)).T;
        var pairs = saves.Where(s => InWorld(s.JT)).Select(s => (s.Seq, At: s.JT + 2.5)).ToList();
        foreach (var s in saves) _out.WriteLine(FormattableString.Invariant($"model: host save seq={s.Seq} heard {Hms(s.JT)} -> {(InWorld(s.JT) ? $"paired {Hms(s.JT + 2.5)}" : "not in the host's world (rule b)")}"));
        var lastBefore = pairs.Where(p => p.At <= reload).OrderBy(p => p.At).Last();
        uint newestBefore = saves.Where(s => s.JT <= reload).Max(s => s.Seq);
        _out.WriteLine($"the host's reload {Hms(reload)}: its newest save before it seq={newestBefore}; WO-192's newest pair seq={lastBefore.Seq} ({Hms(lastBefore.At)})");
        Assert.True(lastBefore.Seq + 1 >= newestBefore, "the rejoin would restore a snapshot from at most one host save before the reload");
        // as logged: the 20:09 rejoin restored the snapshot of the 19:53 join (four host saves back)
        string restore = jl.First(l => l.Contains("RESTORE snapshot", StringComparison.Ordinal) && Hms(Stamp(l).T).StartsWith("20:09", StringComparison.Ordinal));
        _out.WriteLine("as logged: " + restore[(restore.IndexOf("MP-HENRY", StringComparison.Ordinal))..]);
        Assert.Contains("branch save #4", restore);
    }

    [Fact]
    public void Part4a_the_partners_position_updates_reached_the_dll_as_entity_moves()
    {
        string? h = Dir("host");
        if (h is null) { _out.WriteLine("the 0.60.9 host bundle is not unpacked in logs/w192 -- skipped"); return; }
        string stats = File.ReadLines(Path.Combine(h, "agent.log")).Last(l => l.Contains("MP-WO171-STATS", StringComparison.Ordinal));
        int sends = int.Parse(Regex.Match(stats, @"move_sends=(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        int moved = 0, spawned = 0, again = 0;
        foreach (var f in Directory.EnumerateFiles(h, "kcdmp-native*.log"))
            foreach (string l in File.ReadLines(f))
            {
                if (l.Contains("WO164-MAPMARK pin owner=0 moved to", StringComparison.Ordinal)) moved++;
                else if (l.Contains("WO164-MAPMARK pin owner=0 spawned", StringComparison.Ordinal)) spawned++;
                else if (l.Contains("WO171-MARK pin owner=0 picture", StringComparison.Ordinal)) again++;
            }
        _out.WriteLine($"agent move_sends={sends}; the DLL: {moved} 'moved to' lines (one per 30 moves: ~{moved * 30} moves), {spawned} spawns, {again} marks made again (a new picture)");
        Assert.True(sends > 500);
        Assert.True(moved * 30 >= sends / 2, "the position updates reached the DLL");
        // the bundle's native logs span several launches; across them, tens of times more moves than pin entities: a move moved the entity only
        // (WO-192: a move is a new pin entity, which the map's mark follows)
        Assert.True(moved * 30 > 10 * spawned, "every move was an entity move on the same pin");
    }

    [Fact]
    public void Part4b_the_joiner_woke_before_the_host_in_the_shared_waits_and_each_is_held()
    {
        string? j = Dir("joiner"), h = Dir("host");
        if (j is null || h is null) { _out.WriteLine("the 0.60.9 bundles are not unpacked in logs/w192 -- skipped"); return; }
        var jw = File.ReadLines(Path.Combine(j, "agent.log")).Where(l => l.Contains("MP-W140 this player woke (", StringComparison.Ordinal))
                     .Select(l => { var s = Stamp(l); return (HostT: s.T + s.Off, Kind: l.Contains("(wait)", StringComparison.Ordinal) ? "wait" : "sleep"); }).ToList();
        var hw = File.ReadLines(Path.Combine(h, "agent.log")).Where(l => l.Contains("MP-W140 this player woke (", StringComparison.Ordinal)).Select(l => Stamp(l).T).ToList();
        int held = 0;
        foreach (var w in jw)
        {
            double host = hw.OrderBy(x => Math.Abs(x - w.HostT)).First();
            if (Math.Abs(host - w.HostT) > 30) continue;
            long hold = Wo192Rules.HoldFor((long)(w.HostT * 1000), (long)(host * 1000));
            if (hold > 0) held++;
            _out.WriteLine(FormattableString.Invariant($"{w.Kind}: the joiner woke {Hms(w.HostT)} (host clock), the host {Hms(host)} -> {(w.HostT < host ? $"{host - w.HostT:F1} s early: held {hold / 1000.0:F1} s" : "after the host: no hold")}"));
        }
        Assert.Equal(2, held);   // the two shared waits (2 h: 2.3 s early, the WO's "about 3 s"; 6 h: 6.8 s early)
    }
}
