// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-213: the agent's pure rules (E.4's line plan, D.2's bands, G's poor link), each check with the case that must not pass.</summary>
public class Wo213Tests
{
    [Fact]
    public void A_line_late_by_the_picks_hold_keeps_its_voice_and_shows_what_is_left()
    {
        // the field: the pick held the first line ~770 ms; the line is 2.4 s long, the link's line latency ~120 ms
        var p = W213Rules.LinePlan(ageMs: 820, durMs: 2400, latencyMs: 120, prevLeftMs: 0);
        Assert.False(p.Over);
        Assert.Equal(0, p.WaitMs);
        Assert.InRange(p.ShowMs, 1600, 1800);
        // the twin: a line past its own end (latency + length) is a subtitle only
        Assert.True(W213Rules.LinePlan(ageMs: 2600, durMs: 2400, latencyMs: 120, prevLeftMs: 0).Over);
    }

    [Fact]
    public void The_first_line_after_a_pick_is_never_a_subtitle_alone()
    {
        // a link late by more than the line's own length: any other line is a subtitle only (the twin), the pick's answer is voiced in full
        Assert.True(W213Rules.LinePlan(ageMs: 2600, durMs: 2400, latencyMs: 120, prevLeftMs: 0).Over);
        var p = W213Rules.LinePlan(ageMs: 2600, durMs: 2400, latencyMs: 120, prevLeftMs: 0, firstAfterPick: true);
        Assert.False(p.Over);
        Assert.Equal(2400, p.ShowMs);
    }

    [Fact]
    public void A_line_waits_for_the_line_before_it_while_that_still_leaves_it_a_second()
    {
        var p = W213Rules.LinePlan(ageMs: 100, durMs: 3000, latencyMs: 120, prevLeftMs: 700);
        Assert.Equal(700, p.WaitMs);
        Assert.True(p.ShowMs >= 1000);
        var shortLine = W213Rules.LinePlan(ageMs: 100, durMs: 1200, latencyMs: 120, prevLeftMs: 700);
        Assert.Equal(100, shortLine.WaitMs);   // only what still leaves it its second
    }

    [Fact]
    public void Line_latency_is_the_p90_of_recent_ages_and_300_until_known()
    {
        Assert.Equal(300, W213Rules.LineLatencyMs([50, 60]));
        Assert.InRange(W213Rules.LineLatencyMs([80, 90, 100, 110, 120, 130, 140, 150, 160, 900]), 150, 170);
    }

    [Fact]
    public void Bands_by_distance_and_fight_and_their_override()
    {
        Assert.Equal(0, W213Rules.Band(10, drawn: true));
        Assert.Equal(1, W213Rules.Band(10, drawn: false));
        Assert.Equal(2, W213Rules.Band(100, drawn: true));
        Assert.Equal(3, W213Rules.Band(200, drawn: false));
        Assert.Equal(new[] { 15, 40, 150, 33, 50, 100, 500 }, W213Rules.Bands(null));
        Assert.Equal(new[] { 20, 50, 200, 25, 40, 80, 1000 }, W213Rules.Bands("20:25,50:40,200:80,0:1000"));
        Assert.Equal(new[] { 15, 40, 150, 33, 50, 100, 500 }, W213Rules.Bands("20:5,50:40,200:80,0:1000"));   // a period under 10 ms: refused
    }

    [Fact]
    public void An_NPC_with_a_combat_opponent_fights_whatever_it_holds()
    {
        Assert.True(W213Rules.Fighting(false, oppKind: 2, oppAgeMs: 300));    // the partner's figure is its opponent
        Assert.True(W213Rules.Fighting(true, 0, long.MaxValue));             // drawn
        Assert.False(W213Rules.Fighting(false, 2, 9000));                     // the opponent word is old
        Assert.False(W213Rules.Fighting(false, 0, 100));                      // "none"
        // the twin (the first rule: the drawn bit alone) put the bench's bandits in the 40 m band
        Assert.False(W213Rules.Fighting(false, 0, long.MaxValue));
    }

    [Fact]
    public void A_poor_link_is_Steams_own_word_not_one_number_in_range()
    {
        Assert.True(W213Rules.SteamPoor(0.6f, 1, 0, 50));
        Assert.True(W213Rules.SteamPoor(1, 1, 100_000, 50));
        Assert.True(W213Rules.SteamPoor(1, 1, 0, 400));
        Assert.False(W213Rules.SteamPoor(0.98f, 0.97f, 2_000, 60));
        Assert.False(W213Rules.SteamPoor(-1, -1, 0, 60));   // not known yet: not poor
    }

    private static NativeNpcSample Sample(byte src, string name, ushort seq) => new(src, name, seq, 0, 0, 0, 0, seq, seq, seq);

    private static List<NativeNpcSample> Backlog()
    {
        // C.1: the pipe was busy -- 40 bodies with 3 samples each, interleaved, and another source's body of a known name
        var l = new List<NativeNpcSample>();
        for (ushort round = 1; round <= 3; round++)
            for (int b = 0; b < 40; b++) l.Add(Sample(0, "npc_" + b, round));
        l.Add(Sample(1, "npc_0", 9));
        return l;
    }

    [Fact]
    public void The_pipe_lane_keeps_each_bodys_two_newest_samples_after_a_backlog()
    {
        var pending = Backlog();
        Assert.Equal(40, NativeNpcFeed.Backlog(pending, latestWins: true));
        Assert.DoesNotContain(pending, s => s.Seq == 1);            // the stale round never reaches the game
        Assert.Equal(81, pending.Count);
        Assert.Equal(9, pending[^1].Seq);                            // another source's body is its own
        Assert.True(pending.Select(s => s.Seq).SequenceEqual(pending.Select(s => s.Seq).Order()));   // arrival order kept
        // no backlog: every sample, as before
        var few = Backlog().Take(NativeNpcFeed.BacklogSamples).ToList();
        Assert.Equal(0, NativeNpcFeed.Backlog(few, latestWins: true));
    }

    [Fact]
    public void Twin_C_off_the_pipe_lane_sends_the_stale_backlog()
    {
        var pending = Backlog();
        Assert.Equal(0, NativeNpcFeed.Backlog(pending, latestWins: false));
        Assert.Equal(40, pending.Count(s => s.Seq == 1));
    }

    [Fact]
    public void A_gap_is_a_body_that_kept_moving_through_the_silence()
    {
        Assert.True(W213Rules.MovedThrough(distM: 2.2, dtS: 1.6));     // walked on at 1.4 m/s while nothing arrived: the link
        Assert.False(W213Rules.MovedThrough(distM: 0.05, dtS: 1.6));   // stopped, then took a step: the sender's change gate
        Assert.False(W213Rules.MovedThrough(distM: 0.0, dtS: 2.0));    // stood: the 2 s heartbeat
        Assert.False(W213Rules.MovedThrough(distM: 90, dtS: 340));     // left the stream (the cull) and came back
        Assert.False(W213Rules.MovedThrough(distM: 60, dtS: 1.5));     // a warp (a clock change put it at its schedule's place)
        Assert.True(W213Rules.MovedThrough(distM: 15, dtS: 1.5));      // a galloping horse is still a moving body
        // the twin: the first rule (any move over 1 cm) counted the stop-and-go walker as a silence of its stream
        static bool FirstRule(double d) => d > 0.01;
        Assert.True(FirstRule(0.05));
    }

    [Fact]
    public void A_walking_figure_goes_out_every_round_of_the_loop()
    {
        // 1.5 m/s and a ~25 ms round: 3.75 cm a round -- the top band's gate sends it, the old 5 cm one (the twin) waits a second round
        Assert.True(W213Rules.FigureChanged(0.0375f, 0, 0, 0, fine: true, 0.05f, 0.02f));
        Assert.False(W213Rules.FigureChanged(0.0375f, 0, 0, 0, fine: false, 0.05f, 0.02f));
        Assert.False(W213Rules.FigureChanged(0.01f, 0.01f, 0, 0.005f, fine: true, 0.05f, 0.02f));   // standing: fidgets stay home
        Assert.True(W213Rules.FigureChanged(0, 0, 0, 0.015f, fine: true, 0.05f, 0.02f));             // a turn
    }

    [Fact]
    public void The_figures_Lua_update_keeps_the_old_pace_but_never_misses_a_state_or_a_mount()
    {
        Assert.False(W213Rules.FigureLuaDue(sinceMs: 22, ridingChanged: false, hasState: false));   // a 45 Hz sample: the DLL has it
        Assert.True(W213Rules.FigureLuaDue(50, false, false));
        Assert.True(W213Rules.FigureLuaDue(5, false, hasState: true));
        Assert.True(W213Rules.FigureLuaDue(5, ridingChanged: true, false));
        // the twin (figure_band off): every sample a Lua call, as the 0.63.9 stream's ~17 a second were
        Assert.Equal(50, W213Rules.FigureLuaEveryMs);
    }

    [Fact]
    public void A_talks_camera_sample_is_told_from_its_lines_and_options()
    {
        // the listener's conv lane gives the camera samples the priority way, the rest stay frames' calls
        int k = 1 + Protocol.JoinHeaderLen;
        var cam = new byte[k + 1]; cam[k] = Protocol.ConvCam;
        var line = new byte[k + 1]; line[k] = Protocol.ConvLine;
        Assert.Equal("conv:" + Protocol.ConvCam, LinkKinds.Of(Protocol.ConvDown, cam));
        Assert.NotEqual("conv:" + Protocol.ConvCam, LinkKinds.Of(Protocol.ConvDown, line));
    }

    [Fact]
    public void The_switches_are_on_unless_the_twin_names_them()
    {
        Assert.False(W213Switch.TwinOff(null, "link_sched"));
        Assert.True(W213Switch.TwinOff("x, link_sched", "link_sched"));
        Assert.True(W213Switch.TwinOff("all", "npc_compact"));
        Assert.False(W213Switch.TwinOff("link_sched_x", "link_sched"));
    }
}
