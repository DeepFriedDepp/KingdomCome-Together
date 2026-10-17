// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-194: the pure rules of shared in-game cutscenes (Wo194Rules).</summary>
public class Wo194Tests
{
    [Fact]
    public void Every_mechanism_ships_on()
    {
        Assert.True(Wo194Rules.DefaultShared && Wo194Rules.DefaultHold && Wo194Rules.DefaultFfStop && Wo194Rules.DefaultTimeGate
                    && Wo194Rules.DefaultNudge && Wo194Rules.DefaultFigureHide && Wo194Rules.DefaultSkipBoth);
    }

    // ---- the trigger message (B1) ----
    [Fact]
    public void The_trigger_messages_round_trip_and_fit_the_bus()
    {
        const string name = "zbranePanaSemina_playerSukDialog2";
        foreach (var (text, op) in new[] {
            (Wo194Rules.Started(12, name), 'C'), (Wo194Rules.Runs(12, name), 'G'), (Wo194Rules.Stopped(12, name), 'X'), (Wo194Rules.Ended(12, name), 'Z') })
        {
            Assert.True(text.Length <= 80, text);
            Assert.True(Wo194Rules.IsIngameOp(text));
            Assert.True(Wo194Rules.TryParse(text, out var m));
            Assert.Equal(op, m.Op); Assert.Equal(12, m.Seq); Assert.Equal(name, m.Name);
        }
        Assert.True(Wo194Rules.TryParse(Wo194Rules.Answer(7, Wo194Rules.AnsStarted, name), out var a));
        Assert.Equal('A', a.Op); Assert.Equal(1, a.Code); Assert.Equal(name, a.Name);
        Assert.True(Wo194Rules.TryParse(Wo194Rules.Clock(7, 23370, name), out var p));
        Assert.Equal('P', p.Op); Assert.Equal(23370, p.TimeMs);
        Assert.True(Wo194Rules.TryParse(Wo194Rules.Jumped(7, 69500, name), out var j));
        Assert.Equal('J', j.Op); Assert.Equal(69500, j.TimeMs);
        Assert.True(Wo194Rules.Clock(99999, 3_600_000, new string('a', 64)).Length <= 80, "the longest clock message fits");
    }

    [Fact]
    public void Malformed_or_foreign_text_is_refused()
    {
        Assert.False(Wo194Rules.TryParse("C 1 bad name", out _));
        Assert.False(Wo194Rules.TryParse("C x name", out _));
        Assert.False(Wo194Rules.TryParse("A 1 9 name", out _), "unknown answer code");
        Assert.False(Wo194Rules.TryParse("P 1 -5 name", out _));
        Assert.False(Wo194Rules.TryParse("Q 1 a/b", out _), "a path is letters, digits, _ - .");
        Assert.False(Wo194Rules.IsIngameOp("S 1 V intro_new_game"), "WO-179's letters stay WO-179's");
        Assert.False(Wo194Rules.IsIngameOp("E 1 end"));
        Assert.True(Wo179Rules.TryParse("S 1 V intro_new_game", out _), "the video bus is untouched");
    }

    [Fact]
    public void A_long_handler_path_goes_in_pieces_that_join_back()
    {
        const string path = "Barbora.trosecko.zbranePanaSemina.h.na_semine.cin_s0160t_zbranesemina__meet_semin.cutscenehandler4_1";
        var pieces = Wo194Rules.HandlerPieces(31, path);
        Assert.True(pieces.Count >= 2);
        string joined = "";
        foreach (var q in pieces) { Assert.True(q.Length <= 80); Assert.True(Wo194Rules.TryParse(q, out var m)); Assert.Equal('Q', m.Op); joined += m.Path; }
        Assert.Equal(path, joined);
        Assert.Empty(Wo194Rules.HandlerPieces(31, "not a path!"));
    }

    // ---- the handler mapping (B2) ----
    [Fact]
    public void The_data_mapping_gives_both_semin_scenes_their_handlers()
    {
        Assert.True(Wo194Rules.HandlerCount >= 200, $"{Wo194Rules.HandlerCount} rows");
        var a = Assert.Single(Wo194Rules.HandlersOf("zbranePanaSemina_seminArrival"));
        Assert.Equal("zbranePSemina_seminArrival", a.Holder);
        Assert.Equal("Barbora.trosecko.zbranePanaSemina.h.na_semine.na_semine.cutscenehandler4", Wo194Rules.HandlerPath(a));
        var b = Assert.Single(Wo194Rules.HandlersOf("zbranePanaSemina_playerSukDialog2"));
        Assert.Equal("cutscenehandler4_1", b.Node);
        Assert.Equal("Barbora.trosecko.zbranePanaSemina.h.na_semine.seznameni_se_seminem1.cutscenehandler4_1", Wo194Rules.HandlerPath(b));   // live-verified (FindNode)
        Assert.Empty(Wo194Rules.HandlersOf("crime_beating_trosecko"));
        Assert.True(Wo194Rules.HandlersOf("nebakovObrana_godFinger").Count == 2, "the scene's handler and its catch-up handler");
    }

    [Fact]
    public void A_scene_played_since_the_last_load_is_never_started_again()
    {
        Assert.Null(Wo194Rules.StartRefusal(shared: true, inWorld: true, crimeScene: false, playedSinceLoad: false));
        Assert.Contains("already played", Wo194Rules.StartRefusal(true, true, false, playedSinceLoad: true));   // live: its handler reads idle again
        Assert.Contains("off", Wo194Rules.StartRefusal(false, true, false, false));
        Assert.Contains("not in the world", Wo194Rules.StartRefusal(true, false, false, false));
        Assert.Contains("crime", Wo194Rules.StartRefusal(true, true, true, false));
    }

    [Fact]
    public void A_start_result_becomes_the_partners_answer()
    {
        Assert.Equal(Wo194Rules.AnsStarted, Wo194Rules.AnswerOfStart(1));
        Assert.Equal(Wo194Rules.AnsBystander, Wo194Rules.AnswerOfStart(0));   // not idle: not at that step
        Assert.Equal(Wo194Rules.AnsBystander, Wo194Rules.AnswerOfStart(2));   // no such handler
        Assert.Equal(Wo194Rules.AnsBystander, Wo194Rules.AnswerOfStart(7));   // hibernated
        Assert.Equal(Wo194Rules.AnsRefused, Wo194Rules.AnswerOfStart(3));
        Assert.Equal(Wo194Rules.AnsRefused, Wo194Rules.AnswerOfStart(6));
    }

    // ---- the hold bound (B3; the native half pins the release order) ----
    [Fact]
    public void The_hold_is_bounded_at_three_seconds_in_the_dll()
    {
        // the agent's switch arms it; the bound itself lives in the DLL (wo194_rules.h kHoldBoundMs = 3000, native/tests/wo194_tests.cpp)
        Assert.True(Wo194Rules.DefaultHold);
    }

    // ---- time alignment (B3) ----
    [Fact]
    public void The_follower_sets_its_clock_once_then_only_past_the_drift()
    {
        double target = Wo194Rules.AlignTargetS(10.000, 40, 15);
        Assert.Equal(10.055, target, 3);
        Assert.True(Wo194Rules.NeedsSet(9.0, target, firstDone: false), "first: set");
        Assert.False(Wo194Rules.NeedsSet(10.03, target, firstDone: false), "first, within a frame or two: left");
        Assert.False(Wo194Rules.NeedsSet(10.20, target, firstDone: true), "0.145 s drift: left");
        Assert.True(Wo194Rules.NeedsSet(10.30, target, firstDone: true), "0.245 s drift: set again");
        Assert.False(Wo194Rules.NeedsSet(-1, target, false), "not playing: never set");
        Assert.True(Wo194Rules.IsFollower(ownPlayMs: 5000, partnerRunsArrivedMs: 4900), "the partner ran first: follow");
        Assert.False(Wo194Rules.IsFollower(ownPlayMs: 5000, partnerRunsArrivedMs: 5100), "this copy ran first: lead");
        Assert.False(Wo194Rules.IsFollower(5000, 0), "the partner never ran it: nothing to follow");
    }

    // ---- the skip messages (C) ----
    [Fact]
    public void A_skip_press_is_read_as_a_jump_a_stop_or_nothing()
    {
        // 0.60.9's seminArrival: presses 2, 3, 4 at ~4.1 s into the sequence = 23.37, then 69.5, then the end
        Assert.Equal(Wo194Rules.SkipSeen.Jump, Wo194Rules.Classify(true, 23.37, 3.5, 600));
        Assert.Equal(Wo194Rules.SkipSeen.Jump, Wo194Rules.Classify(true, 69.5, 23.4, 200));
        Assert.Equal(Wo194Rules.SkipSeen.Stopped, Wo194Rules.Classify(false, -1, 69.5, 200));
        Assert.Equal(Wo194Rules.SkipSeen.None, Wo194Rules.Classify(true, 4.25, 3.5, 600));   // the arming press: the clock just ran
        Assert.Equal(Wo194Rules.SkipSeen.None, Wo194Rules.Classify(true, 30, -1, 0));   // no earlier reading: nothing claimed
    }

    // ---- the end (A) ----
    [Fact]
    public void The_end_window_and_the_nudge()
    {
        Assert.True(Wo194Rules.FfStopEveryMs <= 250 && Wo194Rules.FfStopWindowMs >= 2000);
        Assert.True(Wo194Rules.NudgeWanted(joiner: true, on: true, hostPlayedIt: true));
        Assert.False(Wo194Rules.NudgeWanted(joiner: false, on: true, hostPlayedIt: true), "the host's Henry is never moved");
        Assert.False(Wo194Rules.NudgeWanted(joiner: true, on: true, hostPlayedIt: false), "a solo scene: only one Henry on the tag point");
        Assert.Equal(1.0f, Wo194Rules.NudgeM);
    }

    // ---- time of day (D) ----
    [Fact]
    public void The_time_table_lists_the_scenes_that_set_the_clock()
    {
        Assert.Equal(93, Wo194Rules.TimeCount);
        Assert.True(Wo194Rules.TryTime("bohutovaVlozka_pistaRelease", out string t) && t == "17h30m");
        Assert.False(Wo194Rules.TryTime("zbranePanaSemina_seminArrival", out _), "the Semin scenes set no time");
    }

    // ---- the DLL's events ----
    [Fact]
    public void The_dlls_poll_reply_is_parsed()
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes(9u)); b.Add(2);
        void Ev(uint seq, byte kind, string name, string path, string det)
        {
            b.AddRange(BitConverter.GetBytes(seq)); b.Add(kind); b.AddRange(BitConverter.GetBytes(120u));
            foreach (var s in new[] { name, path, det }) { b.Add((byte)s.Length); b.AddRange(System.Text.Encoding.ASCII.GetBytes(s)); }
        }
        Ev(8, 3, "zbranePanaSemina_seminArrival", "Barbora.x.cutscenehandler4", "autoplay=1");
        Ev(9, 4, "zbranePanaSemina_seminArrival", "Barbora.x.cutscenehandler4", "why=the host's copy runs");
        var ev = Wo194Rules.ParsePollEvents(b.ToArray());
        Assert.Equal(2, ev.Count);
        Assert.Equal(3, ev[0].Kind); Assert.Equal("Barbora.x.cutscenehandler4", ev[0].Path);
        Assert.Equal(9u, ev[1].Seq); Assert.Equal("why=the host's copy runs", ev[1].Detail);
        Assert.Empty(Wo194Rules.ParsePollEvents([1, 0, 0, 0, 1, 7, 0]));   // cut short: nothing half-read
    }
}
