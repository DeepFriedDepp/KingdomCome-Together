// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-160: the 2026-10-07 tester findings (agent half). Evidence: docs/WO-160-findings.md.</summary>
public class Wo160Tests
{
    // ------------------------------------------------------------ 2 the false "You loaded your own save"

    [Fact]
    public void A_verdict_needs_the_mods_own_recent_world_answer()
    {
        // never confirmed (a stale agent state, a game that does not answer): silent
        Assert.False(Wo160Rules.SeparateVerdictAllowed(-1, joinBusy: false, msSinceJoinActivity: -1));
        // confirmed a moment ago, no join: spoken (the real own-save case, checklist item 40)
        Assert.True(Wo160Rules.SeparateVerdictAllowed(0, joinBusy: false, msSinceJoinActivity: -1));
        Assert.True(Wo160Rules.SeparateVerdictAllowed(Wo160Rules.WhereFreshMs, joinBusy: false, msSinceJoinActivity: -1));
        // the proof ages out: a game that crashed after its last "world" answer stops being believed within 12 s
        Assert.False(Wo160Rules.SeparateVerdictAllowed(Wo160Rules.WhereFreshMs + 1, joinBusy: false, msSinceJoinActivity: -1));
    }

    [Fact]
    public void A_join_in_flight_keeps_the_verdict_silent()
    {
        Assert.False(Wo160Rules.SeparateVerdictAllowed(0, joinBusy: true, msSinceJoinActivity: 0));
    }

    [Theory]
    [InlineData(0L, false)]
    [InlineData(1_000L, false)]
    [InlineData(27_970L, false)]      // the field: the toast came ~20 s before the join's load even started
    [InlineData(59_999L, false)]
    [InlineData(60_000L, true)]
    [InlineData(-1L, true)]           // no join since this agent began
    public void A_verdict_waits_60_s_after_a_joins_activity(long msSinceJoinActivity, bool allowed)
        => Assert.Equal(allowed, Wo160Rules.SeparateVerdictAllowed(0, joinBusy: false, msSinceJoinActivity));

    [Theory]
    [InlineData(-1L, false)]
    [InlineData(0L, true)]
    [InlineData(12_000L, true)]
    [InlineData(12_001L, false)]
    public void The_needs_menu_line_needs_the_same_proof(long msSinceWorldConfirmed, bool allowed)
        => Assert.Equal(allowed, Wo160Rules.NeedsMenuToldAllowed(msSinceWorldConfirmed));

    [Theory]
    [InlineData(-1L, 3_000L, true)]       // never confirmed, asked a while ago: ask
    [InlineData(-1L, 2_999L, false)]      // ... but never twice within 3 s
    [InlineData(1_000L, 10_000L, false)]  // a fresh proof: no need
    [InlineData(5_000L, 3_000L, true)]    // the proof is aging: refresh
    [InlineData(5_000L, 100L, false)]
    public void The_mod_is_asked_where_the_game_is_when_the_proof_is_missing_or_aging(long confirmedAge, long askedAge, bool due)
        => Assert.Equal(due, Wo160Rules.WhereRefreshDue(confirmedAge, askedAge));

    // ------------------------------------------------------------ 1 the joiner's NPC copies

    [Theory]
    [InlineData(false, true, true, true)]     // a placed copy, a new activity: released first
    [InlineData(false, true, false, false)]   // the host's 10 s refresh of the same activity: nothing at all
    [InlineData(false, false, true, false)]   // the first placement: the puppet start's own release (WO-118) ran
    [InlineData(true, true, true, false)]     // an avatar is never in the game's NPC schedule
    public void A_placed_copy_is_released_before_a_new_activity_only(bool avatar, bool placedBefore, bool changed, bool release)
        => Assert.Equal(release, Wo160Rules.ReleaseBeforeApply(avatar, placedBefore, changed));

    [Fact]
    public void The_planners_refusal_and_the_unreached_load_are_read_per_npc_from_the_field_lines()
    {
        // the exact lines of the 2026-10-07 joiner log
        string refused = "[Error] /ai//(NPC)ttkc_woman_12/NPC Context [NPCContext]:Couldn't find actions to get NPC into game loaded state";
        string unreached = "[Error] /ai//(NPC)ttkc_woman_15/NPC Context [NPCContext]:Execution of 1 actions from load counldn't reach the loaded state in 3 updates";
        Assert.Equal("ttkc_woman_12", Wo160Rules.PlannerErrorNpc(refused, out bool r1));
        Assert.True(r1);
        Assert.Equal("ttkc_woman_15", Wo160Rules.PlannerErrorNpc(unreached, out bool r2));
        Assert.False(r2);
        // the other context lines are not placement failures
        Assert.Null(Wo160Rules.PlannerErrorNpc("[Error] /ai//(NPC)ttkc_woman_12/NPC Context [NPCContext]:Current state 'RightHand Held object: x'", out _));
        Assert.Null(Wo160Rules.PlannerErrorNpc("[Error] /ai//(NPC)ttkc_man_20/NPC Context [NPCContext]:Execution of action PickUpRight has failed! Action for request 'Game loaded state change'", out _));
        Assert.Null(Wo160Rules.PlannerErrorNpc("[Error] [NPCStateSearch]:NPC state search failed: can't find a path from actions.", out _));
        // an animal's scheduler path is a longer name with slashes: not a bare NPC name
        Assert.Null(Wo160Rules.PlannerErrorNpc("[Error] /ai//(NPC)SpawnedAnimal_CattleCow_1/(Base-SUBB)x/NPC Context [NPCContext]:Couldn't find actions to get NPC into game loaded state", out _));
    }

    // ------------------------------------------------------------ 5 the herb proxy

    [Theory]
    [InlineData((byte)4, true, true)]     // herb gathering, the switch on: the plain clip
    [InlineData((byte)4, false, false)]   // the switch off: the avatar stands
    [InlineData((byte)1, true, false)]    // the grindstone: the DLL's own fragment, as before
    [InlineData((byte)255, true, false)]  // no minigame
    public void The_herb_proxy_is_for_the_herb_minigame_only_and_obeys_the_switch(byte minigame, bool on, bool wanted)
        => Assert.Equal(wanted, Wo160Rules.HerbProxyWanted(minigame, Wo143Rules.HerbMinigame, on));

    [Fact]
    public void No_herb_state_ever_selects_the_engines_own_fragment()
    {
        foreach (bool on in new[] { true, false })
            Assert.Null(Wo143Rules.AvatarShow(new ActivityState(0, 0, 0UL, ActivityState.NoUnstance, 0UL, Wo143Rules.HerbMinigame, 0UL, 0), on));
    }

    // ------------------------------------------------------------ 3 a conversation stands the NPC still on the other machine

    [Fact]
    public void The_host_conversation_kind_is_appended_after_the_five_that_shipped()
    {
        Assert.Equal((byte)1, Protocol.QuestHostChange);
        Assert.Equal((byte)2, Protocol.QuestHostResult);
        Assert.Equal((byte)3, Protocol.QuestHostCheckpoint);
        Assert.Equal((byte)4, Protocol.QuestHostHold);
        Assert.Equal((byte)5, Protocol.QuestHostMode);
        Assert.Equal((byte)6, Protocol.QuestHostConverse);
        Assert.Equal("converse", Protocol.QuestHostName(Protocol.QuestHostConverse));
    }

    [Theory]
    [InlineData(true, "ttkc_man_11")]
    [InlineData(false, "ttac_blacksmith")]
    public void A_conversation_text_is_the_talk_text_and_survives_the_peers_checks(bool on, string npc)
    {
        string text = Wo137Rules.TalkText(on, npc);
        Assert.True(Wo137Rules.TryParseTalkText(text, out bool o2, out string n2));
        Assert.Equal((on, npc), (o2, n2));
        // a hostile name never reaches the mod's Lua
        Assert.False(Wo137Rules.TryParseTalkText("on x; quit", out _, out _));
        Assert.False(Wo137Rules.TryParseTalkText("on ttkc\"man", out _, out _));
    }
}
