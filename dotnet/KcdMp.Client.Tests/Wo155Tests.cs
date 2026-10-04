// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-155: hits never knock a player down; the figure falls only on death (docs/WO-155-findings.md).</summary>
public class Wo155Tests
{
    [Fact]
    public void A_down_edge_after_my_own_friendly_fire_hit_is_its_knockdown()
    {
        Assert.True(Wo155Rules.FriendlyKnock(sentAtMs: 10_000, nowMs: 10_400));    // the field: 0.25-0.4 s
        Assert.True(Wo155Rules.FriendlyKnock(10_000, 13_000));                      // the window's end
        Assert.False(Wo155Rules.FriendlyKnock(10_000, 13_001));                     // later: another cause
    }

    [Fact]
    public void A_down_edge_with_no_hit_of_mine_is_not_a_friendly_knockdown()
    {
        Assert.False(Wo155Rules.FriendlyKnock(0, 5_000));       // never sent him one (a knockout, a guard's blow)
        Assert.False(Wo155Rules.FriendlyKnock(9_000, 5_000));   // a hit "from the future" is no cause
    }

    [Fact]
    public void The_no_knockdown_pipe_bit_is_added_only_when_the_switch_is_off_and_never_leaks_into_the_wire_flags()
    {
        Assert.Equal((byte)0x01, Wo155Rules.PvpFlags(0x01, ffKnockdown: true));
        Assert.Equal((byte)0x81, Wo155Rules.PvpFlags(0x01, ffKnockdown: false));
        Assert.Equal((byte)0x03, Wo155Rules.PvpFlags(0x83, ffKnockdown: true));    // a stray high bit from the wire is cleared
        Assert.Equal((byte)0x80, Wo155Rules.PvpNoKnockdownBit);
        Assert.Equal(0, PlayerHitV8.FlagUnarmed & Wo155Rules.PvpNoKnockdownBit);
        Assert.Equal(0, PlayerHitV8.FlagMissile & Wo155Rules.PvpNoKnockdownBit);
    }

    [Theory]
    [InlineData(false, false, Wo155Rules.Body.Up)]
    [InlineData(false, true, Wo155Rules.Body.Up)]
    [InlineData(true, false, Wo155Rules.Body.Death)]      // down, and not a knockout: a death (or an execution)
    [InlineData(true, true, Wo155Rules.Body.Knocked)]     // the game's own knockout, or the death guard's knockdown
    public void The_vitals_flags_tell_a_death_from_a_knockout(bool down, bool knockedDown, Wo155Rules.Body expected) =>
        Assert.Equal(expected, Wo155Rules.BodyOf(down, knockedDown));

    [Fact]
    public void A_respawn_is_a_death_in_the_vitals_followed_by_up_or_ten_seconds_of_lying()
    {
        Assert.True(Wo155Rules.RespawnSeen(vitalsHadDeath: true, collapsedAtMs: 100_000, nowMs: 100_500));   // the normal edge
        Assert.False(Wo155Rules.RespawnSeen(false, 100_000, 102_000));    // a stale "alive" heartbeat right after the death packet
        Assert.False(Wo155Rules.RespawnSeen(false, 100_000, 109_999));
        Assert.True(Wo155Rules.RespawnSeen(false, 100_000, 110_000));     // the vitals never said down: ten seconds are enough
    }

    [Fact]
    public void The_replacement_waits_for_the_new_position_to_stream_in()
    {
        Assert.Equal(1500, Wo155Rules.RespawnReplaceMs);
        Assert.Equal(3000, Wo155Rules.FriendlyKnockWindowMs);
    }
}
