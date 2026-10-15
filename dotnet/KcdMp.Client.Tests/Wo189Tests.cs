// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-189 (0.60.6): the rules (Wo189Rules) -- A1-A4's timings, the profile filter and catch-up, C's 10 s, D's 20 s.</summary>
public class Wo189Tests
{
    // ================================================================ A2: the watchdog

    [Fact]
    public void The_watchdog_acts_once_when_a_save_has_been_pending_a_minute()
    {
        var w = new Wo189Rules.PendingWatch();
        Assert.False(w.Feed(true, 1_000));
        Assert.False(w.Feed(true, 60_999));
        Assert.True(w.Feed(true, 61_000));
        Assert.False(w.Feed(true, 66_000));    // once per stretch
        Assert.False(w.Feed(false, 70_000));   // cleared
        Assert.False(w.Feed(true, 71_000));    // a new stretch
        Assert.True(w.Feed(true, 131_000));
    }

    [Fact]
    public void The_watchdog_counts_from_the_engines_own_start_when_it_is_known()
    {
        var w = new Wo189Rules.PendingWatch();
        // the engine said "started async waiting" at 10 s; the first 5 s read sees it at 14 s
        Assert.False(w.Feed(true, 14_000, knownSinceMs: 10_000));
        Assert.Equal(10_000, w.SinceMs);
        Assert.True(w.Feed(true, 70_000));
        // a "since" in the future is not believed
        var v = new Wo189Rules.PendingWatch();
        v.Feed(true, 5_000, knownSinceMs: 9_000);
        Assert.Equal(5_000, v.SinceMs);
    }

    [Fact]
    public void A_pending_save_is_named_once_after_five_seconds()
    {
        var w = new Wo189Rules.PendingWatch();
        w.Feed(true, 100);
        Assert.False(w.NameDue(5_099));
        Assert.True(w.NameDue(5_100));
        Assert.False(w.NameDue(9_000));
        w.Feed(false, 9_500);
        Assert.False(w.NameDue(20_000));
    }

    [Fact]
    public void The_watchdog_period_and_age_are_the_work_orders()
    {
        Assert.Equal(5_000, Wo189Rules.WatchdogPeriodMs);
        Assert.Equal(60_000, Wo189Rules.WatchdogPendingMs);
        Assert.Equal(15_000, Wo189Rules.NotReadyRetryMs);
        Assert.Equal(5_000, Wo189Rules.PendingNameMs);
    }

    // ================================================================ A3: ask only when the engine can take it

    [Fact]
    public void A_snapshot_is_asked_only_when_nothing_holds_the_engine()
    {
        Assert.Equal("", Wo189Rules.AskBlock(true, "", false, ""));
        Assert.Equal("", Wo189Rules.AskBlock(null, "", false, ""));   // not read: the engine's pending state decides (0.60.5)
        Assert.Contains("readiness", Wo189Rules.AskBlock(false, "C_XGenAIModule[0x2]", false, ""));
        Assert.Contains("C_XGenAIModule", Wo189Rules.AskBlock(false, "C_XGenAIModule[0x2]", false, ""));
        Assert.Contains("fast-forward", Wo189Rules.AskBlock(true, "", true, ""));
        Assert.Contains("konec", Wo189Rules.AskBlock(true, "", false, "konec"));
        // the positioning is named first: a request into it is what made the field's chain
        Assert.Contains("positioning", Wo189Rules.AskBlock(false, "x", true, "konec"));
    }

    [Fact]
    public void The_readiness_read_knows_the_positioning_managers_wait()
    {
        Assert.True(new Wo189Rules.ReadyRead(true, true, true, 2, true, 3, "").PositioningWaiting);
        Assert.False(new Wo189Rules.ReadyRead(true, true, true, 2, false, 3, "").PositioningWaiting);
        Assert.False(new Wo189Rules.ReadyRead(true, true, true, 4, true, 3, "").PositioningWaiting);
        Assert.False(new Wo189Rules.ReadyRead(true, true, false, 2, true, 3, "").PositioningWaiting);
    }

    [Fact]
    public void The_cancel_line_names_the_purpose_and_the_wait()
    {
        string l = Wo189Rules.CancelLine("final snapshot (the ESC menu)", 15_519, "cleared");
        Assert.StartsWith("WO189-SAVE cancelled the pending save of final snapshot (the ESC menu) after 15.5 s (no file)", l);
    }

    // ================================================================ A4: the joiner's own snapshot, the host's reminder

    [Fact]
    public void The_own_snapshot_is_due_ten_minutes_after_the_last_pair_or_ask()
    {
        Assert.Equal(600_000, Wo189Rules.AutoSnapMs);
        Assert.False(Wo189Rules.AutoSnapDue(1_000, 0, 600_999, Wo189Rules.AutoSnapMs));
        Assert.True(Wo189Rules.AutoSnapDue(1_000, 0, 601_000, Wo189Rules.AutoSnapMs));
        Assert.False(Wo189Rules.AutoSnapDue(1_000, 300_000, 601_000, Wo189Rules.AutoSnapMs));   // asked since: not again before 10 min
        Assert.False(Wo189Rules.AutoSnapDue(0, 0, 5_000_000, 0));                                // off
    }

    [Fact]
    public void The_reminder_shows_every_fifteen_minutes_in_a_session_and_a_pair_restarts_it()
    {
        Assert.Equal(900_000, Wo189Rules.ReminderMs);
        var r = new Wo189Rules.Reminder();
        Assert.False(r.Due(10_000_000, true, Wo189Rules.ReminderMs));   // no session started
        r.SessionStarted(0);
        Assert.False(r.Due(899_999, true, Wo189Rules.ReminderMs));
        Assert.True(r.Due(900_000, true, Wo189Rules.ReminderMs));
        Assert.False(r.Due(1_000_000, true, Wo189Rules.ReminderMs));    // once per 15 minutes
        Assert.True(r.Due(1_800_000, true, Wo189Rules.ReminderMs));
        r.PartnerSaved(1_900_000);
        Assert.False(r.Due(2_700_000, true, Wo189Rules.ReminderMs));
        Assert.True(r.Due(2_800_000, true, Wo189Rules.ReminderMs));
        Assert.False(r.Due(9_000_000, false, Wo189Rules.ReminderMs));   // only in a session
        r.SessionEnded();
        Assert.False(r.Due(9_900_000, true, Wo189Rules.ReminderMs));
        Assert.Equal(2, Wo189Rules.ReminderText(15).Split("15 minutes").Length);
        Assert.Contains("press Home to save", Wo189Rules.ReminderText(15));
    }

    [Fact]
    public void The_switches_default_as_the_work_order_says()
    {
        Assert.True(Wo189Rules.DefaultAutoSnap);
        Assert.True(Wo189Rules.DefaultReminder);
        Assert.True(Wo189Rules.DefaultCutsceneRelease);
        Assert.True(Wo189Rules.DefaultPositioningBound);
        Assert.True(Wo189Rules.DefaultProfileMirror);    // B: the WO said off; its live check passed (the joiner's own log: active, then inactive) -- the ship-on rule
    }

    // ================================================================ B: the profile filter, the capture, the catch-up

    private static readonly HashSet<string> Quest = new(StringComparer.Ordinal) { "masterstrikeTutorial_arenaBarrier", "fogControl_krcak", "weather_bees", "event_x" };

    [Fact]
    public void Only_quest_profiles_are_mirrored_never_weather_or_events()
    {
        Assert.True(Wo189Rules.IsQuestProfile("masterstrikeTutorial_arenaBarrier", Quest));
        Assert.True(Wo189Rules.IsQuestProfile("fogControl_krcak", Quest));
        Assert.False(Wo189Rules.IsQuestProfile("weather_bees", Quest));   // even when a quest script names it
        Assert.False(Wo189Rules.IsQuestProfile("event_x", Quest));
        Assert.False(Wo189Rules.IsQuestProfile("zaby_cows", Quest));
        Assert.False(Wo189Rules.IsQuestProfile("", Quest));
    }

    [Fact]
    public void The_shipped_quest_list_holds_the_tutorials_barrier()
    {
        var q = Wo189Rules.QuestProfiles;
        Assert.True(q.Count > 1500, $"{q.Count}");
        Assert.Contains("masterstrikeTutorial_arenaBarrier", q);
        Assert.DoesNotContain(q, n => n.StartsWith('#'));
    }

    [Fact]
    public void The_capture_forwards_changes_in_play_only()
    {
        var c = new Wo189Rules.ProfileCapture();
        string act = "[Info] C_GameProfileManager: Activating profile 'masterstrikeTutorial_arenaBarrier'.";
        string deact = "[Info] C_GameProfileManager: Deactivating profile 'masterstrikeTutorial_arenaBarrier'.";
        Assert.True(c.Feed(act, Quest)!.Forward);
        Assert.Equal(-1, c.Feed(deact, Quest)!.Op);
        Assert.Null(c.Feed("GameProfileManager: Starting batch update.", Quest));
        var inBatch = c.Feed(act, Quest)!;
        Assert.False(inBatch.Forward);
        Assert.Contains("batch", inBatch.Why);
        Assert.Null(c.Feed("GameProfileManager: Finished batch update.", Quest));
        c.Loading();
        Assert.Contains("loading", c.Feed(act, Quest)!.Why);
        c.GameplayStarted();
        Assert.True(c.Feed(act, Quest)!.Forward);
        Assert.Contains("weather", c.Feed("[Info] C_GameProfileManager: Activating profile 'weather_bees'.", Quest)!.Why);
        Assert.Contains("random event", c.Feed("[Info] C_GameProfileManager: Deactivating profile 'event_x'.", Quest)!.Why);
        Assert.Contains("not a quest", c.Feed("[Info] C_GameProfileManager: Activating profile 'zaby_cows'.", Quest)!.Why);
        Assert.Null(c.Feed("[Info] C_GameProfileManager: Activating profile weather_bees which is already active.", Quest));
        Assert.Null(c.Feed("Something else", Quest));
    }

    [Fact]
    public void The_catch_up_is_the_active_quest_profiles_sorted_once_each()
    {
        var got = Wo189Rules.CatchUp(["weather_bees", "masterstrikeTutorial_arenaBarrier", "zaby_cows", "fogControl_krcak", "fogControl_krcak", "event_x"], Quest);
        Assert.Equal(["fogControl_krcak", "masterstrikeTutorial_arenaBarrier"], got);
        Assert.Empty(Wo189Rules.CatchUp([], Quest));
    }

    [Fact]
    public void The_profile_message_round_trips_and_refuses_junk()
    {
        foreach (char op in new[] { '+', '-', '=' })
        {
            string t = Wo189Rules.ProfileText(op, "masterstrikeTutorial_arenaBarrier");
            Assert.True(t.Length <= Protocol.W164TextMax);
            Assert.True(Wo189Rules.TryParseProfileText(t, out char o, out string n));
            Assert.Equal(op, o);
            Assert.Equal("masterstrikeTutorial_arenaBarrier", n);
        }
        Assert.True(Wo189Rules.TryParseProfileText("?", out char q, out _));
        Assert.Equal('?', q);
        Assert.False(Wo189Rules.TryParseProfileText("+", out _, out _));
        Assert.False(Wo189Rules.TryParseProfileText("*x", out _, out _));
        Assert.False(Wo189Rules.TryParseProfileText("+a b", out _, out _));
        Assert.False(Wo189Rules.TryParseProfileText("+a\"b", out _, out _));
        Assert.False(Wo189Rules.TryParseProfileText(null, out _, out _));
        Assert.Equal("profile", Protocol.W164KindName(Protocol.W164Profile));
        Assert.Equal(17, Protocol.W164Profile);
    }

    // ================================================================ C: the stuck cutscene detector

    private const string InitLine = "CutscenePlayer::OnCutsceneInitialized called for Fader cutscene 'masterstrikeTutorial_konecTutorialu_vasko' with holder 'masterstrikeTutorial_unstreamBarrierCH' from module 'brambora::Barbora::utils::masterstriketutorial_::barrier'";

    [Fact]
    public void The_initialized_line_gives_type_name_and_holder()
    {
        Assert.True(Wo189Rules.TryParseInitialized(InitLine, out string t, out string n, out string h));
        Assert.Equal("Fader", t);
        Assert.Equal("masterstrikeTutorial_konecTutorialu_vasko", n);
        Assert.Equal("masterstrikeTutorial_unstreamBarrierCH", h);
        Assert.True(Wo189Rules.TryParseInitialized("CutscenePlayer::OnCutsceneInitialized called for Ingame cutscene 'a_b'", out _, out string n2, out string h2));
        Assert.Equal("a_b", n2);
        Assert.Equal("", h2);
        Assert.False(Wo189Rules.TryParseInitialized("CutscenePlayer::PlayCutscene called for Fader cutscene 'x'", out _, out _, out _));
    }

    [Fact]
    public void A_cutscene_is_stuck_ten_seconds_after_initialized_with_no_further_stage()
    {
        var w = new Wo189Rules.CutsceneWatch();
        w.Initialized("Fader", "konec", "unstreamBarrierCH", 1_000);
        Assert.False(w.Due(10_999));
        Assert.True(w.Due(11_000));
        Assert.Equal("konec", w.StuckName);
        Assert.False(w.Due(30_000));   // once
        // the normal path: PlayCutscene within 10 s ends the watch
        var v = new Wo189Rules.CutsceneWatch();
        v.Initialized("Fader", "a", "", 1_000);
        v.Stage("PlayCutscene", "a");
        Assert.False(v.Due(60_000));
        // a stage of another cutscene does not end it
        var u = new Wo189Rules.CutsceneWatch();
        u.Initialized("Fader", "a", "", 1_000);
        u.Stage("PlayCutscene", "b");
        Assert.True(u.Due(11_000));
        Assert.Equal(10_000, Wo189Rules.CutsceneStuckMs);
    }

    [Fact]
    public void The_last_resort_waits_ten_seconds_after_the_release_and_comes_once()
    {
        var w = new Wo189Rules.CutsceneWatch();
        w.Initialized("Fader", "a", "", 1_000);
        Assert.True(w.Due(11_000));
        w.Released(12_000);
        Assert.False(w.LastResortDue(21_999));
        Assert.True(w.LastResortDue(22_000));
        Assert.False(w.LastResortDue(40_000));
        // the normal sequence followed: no last resort
        var v = new Wo189Rules.CutsceneWatch();
        v.Initialized("Fader", "a", "", 1_000);
        v.Due(11_000);
        v.Released(12_000);
        v.Stage("PlayCutscene", "a");
        Assert.False(v.LastResortDue(30_000));
    }

    // ================================================================ D: the positioning bound

    [Fact]
    public void A_scene_still_positioning_twenty_seconds_gets_its_copies_fast_forward_stopped_once()
    {
        Assert.Equal(20_000, Wo189Rules.PositioningBoundMs);
        var p = new Wo189Rules.PositioningWatch();
        p.Enqueued(1_000);
        Assert.False(p.Due(20_999, false, false, null));
        Assert.False(p.Due(21_000, true, false, null));    // a conversation with the player runs
        Assert.False(p.Due(21_000, false, true, null));    // a cutscene plays
        Assert.False(p.Due(21_000, false, false, false));  // the manager says it does not wait
        Assert.True(p.Due(21_000, false, false, null));
        Assert.False(p.Due(40_000, false, false, null));   // once per scene
        p.Ended();
        Assert.Equal(0, p.Open);
        p.Enqueued(50_000);
        Assert.True(p.Due(70_000, false, false, true));
        Assert.Equal(2, p.Acts);
    }

    [Fact]
    public void The_positioning_watch_is_fifo_and_a_load_resets_it()
    {
        var p = new Wo189Rules.PositioningWatch();
        p.Enqueued(1_000); p.Enqueued(5_000);
        p.Ended();
        Assert.Equal(5_000, p.OldestMs);
        p.Ended(); p.Ended();   // more ends than enqueues never goes below zero
        Assert.Equal(0, p.Open);
        p.Enqueued(1_000);
        p.Reset();
        Assert.False(p.Due(100_000, false, false, null));
    }

    // ================================================================ F

    [Fact]
    public void The_barrier_reads_name_the_tutorials_two_waits()
    {
        string quest = Wo189Rules.QuestOfDialogue("Barbora.trosecko.masterstrike_tutorial.po_duelu_1");
        Assert.Equal("Barbora.trosecko.masterstrike_tutorial", quest);
        var p = Wo189Rules.BarrierPaths("masterstrikeTutorial_unstreamBarrierCH", quest);
        Assert.NotNull(p);
        Assert.Equal("Barbora.trosecko.masterstrike_tutorial.barrier.stopcrime_1.waitingForReactors", p!.Value.StopCrime);
        Assert.Equal("Barbora.trosecko.masterstrike_tutorial.barrier.streamprofileshandling.streamHandling", p.Value.Streaming);
        Assert.Null(Wo189Rules.BarrierPaths("someHolderCH", quest));
        Assert.Equal("Unstreamed", Wo189Rules.StreamingName(4));
        Assert.Equal("None", Wo189Rules.StreamingName(0));
        Assert.Null(Wo189Rules.BarrierPaths("masterstrikeTutorial_unstreamBarrierCH", ""));
    }

    // ================================================================ the source's guarantees

    private static string Src(string f)
    {
        string d = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(d, "dotnet", "KcdMp.Client", f))) d = Path.GetDirectoryName(d)!;
        return File.ReadAllText(Path.Combine(d, "dotnet", "KcdMp.Client", f));
    }

    [Fact]
    public void The_mod_never_requests_a_save_to_unstick_a_scene_or_a_cutscene()
    {
        string s = Src("GameBridge.Wo189.cs");
        Assert.DoesNotContain("QuickSave", s);
        Assert.DoesNotContain("KCD2MP_Wo125Snapshot", s);
        Assert.DoesNotContain("SaveManual", s);
    }

    [Fact]
    public void Every_snapshot_wait_that_ends_without_a_file_cancels_and_asks_the_readiness_first()
    {
        string s = Src("GameBridge.Wo184.cs");
        Assert.Contains("await Wo189CancelAfterWaitAsync(purpose, tReq);", s);
        Assert.Contains("Wo189Rules.CancelLine(purpose", s);
        int ask = s.IndexOf("await Wo189AskBlockAsync()", StringComparison.Ordinal);
        int req = s.IndexOf("AskModAsync(\"KCD2MP_Wo125Snapshot\"", StringComparison.Ordinal);
        Assert.True(ask > 0 && req > ask, "A3's readiness check must come before the request");
    }
}
