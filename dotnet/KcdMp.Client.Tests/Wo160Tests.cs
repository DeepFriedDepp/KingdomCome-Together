// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
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
}
