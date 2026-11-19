// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

public class Wo222Tests
{
    [Fact]
    public void Hold_is_the_unknown_wait_until_five_answers_are_known()
    {
        Assert.Equal(Wo222Rules.DefHoldUnknownMs, Wo222Rules.DefHoldMs(new[] { 100, 120, 140, 160 }));
        Assert.Equal(Wo222Rules.DefHoldUnknownMs, Wo222Rules.DefHoldMs(Array.Empty<int>()));
    }

    [Fact]
    public void Hold_follows_the_slowest_answers_within_floor_and_cap()
    {
        // fast answers: the floor (WO-208's 450 ms)
        Assert.Equal(Wo222Rules.DefHoldFloorMs, Wo222Rules.DefHoldMs(Enumerable.Repeat(120, 40).ToArray()));
        // the field's link: answers 300..612 ms -> the slowest + margin
        var field = Enumerable.Range(0, 40).Select(i => 300 + i * 8).ToArray();   // 300..612
        Assert.Equal(612 + Wo222Rules.DefMarginMs, Wo222Rules.DefHoldMs(field));
        // the bench's defence scene: late answers ~1.05-1.08 s after the blow join the sample -> the next wait covers them
        Assert.True(Wo222Rules.DefHoldMs(field.Append(1078).ToArray()) >= 1078 + Wo222Rules.DefMarginMs);
        // a terrible link: capped (2 s holds made every blow the joiner's copy never swung at him wait 2 s for nothing)
        Assert.Equal(Wo222Rules.DefHoldCapMs, Wo222Rules.DefHoldMs(Enumerable.Repeat(3000, 40).ToArray()));
        // a combo's hold is a floor
        Assert.Equal(Wo222Rules.DefHoldFloorMs, Wo222Rules.DefHoldMs(Enumerable.Repeat(120, 40).ToArray(), comboMs: 650));
        Assert.Equal(1900, Wo222Rules.DefHoldMs(Enumerable.Repeat(120, 40).ToArray(), comboMs: 1900));
    }

    [Fact]
    public void Delay_samples_are_answers_to_their_blow_only()
    {
        Assert.True(Wo222Rules.DelaySample(1300, 1000));
        Assert.False(Wo222Rules.DelaySample(900, 1000));                              // came before the blow: an earlier blow's
        Assert.False(Wo222Rules.DelaySample(1000 + Wo222Rules.DefStallCapMs + 1, 1000));
    }

    [Fact]
    public void Ring_keeps_the_last_answers()
    {
        var r = new Wo222Rules.DelayRing(3);
        foreach (int v in new[] { 1, 2, 3, 4, 5 }) r.Add(v);
        Assert.Equal(new[] { 3, 4, 5 }, r.Snapshot().OrderBy(x => x).ToArray());
    }

    [Fact]
    public void A_guess_is_overruled_by_the_joiners_own_judgement_of_the_same_attacker_soon_after()
    {
        var list = new List<Wo222Rules.Applied>
        {
            new("bandit_1", 7, 12f, 5f, 1000, (byte)HitVerdict.Hit),
            new("bandit_2", 8, 9f, 3f, 1100, (byte)HitVerdict.Hit),
        };
        var a = Wo222Rules.Overruled(list, "bandit_1", 1500);
        Assert.NotNull(a);
        Assert.Equal(7u, a!.HitId);
        a.Settled = true;
        Assert.Null(Wo222Rules.Overruled(list, "bandit_1", 1600));                  // settled: once
        Assert.Null(Wo222Rules.Overruled(list, "bandit_2", 1100 + Wo222Rules.CorrectWithinMs + 1));   // too late: another blow
        Assert.Null(Wo222Rules.Overruled(list, "bandit_3", 1200));
    }

    [Fact]
    public void Contradiction_is_a_different_verdict()
    {
        Assert.True(Wo222Rules.Contradicts(Wo208Rules.DefBlocked, HitVerdict.Hit));
        Assert.True(Wo222Rules.Contradicts(Wo208Rules.DefPerfect, HitVerdict.Hit));
        Assert.True(Wo222Rules.Contradicts(Wo208Rules.DefMissed, HitVerdict.Hit));
        Assert.False(Wo222Rules.Contradicts(Wo208Rules.DefHit, HitVerdict.Hit));
        Assert.False(Wo222Rules.Contradicts(Wo208Rules.DefBlocked, HitVerdict.Blocked));
        Assert.True(Wo222Rules.Contradicts(Wo208Rules.DefHit, HitVerdict.Blocked));
    }

    [Fact]
    public void A_runner_stays_watched_two_minutes_after_its_fight()
    {
        Assert.True(Wo222Rules.KeepWatching(1000, 1000 + 60_000));
        Assert.True(Wo222Rules.KeepWatching(1000, 1000 + Wo222Rules.FleeWatchKeepMs));
        Assert.False(Wo222Rules.KeepWatching(1000, 1000 + Wo222Rules.FleeWatchKeepMs + 1));
        Assert.False(Wo222Rules.KeepWatching(5000, 1000));
    }

    [Fact]
    public void Switches_twin_and_native_bits()
    {
        Assert.True(W222Switch.TwinOff("all", "flee_live"));
        Assert.True(W222Switch.TwinOff("react_show,def_wait", "def_wait"));
        Assert.False(W222Switch.TwinOff("react_show", "def_wait"));
        Assert.False(W222Switch.TwinOff("all", "swing_log"));
        Assert.Equal(0x1Fu, W222Switch.NativeBits(_ => true));
        Assert.Equal(0x04u, W222Switch.NativeBits(n => n == "flee_live"));
    }

    [Fact]
    public void Fleeing_rides_the_npc_combat_event_and_round_trips()
    {
        var s = new NpcCombatState(7, false, -1, -1, -1, false, false, 0, "tapo_kasparBandit_4", true, Fleeing: true);
        var ev = Wo132Rules.ToEvent(s, 123, _ => null);
        Assert.True(ev.Fleeing);
        Assert.False(ev.InCombat);
        Assert.True(NpcCombatEvent.TryFromBytes(ev.ToBytes(), out var back));
        Assert.True(back.Fleeing);
        // a fighting body is never "fleeing"
        var f = new NpcCombatState(7, true, -1, -1, -1, false, true, 0, "tapo_kasparBandit_4", true, Fleeing: true);
        Assert.False(Wo132Rules.ToEvent(f, 1, _ => null).Fleeing);
        // the DLL's frame: combat byte 2
        var b = new byte[15 + 4];
        b[4] = 2; b[14] = 4; "abcd"u8.CopyTo(b.AsSpan(15));
        Assert.True(NpcCombatState.TryParse(b, out var p));
        Assert.True(p.Fleeing);
        Assert.False(p.Combat);
    }
}
