// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;
using KcdMp.Wire;
using Xunit;
using Xunit.Abstractions;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-191: offline replays of the 0.60.5 session (the host's one agent run 21:11-23:57; the joiner's last three runs 22:30, 22:32, 22:36)
/// through the new rules. The bundles are the maintainer's private logs (never committed), unpacked in logs/w191/host and logs/w191/joiner;
/// a replay passes with a note when they are not there. Every result is printed (the findings quote these lines).
///   1  the sends of the joiner's last three runs, rebuilt from the joiner's own logs, through the 0.60.5 receiver (it reproduces the host's
///      refusals) and through the new one (forget at each connect/disconnect; a run id per run): 0 StaleOrDuplicate
///   2  the 23:03 focus sweep of ttac_smolik (reported 6 s late at 23:03:44): skipped
///   3  ttac_dog_1: asked for (joiner) and pinned (host) before its first blow
///   4  the 22:45 black on the joiner: ended within 3 s, its holder named
/// </summary>
public class Wo191ReplayTests
{
    private readonly ITestOutputHelper _out;
    public Wo191ReplayTests(ITestOutputHelper o) { _out = o; }

    private static string? Log(string bundle, string file)
    {
        string? d = AppContext.BaseDirectory;
        while (d is not null && !Directory.Exists(Path.Combine(d, "dotnet", "KcdMp.Client"))) d = Path.GetDirectoryName(d);
        if (d is null) return null;
        string p = Path.Combine(d, "logs", "w191", bundle, file);
        return File.Exists(p) ? p : null;
    }

    private static readonly Regex AgentT = new(@"^(\d\d):(\d\d):(\d\d\.\d\d\d) ", RegexOptions.Compiled);
    private static readonly Regex Off = new(@" off=\+(\d+) ", RegexOptions.Compiled);
    private static double Wall(string l) { var m = AgentT.Match(l); return m.Success ? int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : -1; }
    private static string Hms(double s) => TimeSpan.FromSeconds(s).ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture);
    /// <summary>A joiner line's time on the host's clock (its own off= estimate).</summary>
    private static double HostWall(string l) { var m = Off.Match(l); return Wall(l) + (m.Success ? int.Parse(m.Groups[1].Value) / 1000.0 : 4.0); }

    private static byte[] Down(byte sender, byte[] up)
    {
        var body = up.AsSpan(3).ToArray();
        var payload = new byte[1 + body.Length];
        payload[0] = sender;
        body.CopyTo(payload, 1);
        return payload;
    }

    // ================================================================ 1

    private sealed record Ev(double At, string What, ActionKind Kind = ActionKind.Attack, int Run = 0);

    [Fact]
    public void Item1_the_joiners_relaunches_give_no_stale_action_through_the_new_receiver()
    {
        string? hostLog = Log("host", "agent.log");
        string[] runs = ["agent.prev2.log", "agent.prev.log", "agent.log"];
        if (hostLog is null || runs.Any(r => Log("joiner", r) is null)) { _out.WriteLine("SKIPPED: logs/w191 is not unpacked here"); return; }

        // the joiner's sends, per run, on the host's clock
        var evs = new List<Ev>();
        for (int r = 0; r < runs.Length; r++)
        {
            foreach (string l in File.ReadLines(Log("joiner", runs[r])!))
            {
                ActionKind? k = null;
                if (l.Contains(" MP-ACTION section=outbound kind=Attack ")) k = ActionKind.Attack;
                else if (l.Contains(" MP-ACTION section=outbound kind=Jump ")) k = ActionKind.Jump;
                else if (l.Contains(" MP-NPCRESYNC dir=out ")) k = ActionKind.NpcResync;
                else if (l.Contains(" MP-W151 door ask out ")) k = ActionKind.DoorAsk;
                else if (l.Contains(" MP-W151 emote out ")) k = ActionKind.Emote;
                if (k is ActionKind kk) evs.Add(new Ev(HostWall(l), "send", kk, r));
            }
            evs.Add(new Ev(Wall(File.ReadLines(Log("joiner", runs[r])!).First()) + 3.9, "start", Run: r));
        }
        // the host's connect / disconnect of ghost 1 and its refusals, from 22:28 (the window the joiner's logs cover)
        double from = 22 * 3600 + 28 * 60;
        int observedStale = 0;
        var held = new Dictionary<string, int>();   // the 0.60.5 receiver's state at 22:28: the host's accepted counts (a lower bound of its sequence)
        foreach (string l in File.ReadLines(hostLog))
        {
            double t = Wall(l);
            if (t < from)
            {
                if (l.Contains("MP-ACTION section=inbound accepted="))
                {
                    var m = Regex.Match(l, @" kinds=(\S+)");
                    held = m.Groups[1].Value.Split(',').Select(p => p.Split(':')).Where(p => p.Length == 2).ToDictionary(p => p[0], p => int.Parse(p[1], CultureInfo.InvariantCulture));
                }
                var a = Regex.Match(l, @"MP-ACTION section=inbound ghost=1 kind=(Attack|Jump) seq=(\d+)");
                if (a.Success) held[a.Groups[1].Value] = Math.Max(held.GetValueOrDefault(a.Groups[1].Value), int.Parse(a.Groups[2].Value, CultureInfo.InvariantCulture));
                continue;
            }
            if (l.Contains("MP-PEERS ghost 1 connected")) evs.Add(new Ev(t, "connect"));
            else if (l.Contains("MP-PEERS ghost 1 disconnected")) evs.Add(new Ev(t, "disconnect"));
            else if (l.Contains("MP-ACTION section=inbound reject=StaleOrDuplicate")) observedStale++;
        }
        evs.Sort((a, b) => a.At.CompareTo(b.At));
        _out.WriteLine($"the 0.60.5 receiver held at 22:28: {string.Join(", ", held.Select(kv => $"{kv.Key}={kv.Value}"))}");

        (int Stale, Dictionary<ActionKind, int> PerKind, int Accepted) Run(bool forget, bool runIds)
        {
            var inbox = new ActionInbox();
            // prime: the old runs' last sequences, as one earlier run of the same sender (gen 1.1.<run 0>)
            var prime = new ActionOutbox(runId: runIds ? (byte)200 : (byte)0);
            prime.BumpEpoch();
            foreach (var (name, n) in held)
                if (Enum.TryParse<ActionKind>(name, out var k))
                    for (int i = 0; i < n; i++) inbox.Accept(Down(1, prime.Build(k, ActionPhase.Commit, [0])), out _);
            ActionOutbox? cur = null; int curRun = -1;
            int stale = 0, ok = 0;
            var perKind = new Dictionary<ActionKind, int>();
            foreach (var e in evs)
            {
                switch (e.What)
                {
                    case "start":
                        curRun = e.Run;
                        cur = new ActionOutbox(runId: runIds ? (byte)(201 + e.Run) : (byte)0);
                        break;
                    case "connect":
                        cur?.BumpEpoch();
                        if (forget) inbox.Forget(1);
                        break;
                    case "disconnect":
                        if (forget) inbox.Forget(1);
                        break;
                    case "send":
                        if (cur is null || e.Run != curRun) break;
                        if (inbox.Accept(Down(1, cur.Build(e.Kind, ActionPhase.Commit, [0])), out var r) is null)
                        {
                            Assert.Equal(ActionReject.StaleOrDuplicate, r);
                            stale++;
                            perKind[e.Kind] = perKind.GetValueOrDefault(e.Kind) + 1;
                        }
                        else ok++;
                        break;
                }
            }
            return (stale, perKind, ok);
        }

        var old = Run(forget: false, runIds: false);
        _out.WriteLine($"0.60.5 receiver (no forget, every run 1.1.0): {old.Stale} stale [{string.Join(", ", old.PerKind.Select(kv => $"{kv.Key}={kv.Value}"))}], {old.Accepted} accepted; the host's log: {observedStale} StaleOrDuplicate after 22:28");
        var a1 = Run(forget: true, runIds: false);
        var b1 = Run(forget: false, runIds: true);
        var both = Run(forget: true, runIds: true);
        _out.WriteLine($"1a only (an older sender): {a1.Stale} stale, {a1.Accepted} accepted; 1b only: {b1.Stale} stale; 1a+1b (0.60.7): {both.Stale} stale, {both.Accepted} accepted");
        // the model reproduces the field: every Attack, Jump, door ask and emote the host refused (NpcResync's earlier sequence is a bound)
        Assert.Equal(16, old.PerKind.GetValueOrDefault(ActionKind.Attack));
        Assert.Equal(3, old.PerKind.GetValueOrDefault(ActionKind.Jump));
        Assert.Equal(3, old.PerKind.GetValueOrDefault(ActionKind.DoorAsk));
        Assert.Equal(2, old.PerKind.GetValueOrDefault(ActionKind.Emote));
        Assert.Equal(0, a1.Stale);
        Assert.Equal(0, b1.Stale);
        Assert.Equal(0, both.Stale);
    }

    // ================================================================ 2

    [Fact]
    public void Item2_the_focus_sweep_of_the_fighting_copy_is_skipped()
    {
        string? kcd = Log("joiner", "kcd.log");
        if (kcd is null) { _out.WriteLine("SKIPPED: logs/w191/joiner is not unpacked here"); return; }
        bool drawn = false; string? target = null;
        int sweeps = 0, skipped = 0; bool smolik = false;
        var byTrigger = new Dictionary<string, (int All, int Skip)>();
        foreach (string l in File.ReadLines(kcd))
        {
            if (!l.StartsWith("[KCD2-MP-EVT] v1 ", StringComparison.Ordinal)) continue;
            var f = l.Split(' ');
            if (f.Length < 4) continue;
            switch (f[3])
            {
                case "wo124_reply" when l.Contains(" drawn="):
                    drawn = l.Contains(" drawn=1 ");
                    break;
                case "npc_target":
                    target = f.Length > 4 && f[4] != "-" ? f[4] : null;
                    break;
                case "w164_sweep" when f.Length >= 6:
                {
                    string npc = f[4], trig = f[5];
                    sweeps++;
                    // the Lua gate: this player faces it within 4 m with a weapon drawn (the focus sweep's candidate is faced by construction;
                    // npc_target is the nearest copy within 4 m)
                    bool skip = drawn && target == npc && trig == "focus";
                    var bt = byTrigger.GetValueOrDefault(trig);
                    byTrigger[trig] = (bt.All + 1, bt.Skip + (skip ? 1 : 0));
                    if (skip) skipped++;
                    if (npc == "ttac_smolik" && trig == "focus")
                    {
                        smolik = skip;
                        _out.WriteLine($"ttac_smolik focus sweep (the field's 23:03:44 line, run 6 s earlier): drawn={drawn} npc_target={target ?? "-"} -> {(skip ? "SKIPPED (a-player-faces-it-armed)" : "would run")}");
                    }
                    break;
                }
            }
        }
        _out.WriteLine($"all sweeps in the joiner's kcd.log: {sweeps}; skipped by the armed-facing sign alone: {skipped} [{string.Join(", ", byTrigger.Select(kv => $"{kv.Key} {kv.Value.Skip}/{kv.Value.All}"))}] (the agent's fight signs skip more)");
        Assert.True(smolik);
    }

    [Fact]
    public void Item2_the_sweeps_report_line_comes_6_s_after_the_sweep_and_the_engagement_came_after_it()
    {
        string? agent = Log("joiner", "agent.log");
        if (agent is null) { _out.WriteLine("SKIPPED: logs/w191/joiner is not unpacked here"); return; }
        double sweepLine = -1, engage = -1, firstSwing = -1;
        foreach (string l in File.ReadLines(agent))
        {
            if (sweepLine < 0 && l.Contains("WO164-SWEEP npc=ttac_smolik op=release+neutral trigger=focus")) sweepLine = Wall(l);
            if (engage < 0 && l.Contains("MP-W132 engage on ttac_smolik")) engage = Wall(l);
            if (firstSwing < 0 && Wall(l) > 23 * 3600 + 3 * 60 + 30 && l.Contains("MP-ACTION section=outbound kind=Attack")) firstSwing = Wall(l);
        }
        double sweptAt = sweepLine - Wo164Rules.SweepReportMs / 1000.0;
        _out.WriteLine($"sweep report {Hms(sweepLine)} -> swept {Hms(sweptAt)}; first swing {Hms(firstSwing)}; engaged {Hms(engage)}");
        Assert.True(sweptAt < firstSwing && firstSwing < engage);
    }

    // ================================================================ 3

    [Fact]
    public void Item3_the_dog_is_asked_for_and_pinned_before_its_first_blow()
    {
        string? jAgent = Log("joiner", "agent.log"), hAgent = Log("host", "agent.log"), hKcd = Log("host", "kcd.log");
        if (jAgent is null || hAgent is null || hKcd is null) { _out.WriteLine("SKIPPED: logs/w191 is not unpacked here"); return; }
        // the joiner: the first row with no body (the new code asks there) against the first blow on the joiner
        double ask = -1, blow = -1;
        foreach (string l in File.ReadLines(jAgent))
        {
            if (ask < 0 && l.Contains("kind=NpcAttack npc=ttac_dog_1 dispatch=dropped-no-entity")) ask = Wall(l);
            if (blow < 0 && l.Contains("WO161-HIT victim=me by=ttac_dog_1")) blow = Wall(l);
        }
        _out.WriteLine($"joiner: first attack row with no body {Hms(ask)} (WO191-BIND ask) -> first blow {Hms(blow)} ({blow - ask:F1} s later)");
        Assert.True(ask > 0 && ask < blow);
        // the host: the engine's target line in kcd.log (no clock: anchored by the smolik death the agent logged too)
        double deathT = -1, deathWall = -1, targetT = -1, lastT = 0, hostBlow = -1, hostRow = -1;
        foreach (string l in File.ReadLines(hKcd))
        {
            var m = Regex.Match(l, @" t=(\d+\.\d+)\s*$");
            if (m.Success) lastT = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (deathT < 0 && l.StartsWith("[KCD2-MP] NPC-DEATH ttac_smolik died here")) deathT = lastT;
            if (targetT < 0 && Wo191Rules.TryParseTargetLine(l, out string n, out byte p) && n == "ttac_dog_1") { targetT = lastT; Assert.Equal(1, p); }
        }
        foreach (string l in File.ReadLines(hAgent))
        {
            if (deathWall < 0 && l.Contains("[npcdeath] out: mod observed 'ttac_smolik' die locally")) deathWall = Wall(l);
            if (hostRow < 0 && l.Contains("MP-ACTION section=outbound kind=NpcAttack") && l.Contains("npc=ttac_dog_1")) hostRow = Wall(l);
            if (hostBlow < 0 && l.Contains("WO161-HIT victim=1 by=ttac_dog_1")) hostBlow = Wall(l);
        }
        double targetWall = targetT + (deathWall - deathT);   // the last t= before the target line: at most a few seconds early
        _out.WriteLine($"host: [ttac_dog_1] Set target: kcd2mp_1 ~{Hms(targetWall)} (WO191-BIND pinned) -> its first attack row {Hms(hostRow)} -> first blow {Hms(hostBlow)} ({hostBlow - targetWall:F1} s later)");
        Assert.True(targetT > 0 && deathT > 0 && deathWall > 0);
        Assert.True(targetWall < hostRow && hostRow < hostBlow);
    }

    // ================================================================ 4

    [Fact]
    public void Item4_the_black_after_the_shop_talk_ends_within_3_s()
    {
        string? agent = Log("joiner", "agent.log");
        if (agent is null) { _out.WriteLine("SKIPPED: logs/w191/joiner is not unpacked here"); return; }
        var conv = new Wo191Rules.PendingConversation();
        byte pause = 0;
        double fadeOn = -1; string scene = "-";
        var verdicts = new List<string>();
        bool fieldReleased = false;
        string? due = null; double dueAt = -1;
        void Due(double now)
        {
            if (fadeOn < 0 || dueAt < 0 || now < dueAt) return;
            string? content = Wo191Rules.FaderContent(scene, pause, false, false, false);
            due = content is null ? "released after 3.0 s (no content: no scene, no pause reason; the holder from kcd.log below)" : $"left alone: {content}";
            dueAt = -1;
        }
        foreach (string l in File.ReadLines(agent))
        {
            double t = Wall(l);
            Due(t);
            const string eng = "MP-W144 engine: ";
            int ei = l.IndexOf(eng, StringComparison.Ordinal);
            if (ei > 0) conv.OnLine(l[(ei + eng.Length)..], (long)(t * 1000));
            if (l.Contains("[pause] local state -> "))
                pause = l.Contains("-> running") ? (byte)0
                      : (byte)((l.Contains("load") ? Wo138Codec.ReasonLoad : 0) | (l.Contains("dialogue") ? Wo138Codec.ReasonDialogue : 0)
                             | (l.Contains("inventory") ? Wo138Codec.ReasonInventory : 0) | (l.Contains("menu") ? Wo138Codec.ReasonMenu : 0)
                             | (l.Contains("cutscene") ? Wo138Codec.ReasonCutscene : 0) | (l.Contains("skip") ? Wo138Codec.ReasonSkipTime : 0));
            var on = Regex.Match(l, @"WO179-FADE on scene=(\S+)");
            if (on.Success) { fadeOn = t; scene = on.Groups[1].Value; dueAt = t + Wo191Rules.FaderBoundMs / 1000.0; due = null; continue; }
            var off = Regex.Match(l, @"WO179-FADE off scene=\S+ held_ms=(\d+)");
            if (off.Success && fadeOn >= 0)
            {
                long held = long.Parse(off.Groups[1].Value, CultureInfo.InvariantCulture);
                if (held > Wo191Rules.FaderBoundMs)
                {
                    verdicts.Add($"{Hms(fadeOn)} held {held / 1000.0:F1} s -> {due ?? "?"}");
                    if (held > 20_000 && due is not null && due.StartsWith("released", StringComparison.Ordinal)) fieldReleased = true;   // its holder: below
                }
                fadeOn = -1; dueAt = -1;
            }
        }
        foreach (var v in verdicts) _out.WriteLine(v);
        // the holder: the agent prints few engine lines, the game's own log has them all (in order, no clock) -- the conversation
        // that never ran inside each long fader (matched by the engine's own "Last fader took" line)
        string? kcd = Log("joiner", "kcd.log");
        int holder = -1;
        if (kcd is not null)
        {
            var c2 = new Wo191Rules.PendingConversation();
            int lastWaitEnded = -1, endedAt = -1000, n = 0;
            foreach (string l in File.ReadLines(kcd))
            {
                n++;
                if (!LogTailGameTransport.Wo144Passes(l)) continue;
                int w = c2.Waiting;
                c2.OnLine(l, n);
                if (w >= 0 && c2.Id < 0) { lastWaitEnded = w; endedAt = n; }   // ended while still waiting for its NPC
                var lf = Regex.Match(l, @"^Last fader took (\d+\.\d+)s");
                if (lf.Success && double.Parse(lf.Groups[1].Value, CultureInfo.InvariantCulture) > 3.0)
                {
                    int h = n - endedAt < 200 ? lastWaitEnded : c2.Waiting;
                    _out.WriteLine($"kcd.log: a fader of {lf.Groups[1].Value} s, held by {(h >= 0 ? $"conversation id={h} (never past WAITING_FOR_TWINS)" : "no waiting conversation")}");
                    if (lf.Groups[1].Value == "20.10") holder = h;
                }
            }
        }
        Assert.True(fieldReleased);
        if (kcd is not null) Assert.Equal(175, holder);
    }
}
