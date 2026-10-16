// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-189: offline replays of the 0.60.5 session (the host's kcd.log; the joiner's agent.prev.log and logbackups/kcd.log) through the new rules.
/// The bundles are the maintainer's private logs (never committed), unpacked in logs/w189/host and logs/w189/guest; a replay passes with a note
/// when they are not there. Every result is printed (the findings' table is these lines).
///   B  the host's profile lines -> exactly the quest profiles' changes in play (the tutorial's barrier: 11 pairs), never weather_* / event_*
///   C  the joiner's end fader flagged 10 s after "initialized", with its barrier holder
///   D  the post-duel scene's positioning bounded at 20 s (it ran 750 s)
///   A  A1 at the ESC snapshot's wait end, A2 within a minute of the engine's wait (A3 and A4: removed by WO-192 -- asked at once)
/// </summary>
public class Wo189ReplayTests
{
    private readonly ITestOutputHelper _out;
    public Wo189ReplayTests(ITestOutputHelper o) { _out = o; }

    private static string? Log(string bundle, string file)
    {
        string? d = AppContext.BaseDirectory;
        while (d is not null && !Directory.Exists(Path.Combine(d, "dotnet", "KcdMp.Client"))) d = Path.GetDirectoryName(d);
        if (d is null) return null;
        string p = Path.Combine(d, "logs", "w189", bundle, file);
        return File.Exists(p) ? p : null;
    }

    private static readonly Regex GameT = new(@"\bt=(\d+(?:\.\d+)?)\s*$", RegexOptions.Compiled);
    private static readonly Regex DataT = new(@"^\[KCD2-MP-DATA\] v2 \d+ (\d+(?:\.\d+)?) (-?\d+\.\d+) (-?\d+\.\d+) (-?\d+\.\d+) (-?\d+\.\d+) ", RegexOptions.Compiled);
    private static readonly Regex AgentT = new(@"^(\d\d):(\d\d):(\d\d\.\d\d\d) ", RegexOptions.Compiled);
    private static readonly Regex AgentPos = new(@"\[pos\] (-?\d+,\d) (-?\d+,\d) (-?\d+,\d)\s+rot=(-?\d+,\d\d)", RegexOptions.Compiled);

    private static double Wall(string l) { var m = AgentT.Match(l); return m.Success ? int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : -1; }
    private static string Hms(double s) { var ts = TimeSpan.FromSeconds(s); return ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture); }

    /// <summary>The one constant offset of a launch: wall = t + offset (the agent's [pos] lines against the mod's DATA lines; the median of unique matches).</summary>
    private static double Offset(string kcd, string agent)
    {
        var byKey = new Dictionary<string, List<double>>();
        foreach (string l in File.ReadLines(kcd))
        {
            var m = DataT.Match(l);
            if (!m.Success) continue;
            string k = string.Join("|", Enumerable.Range(2, 3).Select(i => Math.Round(double.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture), 1).ToString("F1", CultureInfo.InvariantCulture)))
                       + "|" + Math.Round(double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture), 2).ToString("F2", CultureInfo.InvariantCulture);
            (byKey.TryGetValue(k, out var list) ? list : byKey[k] = []).Add(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        var offs = new List<double>();
        foreach (string l in File.ReadLines(agent))
        {
            var p = AgentPos.Match(l);
            if (!p.Success) continue;
            string k = string.Join("|", Enumerable.Range(1, 3).Select(i => p.Groups[i].Value.Replace(',', '.'))) + "|" + p.Groups[4].Value.Replace(',', '.');
            if (!byKey.TryGetValue(k, out var ts) || ts.Max() - ts.Min() > 2) continue;   // the same pose for long: ambiguous
            offs.Add(Wall(l) - ts.Average());
        }
        offs.Sort();
        return offs.Count == 0 ? double.NaN : offs[offs.Count / 2];
    }

    // ================================================================ B: the host

    [Fact]
    public void B_the_hosts_log_forwards_the_tutorial_barrier_eleven_pairs_and_no_weather_or_event()
    {
        string? kcd = Log("host", "kcd.log");
        if (kcd is null) { _out.WriteLine("SKIPPED: logs/w189/host is not unpacked here"); return; }
        var cap = new Wo189Rules.ProfileCapture();
        var fwd = new List<(int Op, string Name)>();
        var seen = new Dictionary<string, int>();
        foreach (string l in File.ReadLines(kcd))
        {
            if (l.StartsWith("Loading saved game '", StringComparison.Ordinal) || l.StartsWith("[CryAction] LoadGame:", StringComparison.Ordinal)) cap.Loading();
            if (l.StartsWith("Gameplay started", StringComparison.Ordinal) && l.Length < 40) cap.GameplayStarted();
            var d = cap.Feed(l, Wo189Rules.QuestProfiles);
            if (d is null) continue;
            seen[d.Why] = seen.GetValueOrDefault(d.Why) + 1;
            if (d.Forward) fwd.Add((d.Op, d.Name));
        }
        foreach (var kv in seen.OrderByDescending(k => k.Value)) _out.WriteLine($"B decision '{kv.Key}': {kv.Value}");
        foreach (var g in fwd.GroupBy(f => f.Name)) _out.WriteLine($"B forwarded {g.Key}: {string.Concat(g.Select(f => f.Op > 0 ? "+" : "-"))}");
        var barrier = fwd.Where(f => f.Name == "masterstrikeTutorial_arenaBarrier").Select(f => f.Op).ToList();
        Assert.Equal(22, barrier.Count);
        for (int i = 0; i < barrier.Count; i++) Assert.Equal(i % 2 == 0 ? 1 : -1, barrier[i]);   // on, off, on, off ...
        Assert.DoesNotContain(fwd, f => f.Name.StartsWith("weather_", StringComparison.Ordinal) || f.Name.StartsWith("event_", StringComparison.Ordinal));
    }

    // ================================================================ the joiner: C and D as logged, then A

    private sealed class JoinerRun
    {
        public readonly List<(double T, string Name, string Holder)> CutFlags = [];
        public readonly List<(double T, double OldestT)> PosActs = [];
        public readonly List<(double T, bool Open)> PosOpenAt = [];   // per line: an open scene positioning (A3's view)
        public double PendingSinceT = -1, PositioningLateT = -1, PositioningLateS;
        public readonly List<(double T, bool Blocked)> AskBlockAt = [];

        public bool OpenAt(double t)
        {
            bool open = false;
            foreach (var (tt, o) in PosOpenAt) { if (tt > t) break; open = o; }
            return open;
        }
    }

    /// <summary><paramref name="modelD"/>: D's act ends the scene it acts on (as the save lock's FF cancel ended it); the engine's own later end of it is then skipped.</summary>
    private static JoinerRun RunJoiner(string kcd, bool modelD = false)
    {
        int modelEnded = 0;
        var r = new JoinerRun();
        var cut = new Wo189Rules.CutsceneWatch();
        var pos = new Wo189Rules.PositioningWatch();
        var clock = new Wo184Rules.ConversationClock();
        string playing = "";
        double t = 0;
        bool lastOpen = false;
        foreach (string l in File.ReadLines(kcd))
        {
            var gt = GameT.Match(l);
            if (gt.Success) t = Math.Max(t, double.Parse(gt.Groups[1].Value, CultureInfo.InvariantCulture));
            else { var dt = DataT.Match(l); if (dt.Success) t = Math.Max(t, double.Parse(dt.Groups[1].Value, CultureInfo.InvariantCulture)); }
            long ms = (long)(t * 1000);
            if (l.StartsWith("Gameplay started", StringComparison.Ordinal) && l.Length < 40) { cut.Clear(); pos.Reset(); clock.Reset("load"); playing = ""; }
            var d = Wo184Rules.Parse(l);
            if (d.Kind != Wo184Rules.DialogKind.None) clock.Feed(d, ms);
            clock.Tick(ms);
            if (Wo189Rules.TryParseInitialized(l, out string ty, out string nm, out string holder)) cut.Initialized(ty, nm, holder, ms);
            if (LogTailGameTransport.TryParseSceneStage(l, out string st, out _, out string sn))
            {
                cut.Stage(st, sn);
                if (st == "PlayCutscene") playing = sn;
                else if (st is "OnCutsceneEnd" or "ReleaseScene" or "Interrupt" && sn == playing) playing = "";
            }
            switch (Wo167Rules.IsmStage(l))
            {
                case "IsmEnqueue": pos.Enqueued(ms); break;
                case "IsmEnd": case "IsmInterrupt": if (modelEnded > 0) modelEnded--; else pos.Ended(); break;
            }
            if (l.StartsWith("Readiness observer 'BeforeGameSave'", StringComparison.Ordinal) && l.Contains("started async waiting", StringComparison.Ordinal)) r.PendingSinceT = t;
            if (l.Contains("NPC positioning took longer than expected. Finished in ", StringComparison.Ordinal))
            {
                r.PositioningLateT = t;
                r.PositioningLateS = double.Parse(l[(l.IndexOf("Finished in ", StringComparison.Ordinal) + 12)..].Split(' ')[0], CultureInfo.InvariantCulture);
            }
            bool open = pos.Open > 0 && !clock.Running && ms - pos.OldestMs >= 1_000;
            if (open != lastOpen) { r.PosOpenAt.Add((t, open)); lastOpen = open; }
            if (cut.Due(ms)) r.CutFlags.Add((t, cut.Name, cut.Holder));
            long oldest = pos.OldestMs;
            if (pos.Due(ms, clock.Running, playing.Length > 0, null))
            {
                r.PosActs.Add((t, oldest / 1000.0));
                if (modelD) { pos.Ended(); modelEnded++; }
            }
        }
        return r;
    }

    [Fact]
    public void C_and_D_the_joiners_log_flags_the_end_fader_and_bounds_the_positioning()
    {
        string? kcd = Log("guest", Path.Combine("logbackups", "kcd.log"));
        if (kcd is null) { _out.WriteLine("SKIPPED: logs/w189/guest is not unpacked here"); return; }
        var r = RunJoiner(kcd);
        foreach (var f in r.CutFlags) _out.WriteLine(FormattableString.Invariant($"C flag t={f.T:F1} name={f.Name} holder={f.Holder}"));
        foreach (var a in r.PosActs) _out.WriteLine(FormattableString.Invariant($"D act t={a.T:F1} (scene enqueued t={a.OldestT:F1}, after {a.T - a.OldestT:F1} s)"));
        _out.WriteLine(FormattableString.Invariant($"as logged: positioning finished late at t={r.PositioningLateT:F1} after {r.PositioningLateS:F2} s (began t={r.PositioningLateT - r.PositioningLateS:F1})"));
        // C: the barrier's end fader, 10 s after t~1703.4, with its holder
        var konec = r.CutFlags.Where(f => f.Name == "masterstrikeTutorial_konecTutorialu_vasko").ToList();
        Assert.Single(konec);
        Assert.Equal("masterstrikeTutorial_unstreamBarrierCH", konec[0].Holder);
        Assert.InRange(konec[0].T, 1713.3, 1714.5);
        // D: the post-duel scene (positioning began t~953) bounded within 20 s of its start
        double began = r.PositioningLateT - r.PositioningLateS;
        Assert.InRange(began, 952.0, 954.5);
        var first = r.PosActs.FirstOrDefault(a => a.T >= began - 5);
        Assert.True(first.T > 0, "D never acted after the positioning began");
        Assert.InRange(first.T - began, 0, 20.5);
    }

    [Fact]
    public void A_the_joiners_save_never_stays_pending_and_the_quit_pairs()
    {
        string? kcd = Log("guest", Path.Combine("logbackups", "kcd.log"));
        string? agent = Log("guest", "agent.prev.log");
        if (kcd is null || agent is null) { _out.WriteLine("SKIPPED: logs/w189/guest is not unpacked here"); return; }
        double off = Offset(kcd, agent);
        Assert.False(double.IsNaN(off), "no clock offset from the [pos] / DATA lines");
        _out.WriteLine(FormattableString.Invariant($"clock: wall = t + {off:F2} s (t=0 at {Hms(off)})"));
        var r = RunJoiner(kcd);

        double escAsk = -1, escWaitEnd = -1, joinPair = -1, quitMenu = -1, quitDialog = -1;
        foreach (string l in File.ReadLines(agent))
        {
            if (escAsk < 0 && l.Contains("WO184-FINAL the ESC menu: one final snapshot", StringComparison.Ordinal)) escAsk = Wall(l);
            if (escWaitEnd < 0 && l.Contains("its file had not come when the leave went on", StringComparison.Ordinal)) escWaitEnd = Wall(l);
            if (joinPair < 0 && l.Contains("stored join snapshot", StringComparison.Ordinal)) joinPair = Wall(l);
            if (l.Contains("WO184-FINAL the ESC menu opened (#2)", StringComparison.Ordinal)) quitMenu = Wall(l);
            if (l.Contains("WO184-FINAL the game's exit dialog opened", StringComparison.Ordinal)) quitDialog = Wall(l);
        }
        _out.WriteLine($"as logged: join pair {Hms(joinPair)}, ESC snapshot asked {Hms(escAsk)}, its wait ended {Hms(escWaitEnd)}, quit menu {Hms(quitMenu)}, exit dialog {Hms(quitDialog)}");

        // A1: the wait that ended without its file cancels the engine's pending save then (20:16:06)
        _out.WriteLine($"A1 cancel at {Hms(escWaitEnd)}");
        Assert.Equal("20:16:06", Hms(escWaitEnd));

        // A2: the engine's own wait began at t~1703.4; the watchdog (5 s period, any phase) cancels within 65 s of it
        double since = r.PendingSinceT + off;
        var w = new Wo189Rules.PendingWatch();
        double due = -1;
        for (double s = since; s < since + 120 && due < 0; s += 5)
            if (w.Feed(true, (long)(s * 1000), (long)(since * 1000))) due = s;
        _out.WriteLine($"A2 engine waiting since {Hms(since)} -> the watchdog cancels at {Hms(due)} (at the latest {Hms(since + 65)})");
        Assert.InRange(due - since, 60, 65);
        Assert.True(due <= 20 * 3600 + 16 * 60 + 56, "A2 later than 20:16:56");

        // A3 and A4 were removed by WO-192 (the joiner's ask has two hard blocks only; the host's autosave replaced the own snapshot): at the
        // ESC snapshot's moment the scene still positioning the host's copies is a log line, and the snapshot is asked at once
        double escT = escAsk - off;
        bool open = r.OpenAt(escT);
        string block = Wo192Rules.AskBlock(savePending: false, inWorld: true, joining: false, rewinding: false);
        string note = Wo192Rules.SceneNote(false, open ? 1 : 0, null, "", "");
        _out.WriteLine(FormattableString.Invariant($"WO-192 at {Hms(escAsk)} (t={escT:F1}): positioning open={open} -> requested at once; {note}"));
        Assert.True(open);
        Assert.Equal("", block);
        Assert.NotEqual("", note);
    }
}
