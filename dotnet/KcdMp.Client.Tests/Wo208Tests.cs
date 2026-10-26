// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-208: the switches and the DLL's switch word.</summary>
public class Wo208Tests
{
    [Fact]
    public void Every_switch_ships_on_and_maps_to_its_native_bit()
    {
        Assert.Equal(15u, Wo208Rules.ConfigBits(_ => true));
        Assert.Equal(8u, Wo208Rules.ConfigBits(n => n == "copy_anim_owner"));
        Assert.True(Wo208Rules.Default("defence_local", null, null));
        Assert.False(Wo208Rules.Default("defence_local", "defence_local", null));                 // a twin
        Assert.False(Wo208Rules.Default("copy_anim_owner", null, null));                          // starts off
        Assert.True(Wo208Rules.Default("copy_anim_owner", null, "copy_anim_owner"));             // the bench turns it on
        Assert.Equal(4u, Wo208Rules.ConfigBits(n => n == "defence_local"));
        Assert.Equal(0u, Wo208Rules.ConfigBits(_ => false));
        Assert.Equal(1u, Wo208Rules.ConfigBits(n => n == "conv_cam_player_view"));
        Assert.Equal(2u, Wo208Rules.ConfigBits(n => n == "target_in_fight"));
        Assert.Equal(130, Protocol.Wo208OpConfig);
        Assert.Equal(131, Protocol.Wo208OpStatus);
    }

    [Theory]
    [InlineData("conv_cam_player_view=off", true, "conv_cam_player_view", false)]
    [InlineData("target_in_fight=on", true, "target_in_fight", true)]
    [InlineData("target_in_fight=maybe", false, "", false)]
    [InlineData("unknown_switch=on", false, "", false)]
    [InlineData("=on", false, "", false)]
    [InlineData(null, false, "", false)]
    public void The_mods_cfg_event_parses(string? arg, bool ok, string name, bool on)
    {
        Assert.Equal(ok, Wo208Rules.TryParseCfg(arg, out string n, out bool v));
        if (ok) { Assert.Equal(name, n); Assert.Equal(on, v); }
    }

    // ---- B: the joiner's own defence

    [Fact]
    public void The_defence_outcome_text_round_trips()
    {
        string t = Wo208Rules.DefenceText("tkop_vrahBandit_1", Wo208Rules.DefPerfect, 0f, 12.5f, 7);
        Assert.True(Wo208Rules.TryParseDefence(t, out string npc, out byte o, out float hp, out float st, out uint seq));
        Assert.Equal("tkop_vrahBandit_1", npc); Assert.Equal(Wo208Rules.DefPerfect, o); Assert.Equal(0f, hp); Assert.Equal(12.5f, st); Assert.Equal(7u, seq);
        Assert.Equal("perfect-block", Wo208Rules.DefName(o));
    }

    [Theory]
    [InlineData("v1|a b|1|0|0|1")]          // a name that is no entity name
    [InlineData("v1|npc|9|0|0|1")]          // no such outcome
    [InlineData("v1|npc|1|-1|0|1")]         // negative damage
    [InlineData("v2|npc|1|0|0|1")]
    [InlineData("v1|npc|1|0|0")]
    [InlineData(null)]
    public void A_malformed_outcome_is_refused(string? t) => Assert.False(Wo208Rules.TryParseDefence(t, out _, out _, out _, out _, out _));

    [Fact]
    public void The_host_holds_at_most_450_ms_and_matches_an_outcome_to_its_blow()
    {
        Assert.Equal(450, Wo208Rules.HoldMs(438, 20));
        Assert.Equal(300, Wo208Rules.HoldMs(280, 20));
        Assert.True(Wo208Rules.Answers(arrivedMs: 10_040, blowMs: 10_000, holdMs: 450));    // the joiner's copy swung 40 ms later
        Assert.True(Wo208Rules.Answers(arrivedMs: 9_500, blowMs: 10_000, holdMs: 450));     // the host's own frame was late
        Assert.False(Wo208Rules.Answers(arrivedMs: 10_500, blowMs: 10_000, holdMs: 450));   // too late: the host's verdict went
        Assert.False(Wo208Rules.Answers(arrivedMs: 8_000, blowMs: 10_000, holdMs: 450));    // an older blow's
    }

    [Fact]
    public void The_joiner_applies_no_host_verdict_on_a_blow_its_own_engine_judged()
    {
        Assert.True(Wo208Rules.JudgedHere(judgedMs: 10_000, nowMs: 10_600, on: true));
        Assert.False(Wo208Rules.JudgedHere(judgedMs: 10_000, nowMs: 12_000, on: true));
        Assert.False(Wo208Rules.JudgedHere(judgedMs: 0, nowMs: 10_000, on: true));
        // twin: off, every host verdict is applied (the host judges again)
        Assert.False(Wo208Rules.JudgedHere(judgedMs: 10_000, nowMs: 10_100, on: false));
    }

    // ---- D.2 the joiner's player-watching area triggers

    [Fact]
    public void A_player_watching_area_trigger_is_quiet_on_the_joiner_but_a_tutorial_stays_his_own()
    {
        var idx = new QuestValueIndex();
        idx.AddTriggers("Quests/Final/Barbora/trosecko/kumaniNaTrosecku/rvacka_v_hospode/approach.xml",
            "<AreaTrigger Name=\"areatrigger3\"><Asset Name=\"Souls\" Alias=\"player\" /><Asset Name=\"Areas\" Alias=\"tavernTeleportationArea\" /></AreaTrigger>" +
            "<AreaTrigger Name=\"smallAreaTrigger\"><Asset Name=\"Souls\" Alias=\"player\" /></AreaTrigger>");
        idx.AddTriggers("Quests/Final/Barbora/open_world/tutorialy/tutorial_x.xml",
            "<AreaTrigger Name=\"tutArea\"><Asset Name=\"Souls\" Alias=\"player\" /></AreaTrigger>");
        idx.AddTriggers("Quests/Final/Barbora/trosecko/kovar.xml",
            "<SoulAsset Name=\"kovar\" SharedSoulGuids=\"y\" /><AreaTrigger Name=\"areatrigger55\"><Asset Name=\"Souls\" Alias=\"kovar\" /></AreaTrigger>");
        Assert.Equal(new[] { "Barbora.trosecko.kumaniNaTrosecku.rvacka_v_hospode.approach.areatrigger3",
                             "Barbora.trosecko.kumaniNaTrosecku.rvacka_v_hospode.approach.smallAreaTrigger" }, idx.PlayerTriggerPaths());
        // twin: the NPC list alone (WO-207 D.2) never names a player-watching trigger
        Assert.Equal(new[] { "Barbora.trosecko.kovar.areatrigger55" }, idx.NpcTriggerPaths());
    }
    // ---- D.2 the joiner's quest queue: a sleeping node's change waits aside

    private static QuestChange QC(uint seq, string path) => new(seq, 0, 0, 1, "SetX", "", path, 0);

    private static async Task<List<uint>> DrainOnce(bool aside, params QuestChange[] queued)
    {
        var q = new QuestApplyQueue();
        foreach (var c in queued) q.Enqueue(c);
        var applied = new List<uint>();
        await Wo208Rules.DrainQuestTickAsync(q, c =>
        {
            if (c.Path.EndsWith("sleeps", StringComparison.Ordinal)) return Task.FromResult(Wo208Rules.QuestStep.NodeWaits);
            applied.Add(c.Seq);
            return Task.FromResult(Wo208Rules.QuestStep.Done);
        }, () => true, aside, 64);
        return applied;
    }

    [Fact]
    public async Task A_sleeping_node_no_longer_holds_the_host_s_later_quest_changes()
    {
        // the tavern run: #13 trackingPlayerNearPub slept here, #14.. waited 30 s behind it
        var applied = await DrainOnce(true, QC(13, "q.approach.sleeps"), QC(14, "q.fight"), QC(15, "q.approach.sleeps"), QC(16, "q.side"));
        Assert.Equal(new uint[] { 14, 16 }, applied);   // in the host's order; #15 (the sleeping node's own later step) stays behind #13
        // twin (quest_wait_aside off): the first change that waits holds every later one
        Assert.Empty(await DrainOnce(false, QC(13, "q.approach.sleeps"), QC(14, "q.fight"), QC(16, "q.side")));
    }

    [Fact]
    public async Task No_answer_from_the_dll_stops_the_tick_and_the_sleeping_change_stays_first()
    {
        var q = new QuestApplyQueue();
        q.Enqueue(QC(1, "a.sleeps")); q.Enqueue(QC(2, "b")); q.Enqueue(QC(3, "c"));
        int calls = 0;
        await Wo208Rules.DrainQuestTickAsync(q, c => { calls++; return Task.FromResult(c.Seq == 1 ? Wo208Rules.QuestStep.NodeWaits : Wo208Rules.QuestStep.NoAnswer); },
            () => true, true, 64);
        Assert.Equal(2, calls);                        // #1 aside, #2 no answer: stop (#3 is not tried)
        Assert.True(q.TryPeek(out var head)); Assert.Equal(1u, head.Seq);
        Assert.Equal(3, q.Count);
        Assert.Equal(30, Wo208Rules.QuestAsleepTries(true));   // once a second aside: the same 30 s as 600 ticks of 50 ms in the head
        Assert.Equal(600, Wo208Rules.QuestAsleepTries(false));
    }
    // ---- B.4 the host's NPC recoils when the joiner's engine blocked its stab

    [Fact]
    public void The_host_npc_recoils_only_for_a_stab_the_joiner_blocked_that_the_host_had_as_a_hit()
    {
        Assert.True(Wo208Rules.HostRecoils(true, Wo208Rules.DefBlocked, Wo208Rules.StabZone, HitVerdict.Hit));
        Assert.True(Wo208Rules.HostRecoils(true, Wo208Rules.DefPerfect, Wo208Rules.StabZone, HitVerdict.Hit));
        Assert.False(Wo208Rules.HostRecoils(true, Wo208Rules.DefBlocked, 2, HitVerdict.Hit));                       // a blocked slash runs on (the game's rows)
        Assert.False(Wo208Rules.HostRecoils(true, Wo208Rules.DefBlocked, Wo208Rules.StabZone, HitVerdict.Blocked));  // the host's engine blocked it: its own recoil ran
        Assert.False(Wo208Rules.HostRecoils(true, Wo208Rules.DefHit, Wo208Rules.StabZone, HitVerdict.Hit));
        Assert.False(Wo208Rules.HostRecoils(true, Wo208Rules.DefMissed, Wo208Rules.StabZone, HitVerdict.Hit));
        // twin (defence_recoil off): the host's NPC runs on through a stab the joiner perfectly blocked
        Assert.False(Wo208Rules.HostRecoils(false, Wo208Rules.DefPerfect, Wo208Rules.StabZone, HitVerdict.Hit));
        Assert.Contains("defence_recoil", Wo208Rules.SwitchNames);
    }
    // ---- D.2 the joiner's clock stands with the host's

    [Fact]
    public void No_clock_correction_runs_a_clock_that_stands_with_the_hosts()
    {
        Assert.False(Wo208Rules.ClockSlewAllowed(standingWithHost: true, on: true));   // the host's dialogue: ratio 0 stays
        Assert.True(Wo208Rules.ClockSlewAllowed(standingWithHost: false, on: true));   // running: the gentle correction as before
        // twin (clock_stands_with_host off): the correction writes a running ratio over the stand (the baseline's +917 game s)
        Assert.True(Wo208Rules.ClockSlewAllowed(standingWithHost: true, on: false));
        Assert.Contains("clock_stands_with_host", Wo208Rules.SwitchNames);
    }

    [Fact]
    public void A_conversation_that_starts_by_itself_is_marked_and_a_player_started_one_is_not()
    {
        const string forced = "New dialogue 'Barbora.trosecko.zbranePanaSemina.h.na_semine.dialogy_se_sukem.force_otazky (normal - Id: 792) (ForceDialogNode)' is starting. Params: souls = 'Ex: Dude; Ex: tsem_suk' forced = false";
        const string own = "New dialogue 'Barbora.trosecko.zbranePanaSemina.h.na_semine.se_sukem_po_preruseni_cs (normal - Id: 25) (Actor::RequestDialog)' is starting. Params: souls = 'Ex: Dude; Ex: tsem_suk' forced = false";
        Assert.True(Wo204Rules.ParseDialogueHead(forced) is { } f && Wo208Rules.StartsByItself(f.Origin));
        Assert.True(Wo204Rules.ParseDialogueHead(own) is { } o && !Wo208Rules.StartsByItself(o.Origin));
        Assert.True(Wo208Rules.StartsByItself("AI::RequestDialog"));   // an NPC opening a full dialogue with the player
        string marked = Wo203CamRules.AnnounceText(792, "normal", true, "tsem_suk", ["tsem_suk"], startsByItself: true);
        string plain = Wo203CamRules.AnnounceText(25, "normal", true, "tsem_suk", ["tsem_suk"]);
        Assert.True(Wo203CamRules.AnnounceStartsByItself(marked));
        Assert.False(Wo203CamRules.AnnounceStartsByItself(plain));
        // the mark rides WO-203's unused fifth field: the announce still parses as before
        Assert.True(Wo203CamRules.TryParseAnnounce(marked, out int cid, out string kind, out bool joinable, out string anchor));
        Assert.Equal((792, "normal", true, "tsem_suk"), (cid, kind, joinable, anchor));
        Assert.Equal("v1|25|normal|1|-|tsem_suk|tsem_suk|0,0,0|-", plain);
        Assert.Contains("conv_autojoin", Wo208Rules.SwitchNames);
        Assert.True(Wo208Rules.Default("conv_autojoin", null, null));
        Assert.False(Wo208Rules.Default("conv_autojoin", "conv_autojoin", null));   // its twin
        Assert.Equal("v1|792|1", Wo203CamRules.JoinText(792, true));
    }

    [Fact]
    public void A_window_with_nothing_landed_is_told_as_none_and_is_never_a_judgement()
    {
        Assert.True(Wo208Rules.TryParseDefence(Wo208Rules.DefenceText("tkop_vrahBandit_5", Wo208Rules.DefNone, 0, 0, 7), out string npc, out byte o, out _, out _, out uint seq));
        Assert.Equal(("tkop_vrahBandit_5", Wo208Rules.DefNone, 7u), (npc, o, seq));
        Assert.Equal("none", Wo208Rules.DefName(Wo208Rules.DefNone));
        Assert.False(Wo208Rules.IsJudgement(Wo208Rules.DefNone));   // the host's verdict goes at once; the joiner applies it as before
        foreach (byte j in new[] { Wo208Rules.DefHit, Wo208Rules.DefBlocked, Wo208Rules.DefPerfect, Wo208Rules.DefBroken, Wo208Rules.DefMissed })
            Assert.True(Wo208Rules.IsJudgement(j));
        Assert.False(Wo208Rules.TryParseDefence("v1|tkop_vrahBandit_5|7|0.00|0.00|8", out _, out _, out _, out _, out _));
        // without it (d11/d12) every blow but about one a fight waited the whole hold: the cap
        Assert.Equal(Wo208Rules.HoldCapMs, Wo208Rules.HoldMs(Wo208Rules.HoldCapMs, 3));
    }
}
