// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-184 C3: offline replays of the joiner's game logs through the real transport filters and the new rules (the conversation clock, WO-179's
/// A3 guard on it, the save gate's two hard blocks) side by side with 0.50.9's (the clock every attempt with Henry started; the gate that
/// waited on it). The bundles are the maintainer's private logs (never committed), unpacked in logs/w184/&lt;name&gt;/; a replay passes with
/// a note when its folder is not there. Every result is printed (the findings' table is these lines).
/// </summary>
public class Wo184ReplayTests
{
    private readonly ITestOutputHelper _out;
    public Wo184ReplayTests(ITestOutputHelper o) { _out = o; }

    private static string? Log(string bundle, string file)
    {
        string? d = AppContext.BaseDirectory;
        while (d is not null && !Directory.Exists(Path.Combine(d, "dotnet", "KcdMp.Client"))) d = Path.GetDirectoryName(d);
        if (d is null) return null;
        string p = Path.Combine(d, "logs", "w184", bundle, file);
        return File.Exists(p) ? p : null;
    }

    private static readonly Regex GameT = new(@"\bt=(\d+(?:\.\d+)?)\s*$", RegexOptions.Compiled);
    private static readonly Regex DataT = new(@"^\[KCD2-MP-DATA\] v2 \d+ (\d+(?:\.\d+)?) ", RegexOptions.Compiled);
    private static readonly Regex HostSave = new(@"^\[KCD2-MP\] MP-WORLDSAVED from=\d+ seq=(\d+) ", RegexOptions.Compiled);
    private static readonly Regex Evt = new(@"^\[KCD2-MP-EVT\] v1 \d+ (\S+) ?(.*)$", RegexOptions.Compiled);
    private static readonly Regex NewName = new(@"^New dialogue '(?<name>[^ ']+) \((?<kind>[^()]*?) - Id: (?<id>\d+)\)[^']*' is starting\. Params: souls = '(?<souls>[^']*)'", RegexOptions.Compiled);

    public sealed record SaveRow(uint Seq, double T, bool Joined, bool PendingNow, bool OldConversation, string NewBlock, bool PendingByNew);
    public sealed record Release(double T, string Dialogue, string Npc, bool Real);

    /// <summary>One launch's kcd.log, in order, through both rule sets.</summary>
    public sealed class Run
    {
        public readonly List<SaveRow> Saves = [];
        public readonly List<Release> A3New = [], A3Old = [];
        public readonly List<(double T, string What)> Edges = [];
        public double LongestOpenS, OldOpenAtEndS;
        public int ShoutAttempts, Lines;

        public static Run Of(string kcdLog)
        {
            var r = new Run();
            var clock = new Wo184Rules.ConversationClock();
            double t = 0;
            long Ms() => (long)(t * 1000);
            bool joined = false, loadingWorld = false;
            long pendingSince = 0;
            long pendingByNewSince = 0;     // a pending save the NEW code could have opened: its own snapshot request, right after a joined host save
            long lastJoinedHostSaveMs = -1_000_000;
            long oldStart = 0;          // 0.50.9: _w179DudeStartMs, from every attempt with Dude, cleared by a Dude end
            long newStart = 0, newReleased = -1, oldReleased = -1, twinSeen = -1_000_000;
            int dudeDialogId = -1;
            var names = new Dictionary<int, (string Name, string Npc)>();
            string lastName = "?", lastNpc = "?";
            void Edge(Wo184Rules.Edge e)
            {
                if (!e.Any) return;
                if (e.Ended) { r.Edges.Add((t, "end: " + e.Why)); if (newStart > 0) r.LongestOpenS = Math.Max(r.LongestOpenS, (Ms() - newStart) / 1000.0); newStart = 0; }
                if (e.Started) { r.Edges.Add((t, "start: " + e.Why)); newStart = Math.Max(Ms(), 1); }
            }
            foreach (string l in File.ReadLines(kcdLog))
            {
                r.Lines++;
                var gt = GameT.Match(l);
                if (gt.Success) t = Math.Max(t, double.Parse(gt.Groups[1].Value, CultureInfo.InvariantCulture));
                else { var dt = DataT.Match(l); if (dt.Success) t = Math.Max(t, double.Parse(dt.Groups[1].Value, CultureInfo.InvariantCulture)); }
                long now = Ms();
                if (l.StartsWith("Loading saved game '", StringComparison.Ordinal)) loadingWorld = l.Contains("/mpworld", StringComparison.Ordinal);
                if (l.StartsWith("Gameplay started", StringComparison.Ordinal) && l.Length < 40)
                {
                    if (loadingWorld) joined = true;
                    Edge(clock.Reset("a load")); oldStart = 0; pendingSince = 0; pendingByNewSince = 0;
                }
                // ---- the QuestLine path (Wo137OnQuestLine): 0.50.9's clock, the new clock's request / attempt
                if (LogTailGameTransport.QuestLinePasses(l) && Wo137Rules.TryParseQuestLine(l, out var q))
                {
                    if (q.Kind is "end" or "attempt" && q.Souls.Contains("Dude"))
                    {
                        if (q.Kind == "end") oldStart = 0; else oldStart = Math.Max(now, 1);
                        if (q.Kind == "attempt" && q.Bark) r.ShoutAttempts++;
                    }
                    var d = Wo184Rules.Parse(l);
                    if (d.Kind is Wo184Rules.DialogKind.Request or Wo184Rules.DialogKind.Attempt) Edge(clock.Feed(d, now));
                }
                // ---- the Wo144 path (Wo144OnEngineLine): A3's signs, the new clock's other lines, the engine's save lines
                if (LogTailGameTransport.Wo144Passes(l))
                {
                    int id = Wo179Rules.DudeDialogId(l);
                    if (id >= 0) dudeDialogId = id;
                    if (Wo179Rules.ConversationRunning(l, dudeDialogId)) twinSeen = now;
                    var nm = NewName.Match(l);
                    if (nm.Success && Wo184Rules.HasDude(nm.Groups["souls"].Value))
                    {
                        string npc = Regex.Match(nm.Groups["souls"].Value, @"Ex: (?!Dude)([A-Za-z0-9_]+)").Groups[1].Value;
                        names[int.Parse(nm.Groups["id"].Value, CultureInfo.InvariantCulture)] = (nm.Groups["name"].Value, npc);
                        lastName = nm.Groups["name"].Value; lastNpc = npc.Length > 0 ? npc : "?";
                    }
                    var d = Wo184Rules.Parse(l);
                    if (d.Kind is not (Wo184Rules.DialogKind.None or Wo184Rules.DialogKind.Request or Wo184Rules.DialogKind.Attempt)) Edge(clock.Feed(d, now));
                    if (l.StartsWith(Wo184Rules.ObserverPrefix, StringComparison.Ordinal))
                    {
                        if (l.Contains("started async waiting", StringComparison.Ordinal))
                        {
                            pendingSince = Math.Max(now, 1);
                            if (now - lastJoinedHostSaveMs <= 3_000) pendingByNewSince = pendingSince;   // the snapshot's own request; any other was 0.50.x's (a rescue)
                        }
                        else if (l.Contains("is ready", StringComparison.Ordinal)) { pendingSince = 0; pendingByNewSince = 0; }
                    }
                }
                // ---- the mod's events: the talk line (WO-164) -> the new clock
                var ev = Evt.Match(l);
                if (ev.Success && ev.Groups[1].Value == "w164_talk")
                {
                    var f = ev.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length >= 2)
                    {
                        if (f[0] == "ask") { Edge(clock.Ask(f[1], now)); lastNpc = f[1]; }
                        else if (f[0] == "start") Edge(clock.TalkStart(f[1], f.Length > 2 && int.TryParse(f[2], out int sid) ? sid : -1, now));
                        else if (f[0] == "end") Edge(clock.TalkEnd(f[1]));
                    }
                }
                Edge(clock.Tick(now));
                // ---- A3 (Wo179GuardTickAsync's rule) on each clock
                if (newStart > 0 && twinSeen < newStart - Wo179Rules.SignLeadMs && now - newStart >= Wo179Rules.TwinsBoundMs && newReleased != newStart)
                {
                    newReleased = newStart;
                    r.A3New.Add(new Release(t, lastName, lastNpc, true));
                }
                if (oldStart > 0 && twinSeen < oldStart - Wo179Rules.SignLeadMs && now - oldStart >= Wo179Rules.TwinsBoundMs && oldReleased != oldStart)
                {
                    oldReleased = oldStart;
                    r.A3Old.Add(new Release(t, lastName, lastNpc, false));
                }
                // ---- a host save: 0.50.9's gate (its conversation flag) and the new one (two hard blocks)
                var hs = HostSave.Match(l);
                if (hs.Success)
                {
                    bool pending = pendingSince != 0;
                    // the new code makes no request but the snapshot: only a pending save of its own blocks it (B3 clears one stuck 60 s)
                    bool pendingNew = pendingByNewSince != 0 && now - pendingByNewSince < Wo184Rules.StuckMs;
                    string block = !joined ? "not joined (the join's own pair)" : Wo184Rules.SaveBlock(new Wo184Rules.SaveGate(InWorld: true, Joining: false, Rewinding: false, SavePending: pendingNew));
                    r.Saves.Add(new SaveRow(uint.Parse(hs.Groups[1].Value, CultureInfo.InvariantCulture), t, joined, pending, oldStart > 0, block, pendingNew));
                    if (joined) lastJoinedHostSaveMs = now;
                }
            }
            if (newStart > 0) r.LongestOpenS = Math.Max(r.LongestOpenS, (Ms() - newStart) / 1000.0);
            r.OldOpenAtEndS = oldStart > 0 ? (Ms() - oldStart) / 1000.0 : 0;
            return r;
        }
    }

    private void Print(string label, Run r)
    {
        _out.WriteLine($"REPLAY {label}: {r.Lines} lines; Henry's combat shouts (bark attempts) {r.ShoutAttempts}; A3 releases 0.50.9 {r.A3Old.Count} -> new {r.A3New.Count}; new clock edges {r.Edges.Count}, longest open {r.LongestOpenS:F1} s; 0.50.9's clock still open at the log's end: {(r.OldOpenAtEndS > 0 ? $"{r.OldOpenAtEndS:F0} s" : "no")}");
        foreach (var s in r.Saves)
            _out.WriteLine($"REPLAY {label}: host save seq={s.Seq} t={s.T:F0}: joined={(s.Joined ? "yes" : "no")} field_engine_pending={(s.PendingNow ? "yes" : "no")} | 0.50.x: {(!s.Joined ? "no snapshot (not joined)" : s.PendingNow ? "REFUSED (another savegame pending)" : s.OldConversation ? "OWED (a conversation is running)" : "requested")} | new: {(s.NewBlock.Length == 0 ? "REQUESTED" : s.NewBlock)}");
        foreach (var a in r.A3New) _out.WriteLine($"REPLAY {label}: A3 (new) at t={a.T:F0}: '{a.Dialogue}' with {a.Npc}");
    }

    [Fact]
    public void Replay_0_50_9_joiner_launch_1_the_gate_never_stays_shut_seq_4_to_8_are_requested()
    {
        string? kcd = Log("j202513", Path.Combine("logbackups", "kcd.log"));
        if (kcd is null) { _out.WriteLine("SKIPPED: logs/w184/j202513 is not unpacked here"); return; }
        var r = Run.Of(kcd);
        Print("0.50.9 joiner launch 1", r);
        var bySeq = r.Saves.ToDictionary(s => s.Seq);
        foreach (uint seq in new uint[] { 3, 4, 5, 6, 7, 8 })
        {
            Assert.True(bySeq[seq].Joined, $"seq {seq}: joined");
            Assert.Equal("", bySeq[seq].NewBlock);   // requested at once: no hard block
        }
        foreach (uint seq in new uint[] { 4, 5, 6, 7, 8 }) Assert.True(bySeq[seq].OldConversation, $"seq {seq}: 0.50.9's gate saw 'a conversation is running'");
        Assert.False(bySeq[3].OldConversation);    // seq 3 paired in the field (the clock had been reset by a monologue's end)
        Assert.True(r.LongestOpenS <= 180.0, "the new clock always ends (3 min at the most)");
        Assert.True(r.OldOpenAtEndS > 300, "0.50.9's clock stayed open to the end of the session");
        Assert.True(r.A3Old.Count >= 8, "0.50.9's A3 fired on the stuck clock again and again");
        Assert.True(r.A3New.Count < r.A3Old.Count);
    }

    [Fact]
    public void Replay_0_50_9_joiner_launch_2_seq_11_is_requested()
    {
        string? kcd = Log("j202513", "kcd.log");
        if (kcd is null) { _out.WriteLine("SKIPPED: logs/w184/j202513 is not unpacked here"); return; }
        var r = Run.Of(kcd);
        Print("0.50.9 joiner launch 2", r);
        var s11 = r.Saves.Single(s => s.Seq == 11);
        Assert.True(s11.Joined);
        Assert.Equal("", s11.NewBlock);
        Assert.True(s11.OldConversation, "0.50.9 owed seq 11 behind one hit scream");
        Assert.True(r.LongestOpenS <= 180.0);
    }

    [Fact]
    public void Replay_0_50_9_joiner_launch_3_every_host_save_is_requested()
    {
        string? kcd = Log("j202913", "kcd.log");
        if (kcd is null) { _out.WriteLine("SKIPPED: logs/w184/j202913 is not unpacked here"); return; }
        var r = Run.Of(kcd);
        Print("0.50.9 joiner launch 3", r);
        Assert.All(r.Saves.Where(s => s.Joined), s => Assert.Equal("", s.NewBlock));
        Assert.True(r.LongestOpenS <= 180.0);
    }

    [Fact]
    public void Replay_0_50_8_combat_logs_no_request_but_the_snapshots_and_a3_only_on_real_talks()
    {
        string? kcd = Log("combat_logs", "kcd.log");
        if (kcd is null) { _out.WriteLine("SKIPPED: logs/w184/combat_logs is not unpacked here"); return; }
        var r = Run.Of(kcd);
        Print("0.50.8 joiner (Combat_logs)", r);
        // every save the new code asks for is a host-save snapshot: the joined host saves, each with nothing pending of the mod's own making
        var joined = r.Saves.Where(s => s.Joined).ToList();
        _out.WriteLine($"REPLAY 0.50.8: the new code's save requests = the joined host saves {string.Join(",", joined.Select(s => s.Seq))} (0.50.8 asked {File.ReadLines(kcd).Count(l => l.StartsWith("[KCD2-MP] WO125-SNAPSHOT quicksave=", StringComparison.Ordinal))} QuickSaves: the rescues)");
        Assert.NotEmpty(joined);
        Assert.All(joined, s => Assert.True(s.PendingNow, "in the field a rescue's save was pending at both host saves"));
        Assert.All(joined, s => Assert.Equal("", s.NewBlock));   // the new code asks for no rescue: nothing of its own is pending -- requested
        // A3: only real talks of the player -- never a shout. Each release names its dialogue.
        Assert.True(r.A3Old.Count >= 30, "0.50.8/9's A3 fired on the shouts (the field: 36)");
        Assert.True(r.A3New.Count <= 6, $"the new clock leaves A3 to real talks only ({r.A3New.Count})");
        Assert.All(r.A3New, a => Assert.NotEqual("?", a.Npc));
        Assert.True(r.ShoutAttempts >= 30);
        Assert.True(r.LongestOpenS <= 180.0);
    }

    [Fact]
    public void Replay_0_50_8_combat_logs_2_and_the_other_bundles_never_keep_the_clock_open()
    {
        foreach (var (b, f) in new[] { ("combat_logs2", "kcd.log"), ("tpose", "kcd.log"), ("decent", "kcd.log"), ("haggle", "kcd.log") })
        {
            string? kcd = Log(b, f);
            if (kcd is null) { _out.WriteLine($"SKIPPED: logs/w184/{b} is not unpacked here"); continue; }
            var r = Run.Of(kcd);
            Print(b, r);
            Assert.True(r.LongestOpenS <= 180.0, $"{b}: the new clock ends");
            Assert.True(r.A3New.Count <= r.A3Old.Count, $"{b}: never more A3 releases than 0.50.9");
        }
    }
}
