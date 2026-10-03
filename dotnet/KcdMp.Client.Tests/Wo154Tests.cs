// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-154: the rules of this WO (docs/WO-154-findings.md).</summary>
public class Wo154Tests
{
    // the field's per-player trigger (the host's and the joiner's own distance to the intermission)
    private const string Trigger = "Barbora.trosecko.socky.straznikuv_monokl.IntermissionTriggerByDistance.WaitForIntermission";
    // a story step that moves on
    private const string Counter = "Barbora.trosecko.mysi1.carryingBags.pickedSacks";

    [Fact]
    public void A_value_put_straight_back_by_this_world_is_contested()
    {
        var c = new Wo154Contest();
        c.NoteImposed(Trigger, 1, 0, 1000);                     // the host's 1->0 applied here
        Assert.True(c.NoteLocal(Trigger, 0, 1, 3000, out var why));   // this world's own trigger: back to 1
        Assert.Contains("put it back from 0 to 1", why);
        Assert.True(c.IsContested(Trigger));
        Assert.Equal(1, c.Count);
        Assert.False(c.NoteLocal(Trigger, 1, 0, 4000, out _));  // found once
    }

    [Fact]
    public void A_story_step_that_moves_on_is_never_contested()
    {
        var c = new Wo154Contest();
        c.NoteImposed(Counter, 2, 3, 1000);
        Assert.False(c.NoteLocal(Counter, 3, 4, 2000, out _));   // progress, not a return
        Assert.False(c.IsContested(Counter));
    }

    [Fact]
    public void A_return_after_the_window_is_ordinary_play()
    {
        var c = new Wo154Contest();
        c.NoteImposed(Trigger, 1, 0, 1000);
        Assert.False(c.NoteLocal(Trigger, 0, 1, 1000 + Wo154Contest.WindowMs + 1, out _));
        Assert.False(c.IsContested(Trigger));
    }

    [Fact]
    public void Without_an_imposed_value_nothing_is_contested()
    {
        var c = new Wo154Contest();
        Assert.False(c.NoteLocal(Trigger, 0, 1, 1000, out _));
        Assert.False(c.NoteLocal(Trigger, 1, 0, 1100, out _));
        Assert.Equal(0, c.Count);
    }

    [Fact]
    public void A_return_to_another_value_is_not_a_return()
    {
        var c = new Wo154Contest();
        c.NoteImposed(Counter, 1, 3, 1000);
        Assert.False(c.NoteLocal(Counter, 3, 2, 1500, out _));
        Assert.False(c.IsContested(Counter));
    }

    [Fact]
    public void The_other_side_marks_it_once_and_a_reset_forgets()
    {
        var c = new Wo154Contest();
        Assert.True(c.Mark(Trigger, "the host's world puts it back by itself"));
        Assert.False(c.Mark(Trigger, "again"));
        Assert.True(c.IsContested(Trigger));
        c.NoteImposed(Trigger, 0, 1, 1000);   // nothing is tracked for a contested State
        c.Reset();
        Assert.False(c.IsContested(Trigger));
        Assert.Equal(0, c.Count);
    }

    [Fact]
    public void A_downed_bit_is_an_edge_on_its_first_sample_each_way()
    {
        // The sender's DLL debounces the bit; the block travels only on a change (live L1: a receiver that
        // waited for a second clear sample never stood the avatar up).
        var d = new Wo154Rules.DownEdge();
        Assert.Null(d.Feed(false, 1000));                   // up and staying up: nothing
        Assert.True(d.Feed(true, 2000));                    // the first Downed block: he is down
        Assert.True(d.Down);
        Assert.Null(d.Feed(true, 3000));                    // the 1 s heartbeat: no second fall
        Assert.False(d.Feed(false, 4000));                  // the first clear block: he is up
        Assert.False(d.Down);
        Assert.Null(d.Feed(false, 5000));
        Assert.True(d.Feed(true, 6000));                    // knocked down again
    }

    // the field's Moravian fight on the joiner, 106 ms apart (22:44:32.453 / .559)
    private const string Fight = "Barbora.trosecko.zbranePanaSemina.h.na_semine.bitka_s_moravaky.stateBitkaSMoravakem";
    private const string Duel = "Barbora.trosecko.zbranePanaSemina.h.na_semine.bitka_s_moravaky.duelbehavioradvanced.OngoingDuel";
    private static QuestChange W(string path, int o, int n, string port) =>
        new(0, QuestChange.FNotify | QuestChange.FOldOk | QuestChange.FNewOk | QuestChange.FWorker, o, n, port, "", path, 33);

    [Fact]
    public void An_AI_behaviours_steps_on_one_State_are_judged_as_their_net_result()
    {
        var co = new Wo154Rules.WorkerCoalescer();
        Assert.False(co.Add(1, 28, W(Fight, 0, 1, "SetInProgress"), 1000));
        Assert.False(co.Add(1, 29, W(Duel, 0, 1, "SetTrue"), 1000));
        Assert.True(co.Add(1, 33, W(Fight, 1, 2, "SetWon"), 1106));
        Assert.True(co.Add(1, 36, W(Duel, 1, 0, "SetFalse"), 1106));
        Assert.Empty(co.TakeReady(2000));                       // quiet for less than 1.5 s: still waiting
        var ready = co.TakeReady(2700);
        Assert.Equal(2, ready.Count);
        Assert.Equal(Fight, ready[0].Path);                     // in the order they first came
        var fight = Wo154Rules.WorkerCoalescer.Merged(ready[0]);
        Assert.Equal((0, 2, "SetWon"), (fight.Old, fight.New, fight.Port));   // one Won, not InProgress then Won
        Assert.Equal(new uint[] { 28, 33 }, ready[0].Toks);   // both steps get the one answer
        Assert.False((fight.Flags & QuestChange.FWorker) != 0);  // judged as one step, never merged again
        var duel = Wo154Rules.WorkerCoalescer.Merged(ready[1]);
        Assert.Equal(duel.Old, duel.New);                       // 0->1->0: nothing to apply (the host answers "already")
        Assert.Equal(0, co.Count);
    }

    [Fact]
    public void A_State_that_keeps_moving_is_judged_after_five_seconds_and_partners_are_kept_apart()
    {
        var co = new Wo154Rules.WorkerCoalescer();
        co.Add(1, 1, W(Fight, 0, 1, "SetInProgress"), 0);
        co.Add(2, 2, W(Fight, 0, 1, "SetInProgress"), 0);      // another joiner's own step: his own entry
        for (long t = 1000; t <= 5000; t += 1000) co.Add(1, (uint)(10 + t), W(Fight, 1, 1, "SetInProgress"), t);
        var r = co.TakeReady(5000);
        Assert.Contains(r, x => x.Src == 1);                    // the 5 s ceiling, though it never went quiet
        Assert.Contains(r, x => x.Src == 2);                    // quiet since 0
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void A_bool_State_is_always_corrected_with_its_own_Set_ports()
    {
        Assert.Equal("SetTrue", Wo147Rules.CorrectionPort(null, "bool", 1, null));
        Assert.Equal("SetFalse", Wo147Rules.CorrectionPort(null, "bool", 0, null));
        Assert.Null(Wo147Rules.CorrectionPort(null, "bool", 7, null));   // not a bool's value: nothing guessed
    }

    [Fact]
    public void Developer_test_projects_do_not_veto_a_type_the_quests_agree_on()
    {
        Assert.True(QuestValueIndex.IsTestingFile("Quests/Testing/someone/asset_problem/bug.xml"));
        Assert.True(QuestValueIndex.IsTestingFile(@"Quests\Testing\x.xml"));
        Assert.False(QuestValueIndex.IsTestingFile("Quests/Final/Barbora.xml"));
        // the field's Moravian fight: with only the game's own definitions, Challenge has a port for every value
        var idx = new QuestValueIndex();
        idx.AddXml("<Type TypeName=\"Challenge\"><StateTypeEnumeration Name=\"None\"/><StateTypeEnumeration Name=\"InProgress\"/>"
                 + "<StateTypeEnumeration Name=\"Won\"/><StateTypeEnumeration Name=\"Lost\"/></Type>");
        Assert.Equal("SetInProgress", Wo147Rules.CorrectionPort(idx, "Challenge", 1, null));
        Assert.Equal("SetWon", Wo147Rules.CorrectionPort(idx, "Challenge", 2, null));
    }

    [Fact]
    public void The_guards_leave_a_partner_alone_while_he_is_down_and_for_two_minutes_after()
    {
        var r = new Wo154Rules.GuardRespite();
        Assert.Null(r.Blocked(0));                                   // never down: the guards act on his record
        r.Down(1000);
        Assert.Equal("down", r.Blocked(1056));                       // the field: a stop at his body 56 ms after the down
        r.Up(30_000);                                                // the respawn
        Assert.Equal("respite 120 s", r.Blocked(30_000));
        Assert.NotNull(r.Blocked(30_000 + 48_000));                  // the field: the "fled" stop 48 s after the respawn
        Assert.Null(r.Blocked(30_000 + Wo154Rules.GuardRespite.RespiteMs));
    }

    [Fact]
    public void A_lost_up_never_blocks_the_guards_for_the_rest_of_the_session_and_an_unstuck_gives_30_s()
    {
        var r = new Wo154Rules.GuardRespite();
        r.Down(0);
        Assert.Equal("down", r.Blocked(Wo154Rules.GuardRespite.MaxDownMs - 1));
        Assert.Null(r.Blocked(Wo154Rules.GuardRespite.MaxDownMs));   // WO-132's LifeGate rule
        var u = new Wo154Rules.GuardRespite();
        u.Unstuck(5000);
        Assert.NotNull(u.Blocked(5000 + 29_000));
        Assert.Null(u.Blocked(5000 + Wo154Rules.GuardRespite.UnstuckRespiteMs));
        u.Up(10_000);                                                // a respawn after it: the longer respite wins
        u.Unstuck(11_000);
        Assert.NotNull(u.Blocked(10_000 + 100_000));
    }

    [Fact]
    public void A_stop_turned_into_an_attack_on_a_standing_player_or_a_death_is_no_resist()
    {
        Assert.True(Wo139Text.IsResult("attacked"));
        Assert.True(Wo139Text.IsResult("died"));
        Assert.Equal(Wo139Rules.OutcomeEffect.Keep, Wo139Rules.EffectOf("attacked"));
        Assert.Equal(Wo139Rules.OutcomeEffect.Keep, Wo139Rules.EffectOf("died"));
        Assert.Equal(Wo139Rules.OutcomeEffect.Resist, Wo139Rules.EffectOf("fled"));   // a real flight still is
        Assert.True(Wo139Rules.TryParseOutcome("attacked tsem_man_9 0", out var res, out var guard, out int fine));
        Assert.Equal(("attacked", "tsem_man_9", 0), (res, guard, fine));
    }

    [Fact]
    public void Mp_unstuck_asks_the_host_to_end_the_fights_on_the_crime_channel()
    {
        Assert.Equal(4, Protocol.CrimeAskEndFights);
        Assert.Equal("end-fights", Protocol.CrimeAskName(Protocol.CrimeAskEndFights));
        Assert.Equal("unstuck", Wo154Rules.EndFightsText("unstuck"));
        Assert.Equal("unstuck", Wo154Rules.EndFightsText("no spaces allowed"));   // never a free text on the wire
        Assert.True(Wo154Rules.TryParseEndFights("unstuck", out var why));
        Assert.Equal("unstuck", why);
        Assert.False(Wo154Rules.TryParseEndFights("rm -rf", out _));
        Assert.False(Wo154Rules.TryParseEndFights("", out _));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, "1.5.5-release_1_5", "no-saves")]
    [InlineData(7, 0, 0, 0, "1.5.5-release_1_5", "only-host-copies")]          // the field's tester: 7 copies of the host's world
    [InlineData(5, 5, 0, 5, "1.5.5-release_1_5", "regular-game-saves")]       // "Your saves are from 1.5.5 -- host 1.5.5"
    [InlineData(5, 5, 0, 2, "1.5.5-release_1_5", "wrong-build")]
    [InlineData(3, 1, 1, 0, "1.5.5-release_1_5", "no-henry")]                 // the prologue save only
    [InlineData(3, 3, 0, 0, null, "no-henry")]                                // the host's build not announced: no build verdict
    public void A_joiner_without_a_save_to_bring_is_told_why_in_plain_words(int all, int own, int same, int regular, string? host, string reason)
    {
        Assert.Equal(reason, Wo154Rules.NoSaveReason(all, own, same, regular, host));
    }

    [Fact]
    public void The_regular_games_saves_are_told_from_the_Modding_Tools_by_their_build_number()
    {
        Assert.True(Wo154Rules.IsRegularGameBuild("1.5.5-15315-release_1_5"));
        Assert.True(Wo154Rules.IsRegularGameBuild("1.5.2-14493"));
        Assert.False(Wo154Rules.IsRegularGameBuild("1.5.5-release_1_5"));
        Assert.False(Wo154Rules.IsRegularGameBuild(null));
        Assert.StartsWith("Your saves are from the regular game, not the Modding Tools.", Wo154Rules.NoSaveMessage("regular-game-saves", false, null, null));
        Assert.EndsWith("You can join with a new character.", Wo154Rules.NoSaveMessage("only-host-copies", true, null, null));
        Assert.Contains("play past the prologue", Wo154Rules.NoSaveMessage("no-saves", false, null, null));
        Assert.DoesNotContain("play past the prologue, then join again", Wo154Rules.NoSaveMessage("wrong-build", false, "1.5.2-14493", "1.5.5-release_1_5"));
    }

    [Fact]
    public void A_busy_game_is_never_given_up_and_a_responsive_menu_three_times_is()
    {
        var w = new Wo154Rules.JoinLoadWatch();
        // the field: the main thread held 50+ s by the load -- the console answers nothing
        for (int s = 20; s < 80; s += 6) Assert.Equal(Wo154Rules.JoinLoadWatch.Verdict.Wait, w.Feed("busy", s, loadAccepted: false));
        Assert.Equal(Wo154Rules.JoinLoadWatch.Verdict.Wait, w.Feed("loading", 86, loadAccepted: true));
        Assert.Equal(Wo154Rules.JoinLoadWatch.Verdict.InWorld, w.Feed("world", 92, loadAccepted: true));   // its log line late
        var m = new Wo154Rules.JoinLoadWatch();
        Assert.Equal(Wo154Rules.JoinLoadWatch.Verdict.Wait, m.Feed("menu", 22, false));
        Assert.Equal(Wo154Rules.JoinLoadWatch.Verdict.Wait, m.Feed("busy", 28, false));             // a busy answer resets the count
        Assert.Equal(Wo154Rules.JoinLoadWatch.Verdict.Wait, m.Feed("menu", 34, false));
        Assert.Equal(Wo154Rules.JoinLoadWatch.Verdict.Wait, m.Feed("menu", 40, false));
        Assert.Equal(Wo154Rules.JoinLoadWatch.Verdict.Abort, m.Feed("menu", 46, false));
        var own = new Wo154Rules.JoinLoadWatch();                                                    // a join from the own world
        own.Feed("world", 22, false); own.Feed("world", 28, false);
        Assert.Equal(Wo154Rules.JoinLoadWatch.Verdict.Abort, own.Feed("world", 34, false));          // still the own world: the load never took
        Assert.Equal(Wo154Rules.JoinLoadWatch.Verdict.GaveUpBusy, new Wo154Rules.JoinLoadWatch().Feed("busy", Wo154Rules.JoinLoadWatch.MaxWaitS, false));
    }

    [Fact]
    public void A_placed_world_file_goes_only_when_no_load_can_be_reading_it()
    {
        Assert.True(Wo154Rules.PlacedFileMayGo(loadCommanded: false, gameplayStarted: false, loadFailed: false, where: "busy"));   // before the load
        Assert.False(Wo154Rules.PlacedFileMayGo(true, false, false, "busy"));      // the field's delete at 02:17:50
        Assert.False(Wo154Rules.PlacedFileMayGo(true, false, false, "loading"));
        Assert.True(Wo154Rules.PlacedFileMayGo(true, false, false, "menu"));
        Assert.True(Wo154Rules.PlacedFileMayGo(true, false, false, "world"));
        Assert.True(Wo154Rules.PlacedFileMayGo(true, true, false, "busy"));        // past Gameplay started: the engine read it
        Assert.True(Wo154Rules.PlacedFileMayGo(true, false, true, "busy"));        // the engine reported the failed load
    }

    [Fact]
    public void The_contested_verdict_parses_and_the_worker_flag_reads()
    {
        string text = Wo137Rules.ResultText(Wo154Rules.VerdictContested, 1, "", Trigger);
        Assert.True(Wo137Rules.TryParseResultText(text, out var v, out int hv, out var port, out var path));
        Assert.Equal("contested", v);
        Assert.Equal(1, hv);
        Assert.Equal("", port);
        Assert.Equal(Trigger, path);
        var q = new QuestChange(1, QuestChange.FNotify | QuestChange.FWorker, 1, 0, "SetFalse", "bool", Trigger, 40);
        Assert.True(q.Worker);
        Assert.False(q.Cascade);
        Assert.NotEqual(QuestChange.FWorker, Wo147Rules.FlagConversation);
    }
}

/// <summary>WO-154 Phase 6.3 and 6.5: a joiner's own skip set back; what the minigame rule keeps per machine.</summary>
public class Wo154RestTests
{
    [Fact]
    public void A_set_back_right_after_this_players_own_skip_is_told_once_a_minute()
    {
        // ended 5 s ago, not shared, nothing told yet
        Assert.True(Wo154Rules.TellSkipUndone(true, false, 100_000, 95_000, false, 0));
        // the switch off
        Assert.False(Wo154Rules.TellSkipUndone(false, false, 100_000, 95_000, false, 0));
        // a shared skip (now, or the one that ended)
        Assert.False(Wo154Rules.TellSkipUndone(true, true, 100_000, 95_000, false, 0));
        Assert.False(Wo154Rules.TellSkipUndone(true, false, 100_000, 95_000, true, 0));
        // no own skip, or one that ended long ago: a cutscene's time jump or the host's reload is not his doing
        Assert.False(Wo154Rules.TellSkipUndone(true, false, 100_000, 0, false, 0));
        Assert.False(Wo154Rules.TellSkipUndone(true, false, 100_000, 100_000 - Wo154Rules.SkipTellWindowMs - 1, false, 0));
        // told 30 s ago: quiet; told a minute ago: again
        Assert.False(Wo154Rules.TellSkipUndone(true, false, 100_000, 95_000, false, 70_000));
        Assert.True(Wo154Rules.TellSkipUndone(true, false, 100_000, 95_000, false, 100_000 - Wo154Rules.SkipTellQuietMs));
    }

    [Fact]
    public void The_set_back_line_is_plain()
    {
        Assert.DoesNotContain("mp_", Wo154Rules.SkipUndoneText);
        Assert.Contains("host", Wo154Rules.SkipUndoneText);
        Assert.True(Wo154Rules.SkipUndoneText.Length <= 160);
    }

    [Theory]
    [InlineData(Protocol.TimeSkipPhaseDoneQuiet, Protocol.TimeSkipKindUnknown, true)]    // the field's 52 of 54 "drops"
    [InlineData(Protocol.TimeSkipPhaseSync, Protocol.TimeSkipKindUnknown, true)]
    [InlineData(Protocol.TimeSkipPhaseStart, Protocol.TimeSkipKindWait, false)]         // a real wait
    [InlineData(Protocol.TimeSkipPhaseDone, Protocol.TimeSkipKindWait, false)]
    [InlineData(Protocol.TimeSkipPhaseDone, Protocol.TimeSkipKindSleep, false)]
    [InlineData(Protocol.TimeSkipPhaseDone, Protocol.TimeSkipKindFastTravel, false)]
    public void A_clock_announce_is_not_a_time_skip(byte phase, byte kind, bool announce)
    {
        Assert.Equal(announce, Wo154Rules.IsClockAnnounce(phase, kind));
    }

    // the game's one State named after a minigame: the knight's dice in utokNaNebakov (a DiceState the quest branches on)
    private const string KnightDice = "Barbora.trosecko.utokNaNebakov.hibernovana_gameplay.na_troskach.porada_s_bergovem.nespokojeny_stav.kostky_s_rytirem.dice_minigame";

    [Fact]
    public void The_knights_dice_is_the_quests_and_is_shared()
    {
        Assert.False(Wo137Rules.PlayerMinigame(KnightDice, "DiceState", true));
        Assert.True(Wo137Rules.PlayerMinigame(KnightDice, "DiceState", false));      // mp_minigame_outcome off: WO-151's rule
        var won = new QuestChange(1, QuestChange.FNotify, 1, 2, "SetWon", "DiceState", KnightDice, "Barbora.trosecko.utokNaNebakov".Length);
        if (Wo137Rules.MinigameOutcomeShared) Assert.Null(Wo137Rules.HostSendVeto(won));
    }

    [Theory]
    // the blacksmith's tutorial module (the field's anvil) and the four tutorial drivers stay each player's own, either way
    [InlineData("Barbora.trosecko.kovar.hibernace.blacksmithing_minigame.tutorialState", "BlacksmithingTutorialProgress")]
    [InlineData("Barbora.trosecko.kovar.hibernace.blacksmithing_minigame.pohyb_mysi__nahrarit_mece.nazhav_obrubek_state", "Progress")]
    [InlineData("Barbora.trosecko.kovar.hibernace.blacksmithing_minigame.dokonceni_mece.state1", "Progress")]
    [InlineData("Barbora.trosecko.kovar.hibernace.blacksmithing_minigame.dokonceni_mece.state3", "Progress")]
    [InlineData("Barbora.trosecko.kovar.hibernace.blacksmithing_minigame.ke_kovadline_a_posouvani_mece_po_kovadline.state3", "Progress")]
    [InlineData("Barbora.trosecko.masterstrike_tutorial.masterstrike_tutorial__kocour.state33", "MasterstrikeTutorialProgress")]
    [InlineData("Barbora.trosecko.prepadeni.hibernovana_cast.tabor.serm_s_ptackem.tutorialProgress", "CombatTutorialProgress")]
    [InlineData("Barbora.trosecko.zachrana.hibernace.leceni_ptacka__druhy_den.alchemy_tutorial.alchemy_tutorial.state31", "AlchemyTutorialProgress")]
    public void The_minigames_own_states_stay_per_machine(string path, string type)
    {
        Assert.True(Wo137Rules.PlayerMinigame(path, type, true));
        Assert.True(Wo137Rules.PlayerMinigame(path, type, false));
    }

    [Theory]
    // the main-quest tutorials' journal objectives are views over their own step States: shared
    [InlineData("Barbora.trosecko.prepadeni.hibernovana_cast.tabor.serm_s_ptackem.n0_vytas_zbran.vytas_mec", "Progress")]
    [InlineData("Barbora.trosecko.zachrana.hibernace.leceni_ptacka__druhy_den.alchemy_tutorial.alchemy_tutorial.naliti_zakladu.state5", "Progress")]
    [InlineData("Barbora.trosecko.zachrana.hibernace.leceni_ptacka__druhy_den.alchemy_tutorial.makeHealingPotion", "Progress")]
    public void The_tutorials_journal_objectives_are_shared(string path, string type)
    {
        Assert.False(Wo137Rules.PlayerMinigame(path, type, true));
        Assert.False(Wo137Rules.PlayerMinigame(path, type, false));
    }
}
