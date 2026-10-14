// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-183: offline replays of the testers' log bundles through the real transport filter and the new rules. The bundles are the
/// maintainer's private logs (never committed): they sit unpacked in logs/w183/&lt;name&gt;/ and a replay is skipped (it passes with a note)
/// when its folder is not there. The expected results are asserted per item and printed (the findings table is these lines).
/// </summary>
public class Wo183ReplayTests
{
    private readonly ITestOutputHelper _out;
    public Wo183ReplayTests(ITestOutputHelper o) { _out = o; }

    private static string? Bundle(string name)
    {
        string? d = AppContext.BaseDirectory;
        while (d is not null && !Directory.Exists(Path.Combine(d, "dotnet", "KcdMp.Client"))) d = Path.GetDirectoryName(d);
        if (d is null) return null;
        string p = Path.Combine(d, "logs", "w183", name);
        return File.Exists(Path.Combine(p, "kcd.log")) ? p : null;
    }

    private static readonly Regex DudeStart = new(@"^Attempting to start new dialogue \(runtime id '(\d+)'\) with souls 'Ex: Dude[';]", RegexOptions.Compiled);
    private static readonly Regex NewWithNpc = new(@"^New dialogue '.*?\((?<kind>[\w ]+) - Id: (?<id>\d+)\)[^']*\((?<dk>\w+)\)' is starting.*souls = 'Ex: Dude; Ex: (?<npc>[A-Za-z0-9_]+)", RegexOptions.Compiled);

    /// <summary>One conversation of the player: its id, its NPC, whether the transport forwards a sign that it runs, and the first state word.</summary>
    private sealed record Conv(int Id, string Npc, string Kind, bool SignNew, bool SignOld, bool Ended);

    private static List<Conv> Conversations(string kcdLog)
    {
        var res = new List<Conv>();
        int id = -1; string npc = "?", kind = "?"; bool signNew = false, signOld = false;
        void Close(bool ended) { if (id >= 0) res.Add(new Conv(id, npc, kind, signNew, signOld, ended)); id = -1; }
        foreach (string raw in File.ReadLines(kcdLog))
        {
            string l = raw;
            var m = DudeStart.Match(l);
            if (m.Success) { Close(false); id = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); npc = "?"; kind = "?"; signNew = signOld = false; continue; }
            if (id < 0) continue;
            var nm = NewWithNpc.Match(l);
            if (nm.Success && int.Parse(nm.Groups["id"].Value, CultureInfo.InvariantCulture) == id) { npc = nm.Groups["npc"].Value; kind = nm.Groups["dk"].Value; }
            // the new rules: the line must pass the forward lists AND show the conversation running
            if (LogTailGameTransport.Wo144Passes(l) && Wo179Rules.ConversationRunning(l, id)) signNew = true;
            // the old rules: only a forwarded "DialogTwin_" line counted, and the old forward lists never passed one
            if (l.Contains("DialogTwin_", StringComparison.Ordinal) && OldForwardPasses(l)) signOld = true;
            if (l.StartsWith($"[ID: {id}] Dialog end", StringComparison.Ordinal) || l.StartsWith($"[ID: {id}] Dialog interrupted", StringComparison.Ordinal)) Close(true);
        }
        Close(false);
        return res;
    }

    /// <summary>The 0.50.8 forward lists, for the comparison (their entries as shipped before WO-183).</summary>
    private static bool OldForwardPasses(string line)
    {
        string[] pre = ["[Warning] Can't equip armor '", "Canceling dialog request id ", "[Warning] [MinigameActivityComponent]:", "Faders are faded out", "Running dialogue '", "New dialogue '", "<RandomEvent>",
                        "PlayAudio: ui_inv_screen_out", "Interrupting FastTravel", "FastTravel: random event variant ", "Cannot change sync guard on ", "[Info] C_GameProfileManager: Activating profile 'weather_",
                        "[Info] C_GameProfileManager: Deactivating profile 'weather_", "PlayAudio: Apse"];
        string[] mid = [" into waiting players", "Dialog interrupted.", "Dialog ends but no response was played", "[NPCContext]:Couldn't find actions to get NPC into game loaded state",
                        "counldn't reach the loaded state in", "] DialogCommand-", "of minigame entity", "PlayerDialogController::NPCPauseRequests timed out", "/(BT)", "Last fader took "];
        return pre.Any(p => line.StartsWith(p, StringComparison.Ordinal)) || mid.Any(m => line.Contains(m, StringComparison.Ordinal));
    }

    [Fact]
    public void Replay_haggling_bundle_item_2_the_joiners_conversations_are_no_longer_ended_at_3_5_s()
    {
        string? b = Bundle("mys_haggle");
        if (b is null) { _out.WriteLine("SKIPPED: logs/w183/mys_haggle is not unpacked here"); return; }
        var convs = Conversations(Path.Combine(b, "kcd.log"));
        // the long conversations are the haggles (the chat kind and the person's own greeting end in under 3.5 s and were never released)
        var man11 = convs.Where(c => c.Npc == "ttkc_man_11" && c.Kind == "Haggle").ToList();
        var emerich = convs.Where(c => c.Npc == "ttkc_emerich" && c.Kind == "Haggle").ToList();
        _out.WriteLine($"REPLAY haggling: {convs.Count} conversations of the player ({convs.Count(c => c.Kind == "Haggle")} haggles); 0.50.8 showed a sign in {convs.Count(c => c.SignOld)} (the twin lines were never forwarded)");
        _out.WriteLine($"REPLAY haggling: ttkc_man_11 haggles {man11.Count}: running (sign seen) under the new rules in {man11.Count(c => c.SignNew)}, ended by A3 {man11.Count(c => !c.SignNew)}   (0.50.8: all {man11.Count} ended at ~3.6-3.9 s)");
        _out.WriteLine($"REPLAY haggling: ttkc_emerich haggles {emerich.Count}: sign in {emerich.Count(c => c.SignNew)}, ended by A3 {emerich.Count(c => !c.SignNew)} (they stayed WAITING_FOR_TWINS: the guard is right)");
        Assert.True(convs.Count(c => c.Kind == "Haggle") >= 4, "the bundle holds the field's haggles");
        Assert.All(convs, c => Assert.False(c.SignOld, "under 0.50.8 no conversation ever showed a sign (the twin lines were not forwarded)"));
        Assert.Equal(2, man11.Count);
        Assert.All(man11, c => Assert.True(c.SignNew, $"the man_11 haggle {c.Id} runs: its commands / twin / state are forwarded now"));
        Assert.NotEmpty(emerich);
        Assert.All(emerich, c => Assert.False(c.SignNew, $"the emerich haggle {c.Id} never ran (WAITING_FOR_TWINS): the guard still ends it"));
    }

    [Fact]
    public void Replay_combat_bundle_item_3_the_joiners_saves_after_the_fix()
    {
        string? b = Bundle("mys_combat");
        if (b is null) { _out.WriteLine("SKIPPED: logs/w183/mys_combat is not unpacked here"); return; }
        string kcd = Path.Combine(b, "kcd.log"), agent = Path.Combine(b, "agent.log");
        int requested = 0, refusedByEngine = 0, pendingOpened = 0;
        foreach (string l in File.ReadLines(kcd))
        {
            if (l.StartsWith("[KCD2-MP] WO125-SNAPSHOT quicksave=", StringComparison.Ordinal)) requested++;
            if (l.Contains(Wo184Rules.RefusedMarker, StringComparison.Ordinal)) refusedByEngine++;
            if (LogTailGameTransport.Wo144Passes(l) && l.StartsWith(Wo184Rules.ObserverPrefix, StringComparison.Ordinal) && l.Contains("started async waiting", StringComparison.Ordinal)) pendingOpened++;
        }
        int rescuesAsked = File.ReadLines(agent).Count(l => l.Contains("WO167-SCENE stuck", StringComparison.Ordinal)) + File.ReadLines(agent).Count(l => l.Contains("MP-W151 scene") && l.Contains("rescue", StringComparison.Ordinal) && l.Contains("save request", StringComparison.Ordinal));
        int hostSnapshotsRefused = File.ReadLines(agent).Count(l => l.Contains("MP-HENRY joiner: host save seq=", StringComparison.Ordinal) && l.Contains("REFUSED the snapshot", StringComparison.Ordinal));
        int a3 = File.ReadLines(agent).Count(l => l.Contains("WO179-RELEASE conversation made no dialogue twin", StringComparison.Ordinal));
        int convs = Conversations(kcd).Count;
        var conv = Conversations(kcd);
        _out.WriteLine($"REPLAY combat (0.50.8): save requests in the game's own log {requested} (of them engine-refused: {refusedByEngine} pending-refusals); stuck-scene rescues asked by the mod {rescuesAsked}; host-save snapshots refused {hostSnapshotsRefused}; false conversation releases (A3) {a3}");
        _out.WriteLine($"REPLAY combat (0.50.8): the BeforeGameSave readiness opened {pendingOpened}x and was never ready: every later save was refused");
        _out.WriteLine($"REPLAY combat (new rules): conversations of the player in the log {convs}, ended by A3 under the new rules {conv.Count(c => !c.SignNew)}; save requests the mod would make: the host-save snapshots only ({hostSnapshotsRefused} of them were refused in the field and are now taken)");
        // the accepting facts of the replay: the field had one accepted request that never became ready, followed by refusals only
        Assert.True(pendingOpened == 1, "one request opened the pending save");
        Assert.True(refusedByEngine >= 30, "the engine refused every later save");
        Assert.True(a3 >= 30, "the false conversation releases are what asked for the rescue saves");
        Assert.Equal(2, hostSnapshotsRefused);   // seq 3 and seq 4 of the host
        // the new code has no request path other than the snapshot: the stuck-scene release, the scene rescue and the positioning lift ask for nothing
        string src = ReadSource("GameBridge.Wo151.cs") + ReadSource("GameBridge.Wo167.cs") + ReadSource("GameBridge.Wo179.cs");
        Assert.DoesNotContain("Wo151SceneRescueSaveAsync", src);
        Assert.DoesNotContain("KCD2MP_Wo125Snapshot", src);
        string snapshotCallers = string.Join(",", Directory.GetFiles(ClientDir(), "*.cs").Where(f => File.ReadAllText(f).Contains("AskModAsync(\"KCD2MP_Wo125Snapshot\"", StringComparison.Ordinal)).Select(Path.GetFileName));
        Assert.Equal("GameBridge.Wo184.cs", snapshotCallers);   // WO-184 moved the one snapshot request (Wo184TakeAsync)
        // the gate (WO-184: two hard blocks): with nothing pending (no rescue ever opened one) and the player in the world, both host snapshots are issued
        Assert.Equal("", Wo184Rules.SaveBlock(new Wo184Rules.SaveGate(InWorld: true, Joining: false, Rewinding: false, SavePending: false)));
    }

    private static string ClientDir()
    {
        string d = AppContext.BaseDirectory;
        while (d is not null && !File.Exists(Path.Combine(d, "dotnet", "KcdMp.Client", "LogTailGameTransport.cs"))) d = Path.GetDirectoryName(d)!;
        return Path.Combine(d, "dotnet", "KcdMp.Client");
    }

    private static string ReadSource(string f) => File.ReadAllText(Path.Combine(ClientDir(), f));

    [Fact]
    public void Replay_host_bundle_item_4b_a_retarget_does_not_release_the_copy()
    {
        string? b = Bundle("holiday_combat");
        if (b is null) { _out.WriteLine("SKIPPED: logs/w183/holiday_combat is not unpacked here"); return; }
        // the host's sender lines (logged on a change): "npc-combat out npc=<n> ... combat=<0|1> ... target=<None|Host|Avatar:g>"
        var rx = new Regex(@"^(\d\d):(\d\d):(\d\d)\.(\d\d\d) .*MP-W132 npc-combat out npc=(?<npc>\S+) .* combat=(?<c>[01]) .* target=(?<t>\S+) ", RegexOptions.Compiled);
        var rows = new List<(string Npc, long AtMs, bool InCombat, string Target)>();
        foreach (string l in File.ReadLines(Path.Combine(b, "agent.log")))
        {
            if (!l.Contains("MP-W132 npc-combat out npc=", StringComparison.Ordinal)) continue;
            var m = rx.Match(l);
            if (!m.Success) continue;
            long t = ((long.Parse(m.Groups[1].Value) * 60 + long.Parse(m.Groups[2].Value)) * 60 + long.Parse(m.Groups[3].Value)) * 1000 + long.Parse(m.Groups[4].Value);
            rows.Add((m.Groups["npc"].Value, t, m.Groups["c"].Value == "1", m.Groups["t"].Value));
        }
        Assert.True(rows.Count > 100, $"the bundle's sender lines were read ({rows.Count})");
        // the engage rule over each NPC's reports: a copy is engaged while an event says Engage; a Release verdict is a leave only after
        // no combat and no target held 1.5 s (the next change of the NPC's report is where a zero period ends)
        int releasesOld = 0, releasesNew = 0, retargetGaps = 0;
        foreach (var grp in rows.GroupBy(r => r.Npc))
        {
            var seq = grp.OrderBy(r => r.AtMs).ToList();
            bool engagedOld = false, engagedNew = false;
            for (int i = 0; i < seq.Count; i++)
            {
                var r = seq[i];
                var ev = new KcdMp.Wire.NpcCombatEvent(1, new KcdMp.Wire.BodyState2(0, 0, r.InCombat ? KcdMp.Wire.BodyState2Bits.CombatMode : KcdMp.Wire.BodyState2Bits.None, 0, 0, 0, 0, 0, 0),
                    r.Target == "None" ? KcdMp.Wire.NpcCombatTarget.None : r.Target == "Host" ? KcdMp.Wire.NpcCombatTarget.Host : KcdMp.Wire.NpcCombatTarget.Avatar, r.Target.StartsWith("Avatar:") ? byte.Parse(r.Target[7..]) : (byte)0, "npc");
                // the joiner (ghost 1) is in the field's session; the engaging event: this player's avatar is the NPC's target
                var vNew = Wo132Rules.JudgeEngage(ev, true, true, true, engagedNew, myGhost: 1);
                // 0.50.8's rule, for the comparison: no "already engaged" clause and no debounce
                bool mine = r.Target == "Avatar:1";
                bool wantOld = mine || (r.InCombat && r.Target != "Host");
                if (wantOld) engagedOld = true;
                else if (engagedOld) { releasesOld++; engagedOld = false; }
                if (vNew == Wo132Rules.EngageVerdict.Engage) engagedNew = true;
                else if (vNew == Wo132Rules.EngageVerdict.Release && engagedNew)
                {
                    long until = i + 1 < seq.Count ? seq[i + 1].AtMs - r.AtMs : long.MaxValue;
                    bool nextFights = i + 1 < seq.Count && (seq[i + 1].InCombat || seq[i + 1].Target != "None");
                    if (until < Wo183Rules.LeftCombatDebounceMs && nextFights) { retargetGaps++; continue; }
                    releasesNew++; engagedNew = false;
                }
            }
        }
        _out.WriteLine($"REPLAY host: {rows.Count} sender lines; releases of an engaged copy: 0.50.8 rule {releasesOld}; WO-183 rule {releasesNew}; retarget gaps kept engaged by the debounce {retargetGaps}");
        // tvez_man_15 and tvez_man_16 in particular: the field's retargets
        var m15 = rows.Where(r => r.Npc == "tvez_man_15").OrderBy(r => r.AtMs).ToList();
        Assert.Contains(m15, r => !r.InCombat && r.Target == "Host");   // the field's combat=0 target=Host report
        Assert.True(releasesNew < releasesOld, "the new rule releases fewer copies than the old one over the same session");
        Assert.True(releasesOld - releasesNew >= 2, "the field's retargets (tvez_man_15 and tvez_man_16) no longer release their copies");
    }
}
