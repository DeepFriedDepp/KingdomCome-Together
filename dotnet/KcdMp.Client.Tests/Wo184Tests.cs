// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Reflection;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-184 (0.60.0): the joiner's progress is saved, every time -- the rules (Wo184Rules) and the bridge's source guarantees.</summary>
public class Wo184Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wo184-" + Guid.NewGuid().ToString("N")[..8]);
    private DateTime _now = new(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc);
    private readonly List<string> _log = [];
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    // ================================================================ B1: two hard blocks only

    private static Wo184Rules.SaveGate Free => new(InWorld: true, Joining: false, Rewinding: false, SavePending: false);

    [Fact]
    public void The_gate_has_two_hard_blocks_and_nothing_else()
    {
        Assert.Equal("", Wo184Rules.SaveBlock(Free));
        Assert.Contains("pending", Wo184Rules.SaveBlock(Free with { SavePending = true }));
        Assert.Contains("loading", Wo184Rules.SaveBlock(Free with { InWorld = false }));
        Assert.Contains("join", Wo184Rules.SaveBlock(Free with { Joining = true }));
        Assert.Contains("reloading", Wo184Rules.SaveBlock(Free with { Rewinding = true }));
        // the gate cannot even be told about a conversation, a scene, a fader, a crime process, downed or a menu: those are not its business
        var fields = typeof(Wo184Rules.SaveGate).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["InWorld", "Joining", "Rewinding", "SavePending"], fields);
    }

    [Fact]
    public void A_pending_save_is_the_first_block_the_engine_refuses_a_second()
    {
        Assert.Contains("pending", Wo184Rules.SaveBlock(new(InWorld: false, Joining: true, Rewinding: true, SavePending: true)));
    }

    [Fact]
    public void The_engines_own_not_now_is_asked_again_every_two_seconds_and_stuck_is_sixty()
    {
        Assert.Equal(2_000, Wo184Rules.EngineRetryMs);
        Assert.Equal(60_000, Wo184Rules.StuckMs);
        Assert.Equal(60_000, Wo184Rules.HealthMs);
    }

    // ================================================================ B2: the conversation clock

    private const string ShoutHit = "Attempting to start new dialogue (runtime id '48') with souls 'Ex: Dude - meta override: COMBAT_VICTIM_SCREAM_RECEIVED_HIT'";
    private const string ShoutAttack = "Attempting to start new dialogue (runtime id '61') with souls 'Ex: Dude - meta override: COMBAT_ACTOR_SCREAM_ATTACK'";
    private const string ShoutClinch = "Attempting to start new dialogue (runtime id '62') with souls 'Ex: Dude - meta override: COMBAT_ACTOR_SCREAM_CLINCH'";
    private const string Monolog = "New dialogue 'Barbora.open_world.stavy_hrace_unavahlad.narazeni_na_barieru_voda_level.jindrich_narazil_na_hlubokou_vodu (ingame monolog - Id: 289)' is starting. Params: souls = 'Ex: Dude - meta override: HRAC_NEMUZE_DO_HLUBOKE_VODY' forced = true, forced decision = none";
    private const string MonologEnd = "[ID: 289] Dialog ending [Ex0: Dude state: CLEANUP flags: 9105]";
    private const string NpcMonolog = "New dialogue 'Barbora.trosecko.zebrackaBarbora.hibernable.barbora__barky_na_jindru (ingame monolog - Id: 1) (AI::DoMonologue)' is starting. Params: souls = 'Ex: ttkc_barbora' forced = true, forced decision = none";
    private const string Chat = "OnChatWithFocus: Dude->ttac_blacksmith";
    private const string Request365 = "Soul 'Dude' requested dialog. Assigned id is 365";
    private const string Attempt365 = "Attempting to start new dialogue (runtime id '365') with souls 'Ex: Dude; Ex: ttac_blacksmith'";
    private const string Cancel365 = "Canceling dialog request id 365 from soul 'Dude'. Request timed out.";
    private const string ChatStart49 = "New dialogue 'Barbora.open_world.shop.nakupovani_z_chatu_muz (chat - Id: 49) (DoChat)' is starting. Params: souls = 'Ex: Dude; Ex: ttkc_man_11' forced = false, forced decision = none";
    private const string Haggle53 = "New dialogue 'Barbora.open_world.shop.bribe_haggle_negotiation_vyjednavani_smlouvani_podplaceni.smlouvani_v_obchode_muz (normal - Id: 53) (Haggle)' is starting. Params: souls = 'Ex: Dude; Ex: ttkc_man_11 - meta override: SMLOUVANI, VYJEDNAVANI' forced = true, forced decision = none";
    private const string End49 = "[ID: 49] Dialog ending [Ex0: Dude Ex1: ttkc_man_11 state: CLEANUP flags: 9104]";
    private const string End53 = "[ID: 53] Dialog ending [Ex0: Dude Ex1: ttkc_man_11 state: CLEANUP flags: 9104]";
    private const string Interrupting72 = "Interrupting dialog 'Barbora.open_world.shop.bribe_haggle_negotiation_vyjednavani_smlouvani_podplaceni.vyjednavani_o_cene_muz (normal - Id: 72) [Ex0: Dude Ex1: ttkc_man_11 state: WAITING_FOR_USER_INPUT flags: 2113297] (Haggle)' in progress. Interrupting";
    private const string Interrupted72 = "[ID: 72] Dialog interrupted. [Ex0: Dude Ex1: ttkc_man_11 state: WAITING_FOR_USER_INPUT flags: 2113305]";

    private static Wo184Rules.Edge Feed(Wo184Rules.ConversationClock c, string line, long now) => c.Feed(Wo184Rules.Parse(line), now);

    [Fact]
    public void A_combat_shout_never_starts_the_clock()
    {
        var c = new Wo184Rules.ConversationClock();
        foreach (var l in new[] { ShoutHit, ShoutAttack, ShoutClinch }) Assert.False(Feed(c, l, 1000).Any);
        Assert.False(c.Running);
        var d = Wo184Rules.Parse(ShoutHit);
        Assert.Equal(Wo184Rules.DialogKind.Attempt, d.Kind);
        Assert.True(d.Bark && d.Dude);
    }

    [Fact]
    public void A_bare_attempt_with_henry_never_starts_the_clock()
    {
        var c = new Wo184Rules.ConversationClock();
        Assert.False(Feed(c, Attempt365, 1000).Any);
        Assert.False(c.Running);
    }

    [Fact]
    public void A_monologue_never_starts_the_clock_and_its_end_never_ends_a_talk()
    {
        var c = new Wo184Rules.ConversationClock();
        Assert.False(Feed(c, Monolog, 1000).Any);
        Assert.False(Feed(c, NpcMonolog, 1000).Any);
        Assert.False(c.Running);
        // a talk asked while Henry's monologue runs: the monologue's end is not the talk's end
        Assert.True(Feed(c, Chat, 2000).Started);
        Assert.False(Feed(c, MonologEnd, 2100).Any);
        Assert.True(c.Running);
    }

    [Fact]
    public void The_field_timed_out_talk_ends_the_clock_and_the_mods_late_start_does_not_reopen_it()
    {
        // j202513 launch 1, t~703: the talk to ttac_blacksmith -- the request timed out at once; WO-183's gate stayed shut for the rest of the session
        var c = new Wo184Rules.ConversationClock();
        Assert.True(Feed(c, Chat, 1000).Started);
        Assert.False(Feed(c, Request365, 1010).Any);
        Assert.False(Feed(c, Attempt365, 1020).Any);
        var e = Feed(c, Cancel365, 1030);
        Assert.True(e.Ended);
        Assert.Contains("cancelled", e.Why);
        Assert.False(c.Running);
        Assert.False(c.TalkStart("ttac_blacksmith", 365, 1300).Any);   // "WO137-TALK start ... id=365" came after the cancel
        Assert.False(c.Running);
    }

    [Fact]
    public void A_real_talk_runs_from_the_ask_through_its_start_to_its_end()
    {
        var c = new Wo184Rules.ConversationClock();
        Assert.True(c.Ask("ttkc_man_11", 1000).Started);
        Assert.False(Feed(c, ChatStart49, 1200).Any);   // the chat starts (not forced: it confirms the asked talk)
        Assert.False(c.Tick(1200 + 60_000).Any);         // confirmed: no 5 s rule
        Assert.True(Feed(c, End49, 1200 + 61_000).Ended);
        Assert.False(c.Running);
    }

    [Fact]
    public void A_chat_handing_over_to_its_haggle_is_one_conversation()
    {
        var c = new Wo184Rules.ConversationClock();
        c.Ask("ttkc_man_11", 0);
        Feed(c, ChatStart49, 100);
        Feed(c, Haggle53, 5000);
        Assert.False(Feed(c, End49, 5100).Any);   // the chat's own end after the haggle took over
        Assert.True(c.Running);
        Assert.True(Feed(c, End53, 40_000).Ended);
    }

    [Fact]
    public void A_forced_dialogue_with_henry_starts_it_a_lone_unforced_one_does_not()
    {
        var c = new Wo184Rules.ConversationClock();
        Assert.False(Feed(c, ChatStart49, 0).Any);    // forced = false, nobody asked: not his conversation by itself
        Assert.False(c.Running);
        var e = Feed(c, Haggle53, 100);               // forced = true, Henry in it
        Assert.True(e.Started);
        Assert.Contains("forced", e.Why);
    }

    [Fact]
    public void An_attempt_with_no_real_start_within_five_seconds_ends_it()
    {
        var c = new Wo184Rules.ConversationClock();
        c.Ask("ttac_blacksmith", 0);
        Feed(c, Attempt365, 1000);
        Assert.False(c.Tick(5999).Any);
        var e = c.Tick(6000);
        Assert.True(e.Ended);
        Assert.Contains("5 s", e.Why);
        // and an ask that never even reached an attempt
        var d = new Wo184Rules.ConversationClock();
        d.Ask("x_npc", 0);
        Assert.True(d.Tick(5000).Ended);
    }

    [Fact]
    public void Three_minutes_end_it_in_any_case()
    {
        var c = new Wo184Rules.ConversationClock();
        Feed(c, Haggle53, 0);
        Assert.False(c.Tick(179_999).Any);
        var e = c.Tick(180_000);
        Assert.True(e.Ended);
        Assert.Contains("3 minutes", e.Why);
    }

    [Fact]
    public void Interrupting_and_interrupted_end_it()
    {
        var c = new Wo184Rules.ConversationClock();
        c.Ask("ttkc_man_11", 0);
        Feed(c, "New dialogue 'x.y (normal - Id: 72) (Haggle)' is starting. Params: souls = 'Ex: Dude; Ex: ttkc_man_11' forced = true, forced decision = none", 100);
        Assert.True(Feed(c, Interrupting72, 2000).Ended);
        Assert.False(Feed(c, Interrupted72, 2001).Any);   // the second word of the same end
        var p = Wo184Rules.Parse(Interrupted72);
        Assert.Equal(Wo184Rules.DialogKind.Interrupted, p.Kind);
        Assert.Equal(72, p.Id);
    }

    [Fact]
    public void The_mods_talk_end_ends_it()
    {
        var c = new Wo184Rules.ConversationClock();
        c.Ask("ttac_blacksmith", 0);
        Assert.False(c.TalkEnd("someone_else").Any);
        Assert.True(c.TalkEnd("ttac_blacksmith").Ended);
    }

    [Fact]
    public void A_load_resets_it()
    {
        var c = new Wo184Rules.ConversationClock();
        c.Ask("x", 0);
        Assert.True(c.Reset("a load").Ended);
        Assert.False(c.Reset("a load").Any);
    }

    [Fact]
    public void The_parser_reads_every_kind_and_the_end_variants()
    {
        Assert.Equal(Wo184Rules.DialogKind.Chat, Wo184Rules.Parse(Chat).Kind);
        Assert.Equal("ttac_blacksmith", Wo184Rules.Parse(Chat).Npc);
        Assert.Equal((Wo184Rules.DialogKind.Request, 365), (Wo184Rules.Parse(Request365).Kind, Wo184Rules.Parse(Request365).Id));
        Assert.Equal(Wo184Rules.DialogKind.Cancel, Wo184Rules.Parse(Cancel365).Kind);
        Assert.True(Wo184Rules.Parse(Cancel365).Dude);
        Assert.False(Wo184Rules.Parse("Canceling dialog request id 7 from soul 'ttkc_man_2'. Request timed out.").Dude);
        var m = Wo184Rules.Parse(Monolog);
        Assert.True(m.Monolog && m.Dude && m.Forced);
        Assert.True(Wo184Rules.Parse(NpcMonolog).Monolog);
        var h = Wo184Rules.Parse(Haggle53);
        Assert.True(h.Dude && h.Forced && !h.Bark && !h.Monolog);
        Assert.Equal(53, h.Id);
        var nr = Wo184Rules.Parse("[ID: 79] Dialog ends but no response was played. (Forced: '0') [Ex0: tkrc_man_3 Nx1: Dude state: CLEANUP flags: 9104]");
        Assert.Equal(Wo184Rules.DialogKind.End, nr.Kind);
        Assert.True(nr.Dude);
        Assert.False(Wo184Rules.Parse("[ID: 80] Dialog ending [Ex0: ttkc_man_3 Ex1: ttkc_woman_2 state: CLEANUP flags: 9104]").Dude);
        Assert.False(Wo184Rules.HasDude("Ex: Dudek; Ex: x"));
        Assert.Equal(Wo184Rules.DialogKind.None, Wo184Rules.Parse("[KCD2-MP] something else").Kind);
    }

    [Fact]
    public void Every_line_the_clock_and_the_save_owner_read_is_forwarded_by_the_log_tail()
    {
        foreach (var pre in Wo184Rules.ClockLinePrefixes)
            Assert.True(LogTailGameTransport.Wo144Passes(pre) || LogTailGameTransport.QuestLinePasses(pre), $"'{pre}' never reaches the clock");
        foreach (var l in new[] { Chat, Request365, Attempt365, Cancel365, ChatStart49, Haggle53, End49, Interrupting72, Interrupted72, Monolog,
                                  "[ID: 79] Dialog ends but no response was played. (Forced: '0') [Ex0: tkrc_man_3 Nx1: Dude state: CLEANUP flags: 9104]",
                                  "Readiness observer 'BeforeGameSave' with category bitmask '42' started async waiting",
                                  "Readiness observer 'BeforeGameSave' with category bitmask '42' is ready",
                                  "C_UISaveLoad::HandleExit()" })
            Assert.True(LogTailGameTransport.Wo144Passes(l) || LogTailGameTransport.QuestLinePasses(l), $"not forwarded: {l}");
    }

    // ================================================================ B3: stuck

    [Fact]
    public void A_request_with_no_file_and_no_ready_for_sixty_seconds_is_stuck()
    {
        Assert.False(Wo184Rules.IsStuck(1000, false, false, 60_999));
        Assert.True(Wo184Rules.IsStuck(1000, false, false, 61_000));
        Assert.False(Wo184Rules.IsStuck(1000, true, false, 999_999));    // its file came
        Assert.False(Wo184Rules.IsStuck(1000, false, true, 999_999));    // the engine said ready
        Assert.False(Wo184Rules.IsStuck(0, false, false, 999_999));      // nothing was asked
    }

    // ================================================================ B5: the health check

    [Fact]
    public void Every_host_save_ends_in_a_pair_or_a_reason_within_sixty_seconds()
    {
        var h = new Wo184Rules.SaveHealth();
        h.Arrived(3, "a", 0);
        h.Paired(3);
        Assert.Empty(h.Tick(61_000, () => "x", out bool t0));
        Assert.False(t0);
        h.Arrived(4, "b", 70_000);
        Assert.Empty(h.Tick(129_999, () => "owed", out _));
        var due = h.Tick(130_000, () => "owed: the engine cannot save now", out bool t1);
        Assert.Single(due);
        Assert.Equal(4u, due[0].Seq);
        Assert.Contains("engine", due[0].Reason);
        Assert.False(t1);   // one unpaired is logged, not told
    }

    [Fact]
    public void Two_host_saves_in_a_row_unpaired_tell_both_players_once_and_a_pair_ends_the_run()
    {
        var h = new Wo184Rules.SaveHealth();
        h.Arrived(4, "a", 0);
        h.Arrived(5, "b", 1000);
        h.Tick(60_000, () => "owed", out bool t1);
        Assert.False(t1);
        h.Tick(61_000, () => "owed", out bool t2);
        Assert.True(t2);
        h.Arrived(6, "c", 62_000);
        h.Tick(122_000, () => "owed", out bool t3);
        Assert.False(t3);   // told once per run
        h.Paired(6);
        Assert.Equal(0, h.FailRun);
        h.Arrived(7, "d", 130_000); h.Arrived(8, "e", 131_000);
        h.Tick(191_000, () => "owed", out bool t4);
        Assert.True(t4);    // a new run is told again
    }

    [Fact]
    public void A_superseded_host_save_is_covered_by_the_pair_of_the_newer_one()
    {
        var h = new Wo184Rules.SaveHealth();
        h.Arrived(4, "a", 0);
        h.Arrived(5, "b", 1000);
        h.Paired(5);   // one snapshot, paired with the newest host save it follows
        Assert.Empty(h.Tick(200_000, () => "x", out _));
        Assert.Equal(5u, h.LastPaired()!.Seq);
    }

    [Fact]
    public void A_logged_reason_is_kept_as_the_saves_reason()
    {
        var h = new Wo184Rules.SaveHealth();
        h.Arrived(9, "a", 0);
        h.Reason(9, "the host's player is not Henry");
        var due = h.Tick(60_000, () => "other", out _);
        Assert.Equal("the host's player is not Henry", due.Single().Reason);
    }

    [Fact]
    public void The_kind_16_texts_round_trip_and_garbage_is_refused()
    {
        Assert.Equal(16, Protocol.W164SaveHealth);
        Assert.Equal("save-health", Protocol.W164KindName(Protocol.W164SaveHealth));
        string p = Wo184Rules.PairedText(12, "0123456789abcdef0123456789abcdef");
        Assert.Equal("paired 12 01234567", p);
        Assert.True(Wo184Rules.TryParseHealth(p, out string w, out uint seq, out string md5, out _));
        Assert.Equal(("paired", 12u, "01234567"), (w, seq, md5));
        Assert.True(Wo184Rules.TryParseHealth(Wo184Rules.UnpairedText(2), out w, out _, out _, out int n));
        Assert.Equal(("unpaired", 2), (w, n));
        Assert.True(Wo184Rules.TryParseHealth("stuck", out w, out _, out _, out _));
        Assert.True(Wo184Rules.TryParseHealth("recovered", out _, out _, out _, out _));
        foreach (var bad in new[] { "", "paired", "paired x 01234567", "paired 1 XYZ", "unpaired 0", "stuck now", "hello", new string('a', 50) })
            Assert.False(Wo184Rules.TryParseHealth(bad, out _, out _, out _, out _), bad);
    }

    // ================================================================ B4: leaving

    [Fact]
    public void A_final_snapshot_is_taken_at_a_leave_when_the_joiner_is_in_the_world_and_nothing_is_pending()
    {
        Assert.Equal("", Wo184Rules.FinalSnapshotBlock(joined: true, inWorld: true, pending: false, owed: false, always: true, haveHostSave: true));
        Assert.Equal("", Wo184Rules.FinalSnapshotBlock(true, true, false, owed: true, always: false, true));
        Assert.Equal("-", Wo184Rules.FinalSnapshotBlock(true, true, false, owed: false, always: false, true));   // off and nothing owed: not a failure
        Assert.Contains("pending", Wo184Rules.FinalSnapshotBlock(true, true, pending: true, true, true, true));
        Assert.Contains("not loaded", Wo184Rules.FinalSnapshotBlock(true, inWorld: false, false, true, true, true));
        Assert.Contains("not in the host's world", Wo184Rules.FinalSnapshotBlock(joined: false, true, false, true, true, true));
        Assert.Contains("no host save", Wo184Rules.FinalSnapshotBlock(true, true, false, true, true, haveHostSave: false));
    }

    [Fact]
    public void The_hosts_partner_saved_line_shows_once_per_host_save()
    {
        Assert.True(Wo184Rules.PartnerSavedShows(known: false, 0, "", 14, "698d1ea7"));
        Assert.False(Wo184Rules.PartnerSavedShows(true, 14, "698d1ea7", 14, "698d1ea7"));   // the joiner's menu or leave snapshot of the same save
        Assert.True(Wo184Rules.PartnerSavedShows(true, 14, "698d1ea7", 15, "0badf00d"));    // the next host save
        Assert.True(Wo184Rules.PartnerSavedShows(true, 14, "698d1ea7", 14, "0badf00d"));    // a new session's seq 14 is another save
    }

    [Fact]
    public void The_esc_menu_starts_no_snapshot_any_more_and_the_exit_dialog_takes_the_quits()
    {
        // WO-192: the ESC menu's early snapshot is gone (it was refused with every other ask while a scene positioned copies); the menu
        // edge is only the host autosave's "a menu"
        Assert.Equal(0xAD, CombatPipe.MenuOut);   // native wo138_rules.h kMenuFrame (pinned there too)
        Assert.Contains("_combat.OnMenu = Wo192OnMenu", ClientFile("GameBridge.Wo138.cs"));
        string b = ClientFile("GameBridge.Wo184.cs");
        Assert.DoesNotContain("Wo184OnMenu", b);
        Assert.DoesNotContain("MenuPurpose", b);
        Assert.Contains("Wo184FinalSnapshotAsync(\"quit (the exit dialog)\")", b);
        // the joiner's "your host's game saved" line is about a host save only
        Assert.Contains("if (purpose.StartsWith(\"host save\", StringComparison.Ordinal)) Wo157TellCharacterSaved();", b);
    }

    // ================================================================ B6: the restore picks the right Henry

    private HenryStore Store() => new(Path.Combine(_root, "henry"), () => _now, s => _log.Add(s));
    private static string M(int i) => i.ToString("x32");

    private static WhsSave.HenryParts Parts(byte side)
    {
        var (_, js) = WhsSaveTests.Pair();
        js.Side = side;
        return WhsSave.PartsFromFile(WhsSaveTests.File(js), WhsSave.HenryParts.OriginSnapshot);
    }

    [Fact]
    public void The_field_rejoin_restores_the_newest_pair_on_the_branch_which_was_seq_3_because_seq_4_to_10_never_paired()
    {
        // j202513: the join paired seq 2 (the offer); the snapshot paired seq 3; seq 4-8 owed and dropped; seq 9-10 came while the game was not in the world
        var st = Store();
        string tag = WhsSave.SeedTag(184);
        st.Store(tag, Parts(2), M(2), HenryStore.SourceJoin, "the join save (seq 2)"); _now = _now.AddMinutes(10);
        st.Store(tag, Parts(3), M(3), HenryStore.SourceSnapshot, "seq 3");
        var branch = Enumerable.Range(1, 10).Reverse().Select(M).ToList();   // newest first: 10 .. 1
        var p = st.PickFor(tag, branch)!;
        Assert.Equal(M(3), p.Snapshot.Md5);
        Assert.Contains("branch save #7", p.How);   // the field's own words: "paired with the host's branch save #7 (newest = 0)"
        // had seq 8 paired (WO-184), the rejoin restores it
        _now = _now.AddMinutes(10);
        st.Store(tag, Parts(4), M(8), HenryStore.SourceSnapshot, "seq 8");
        Assert.Equal(M(8), st.PickFor(tag, branch)!.Snapshot.Md5);
        // and a final snapshot at the quit, paired with the newest host save (the late rule), wins over the earlier pair of that save
        _now = _now.AddMinutes(1);
        var fin = st.Store(tag, Parts(5), M(8), HenryStore.SourceSnapshot, "final snapshot (quit)");
        Assert.Equal(fin.File, st.PickFor(tag, branch)!.Snapshot.File);
    }

    [Fact]
    public void Late_snapshots_of_one_host_save_never_push_other_host_saves_out_of_the_store()
    {
        var st = Store();
        string tag = WhsSave.SeedTag(184);
        var parts = Parts(1);
        for (int i = 0; i < HenryStore.KeepPerWorld; i++) { st.Store(tag, parts, M(i), HenryStore.SourceSnapshot, "host save"); _now = _now.AddSeconds(1); }
        // the joiner opens the ESC menu 30 times before the host saves again: 30 late snapshots of the newest host save
        for (int k = 0; k < 30; k++) { st.Store(tag, parts, M(HenryStore.KeepPerWorld - 1), HenryStore.SourceSnapshot, "final snapshot (the ESC menu)"); _now = _now.AddSeconds(1); }
        var all = st.Snapshots(tag);
        Assert.Equal(HenryStore.KeepPerWorld, all.Select(s => s.Md5).Distinct().Count());               // every host save keeps its pair
        Assert.Equal(HenryStore.KeepPerHostSave, all.Count(s => s.Md5 == M(HenryStore.KeepPerWorld - 1)));
        Assert.Equal(HenryStore.KeepPerWorld + 1, all.Count);
        Assert.Equal(M(0), st.PickFor(tag, [M(0)])!.Snapshot.Md5);                                       // a rewind to the oldest still finds its own Henry
        var newest = all.First(s => s.Md5 == M(HenryStore.KeepPerWorld - 1));
        Assert.Equal(newest.File, st.PickFor(tag, [M(HenryStore.KeepPerWorld - 1)])!.Snapshot.File);    // and the newest host save its newest
        st.Store(tag, parts, M(HenryStore.KeepPerWorld), HenryStore.SourceSnapshot, "host save");        // a 101st host save: the oldest goes
        Assert.DoesNotContain(st.Snapshots(tag), s => s.Md5 == M(0));
    }

    // ================================================================ the bridge's source guarantees

    private static string ClientFile(string f)
    {
        string? d = AppContext.BaseDirectory;
        while (d is not null && !File.Exists(Path.Combine(d, "dotnet", "KcdMp.Client", f))) d = Path.GetDirectoryName(d);
        Assert.NotNull(d);
        return File.ReadAllText(Path.Combine(d!, "dotnet", "KcdMp.Client", f));
    }

    [Fact]
    public void The_snapshot_never_reads_a_mod_flag_and_the_clock_is_fed_only_through_its_rules()
    {
        string b = ClientFile("GameBridge.Wo184.cs");
        string block = b[b.IndexOf("private async Task<string> Wo184BlockAsync", StringComparison.Ordinal)..];
        block = block[..block.IndexOf("\n    }", StringComparison.Ordinal)];
        foreach (var flag in new[] { "_w179DudeStartMs", "_w138Dialog", "_w179Cur", "_w179Faded", "_w151Scenes", "_w139StoppedHere", "_localDowned", "Wo138PauseState", "W183CrimeProcess", "_w167IsmEnq" })
            Assert.DoesNotContain(flag, block);
        // WO-167's line 728 fed the clock from every attempt with Henry: gone
        string q = ClientFile("GameBridge.Wo137.cs");
        Assert.DoesNotContain("Wo167OnDudeDialogue(q.Kind", q);
        Assert.Contains("Wo184OnQuestLine(line)", q);
    }

    [Fact]
    public void Nothing_is_dropped_silently_and_the_quit_notice_tells_the_truth()
    {
        string all = string.Join("\n", new[] { "GameBridge.Wo184.cs", "GameBridge.Wo124.cs", "GameBridge.Wo125.cs", "GameBridge.Wo183.cs" }.Select(ClientFile));
        Assert.DoesNotContain("OwedMaxTries", all);                                                // no cap: owed until taken or the joiner leaves
        Assert.DoesNotContain("Your progress in this world is saved up to your host's last save.", all);   // WO-125's quit notice said it even when unpaired
        Assert.Contains("Wo184TakeLateFile(path)", ClientFile("GameBridge.Wo125.cs"));               // a late snapshot file pairs, never a "leak"
        Assert.Contains("Wo184FinalSnapshotAsync(\"leave (\" + why + \")\")", ClientFile("GameBridge.Wo124.cs"));
        Assert.DoesNotContain("lagS > 3.0", all);                                                  // the late rule: a slow request still pairs
    }

    [Fact]
    public void The_only_save_request_on_the_joiner_is_the_snapshot()
    {
        var callers = new List<string>();
        string? d = AppContext.BaseDirectory;
        while (d is not null && !Directory.Exists(Path.Combine(d, "dotnet", "KcdMp.Client"))) d = Path.GetDirectoryName(d);
        foreach (var f in Directory.GetFiles(Path.Combine(d!, "dotnet", "KcdMp.Client"), "*.cs"))
            if (File.ReadAllText(f).Contains("AskModAsync(\"KCD2MP_Wo125Snapshot\"", StringComparison.Ordinal)) callers.Add(Path.GetFileName(f));
        Assert.Equal(["GameBridge.Wo184.cs"], callers);
    }
}
