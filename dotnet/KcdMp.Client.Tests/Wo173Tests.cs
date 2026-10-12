// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-173 (docs/WO-173-findings.md): 0.49.0's agent rules -- the DLL's real-hit answer, the swing a hit belongs to, the outcome and
/// down / up texts (W164 kinds 7 and 8), the down report rule. The native half is native/tests/wo173_tests.cpp; the Lua half
/// tools/Test-WO173Synthetic.lua.
/// </summary>
public class Wo173Tests
{
    [Fact]
    public void Real_hits_default_on_with_the_capped_fallback_per_blow()
    {
        // WO-173 shipped it off (block-rate check not staged); WO-174 D: the maintainer chose on for 0.49.1; WO-176: the maintainer kept it
        // on for 0.50.0 although B1's block-rate check could not be staged (a camp bandit never fought the partner's figure)
        Assert.True(Wo173Rules.DefaultRealHits);
    }

    private static byte[] Answer(byte applied, byte outcome, byte fallback, byte react, byte steps, ulong aw, ulong vw, uint seq, float st, float hp)
    {
        var p = new byte[33];
        p[0] = applied; p[1] = outcome; p[2] = fallback; p[3] = react; p[4] = steps;
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(5), aw);
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(13), vw);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(21), seq);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(25), st);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(29), hp);
        return p;
    }

    [Fact]
    public void The_dlls_answer_decodes_and_applies_exactly_one_way()
    {
        var e = Wo173Hit.TryDecode(true, 0, Answer(1, 2, 0, 1, 6, 0x0500000000000001, 0x0500000000000002, 7, 0, 0))!.Value;
        Assert.True(e.Ok); Assert.True(e.ByEngine); Assert.True(e.AppliedAny);
        Assert.Equal(HitVerdict.Blocked, Wo173Rules.VerdictOf(e.Outcome));
        Assert.Equal("queued", Wo173Rules.ReactName(e.React));
        Assert.Equal(7u, e.Seq);
        var r = Wo173Hit.TryDecode(true, 0, Answer(2, 0, 5, 0, 7, 1, 2, 0, 35f, 8f))!.Value;
        Assert.False(r.ByEngine); Assert.True(r.AppliedAny);
        Assert.Equal("no-swing-window", Wo173Rules.FallbackName(r.Fallback));
        Assert.Equal(35f, r.StApplied);
        // an older DLL: "bad request", no payload -> not applied here, the caller takes WO-121's path (never both)
        var old = Wo173Hit.TryDecode(false, 8, Array.Empty<byte>())!.Value;
        Assert.False(old.Ok); Assert.False(old.AppliedAny);
        var shortOne = Wo173Hit.TryDecode(true, 0, new byte[10])!.Value;
        Assert.False(shortOne.Ok);
    }

    [Fact]
    public void Every_native_outcome_and_fallback_has_a_name()
    {
        Assert.Equal(HitVerdict.Hit, Wo173Rules.VerdictOf(1));
        Assert.Equal(HitVerdict.PerfectBlock, Wo173Rules.VerdictOf(3));
        Assert.Equal(HitVerdict.Broken, Wo173Rules.VerdictOf(4));
        Assert.Equal(HitVerdict.None, Wo173Rules.VerdictOf(5));   // filtered: the raw path applied it
        for (byte f = 0; f <= 10; f++) Assert.False(Wo173Rules.FallbackName(f).StartsWith('f') && Wo173Rules.FallbackName(f).Length <= 3);
        for (byte r = 0; r <= 9; r++) Assert.False(Wo173Rules.ReactName(r).StartsWith('r') && Wo173Rules.ReactName(r).Length <= 3);
    }

    private static ActionRowCatalog.Row Row(int attackType, int zone, float start, float hit) =>
        new("combat_action_attack", "FreeAttack", "punch", zone, 1, attackType, -1, start, hit);

    [Fact]
    public void A_hit_belongs_to_the_last_fresh_swing_of_that_joiner()
    {
        var row = Row(5, 2, 0.25f, 0.4f);
        var f = Wo173Rules.FieldsFor(row, 1_000, 2_000);
        Assert.NotNull(f);
        Assert.Equal((sbyte)5, f!.Value.Type);
        Assert.Equal((sbyte)2, f.Value.Zone);
        Assert.Equal(1.0f, f.Value.Strength);
        Assert.Null(Wo173Rules.FieldsFor(row, 1_000, 1_000 + Wo173Rules.RowFreshMs + 1));   // too old: no swing (fallback no-swing-row)
        Assert.Null(Wo173Rules.FieldsFor(null, 1_000, 1_100));
        Assert.Null(Wo173Rules.FieldsFor(Row(-1, 2, 0, 0), 1_000, 1_100));                   // a defence row carries no attack
    }

    [Fact]
    public void The_switches_parse()
    {
        var kv = Wo173Rules.ParseCfg("real_hits=on threat=off react=on junk=1 real_hits=maybe").ToList();
        Assert.Equal(3, kv.Count);
        Assert.Contains(("real_hits", "on"), kv);
        Assert.Contains(("threat", "off"), kv);
        Assert.Empty(Wo173Rules.ParseCfg(null));
    }

    [Fact]
    public void The_outcome_text_round_trips()
    {
        string t = W164Text.RealHit("tapo_kasparBandit_6", HitVerdict.Blocked, 1, true, 8f, 20f);
        Assert.Equal("tapo_kasparBandit_6 blocked 1 e 8.0 20.0", t);
        Assert.True(t.Length <= Protocol.W164TextMax);
        Assert.True(W164Text.TryParseRealHit(t, out var npc, out var v, out var react, out var byEngine, out var hp, out var st));
        Assert.Equal("tapo_kasparBandit_6", npc); Assert.Equal(HitVerdict.Blocked, v); Assert.Equal(1, react); Assert.True(byEngine);
        Assert.Equal(8f, hp); Assert.Equal(20f, st);
        Assert.True(W164Text.TryParseRealHit(W164Text.RealHit("ttkc_man_3", HitVerdict.None, 0, false, 8f, 20f), out _, out var v2, out _, out var e2, out _, out _));
        Assert.Equal(HitVerdict.None, v2); Assert.False(e2);
        // the longest name allowed still fits the text maximum
        Assert.True(W164Text.RealHit(new string('a', 48), HitVerdict.PerfectBlock, 255, true, 999.9f, 999.9f).Length <= Protocol.W164TextMax);
        Assert.False(W164Text.TryParseRealHit("bad name! hit 1 e 1 1", out _, out _, out _, out _, out _, out _));
        Assert.False(W164Text.TryParseRealHit("npc missed 1 e 1 1", out _, out _, out _, out _, out _, out _));
        Assert.False(W164Text.TryParseRealHit("npc hit 1 x 1 1", out _, out _, out _, out _, out _, out _));
        Assert.False(W164Text.TryParseRealHit("npc hit 1 e -1 1", out _, out _, out _, out _, out _, out _));
        Assert.False(W164Text.TryParseRealHit("npc hit 1 e 1", out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void The_down_text_round_trips()
    {
        Assert.True(W164Text.TryParseNpcDown(W164Text.NpcDown("ttkc_man_5", 1), out var n, out var s));
        Assert.Equal("ttkc_man_5", n); Assert.Equal(1, s);
        Assert.True(W164Text.TryParseNpcDown("ttkc_man_5 0", out _, out var s0)); Assert.Equal(0, s0);
        Assert.True(W164Text.TryParseNpcDown("ttkc_man_5 2", out _, out var s2)); Assert.Equal(2, s2);
        Assert.False(W164Text.TryParseNpcDown("ttkc_man_5 3", out _, out _));
        Assert.False(W164Text.TryParseNpcDown("ttkc man 1", out _, out _));
        Assert.Equal("real-hit", Protocol.W164KindName(Protocol.W164RealHit));
        Assert.Equal("npc-down", Protocol.W164KindName(Protocol.W164NpcDown));
    }

    [Fact]
    public void A_down_state_is_said_on_change_and_a_knockout_repeats()
    {
        var t = new Wo173Rules.DownTrack();
        Assert.True(t.ShouldSend(0, 1_000));                    // first reading
        Assert.False(t.ShouldSend(0, 1_200));
        Assert.True(t.ShouldSend(1, 1_300));                    // knocked out: at once
        Assert.False(t.ShouldSend(1, 1_400));
        Assert.True(t.ShouldSend(1, 1_300 + Wo173Rules.DownRepeatMs));   // still out: again (a lost message recovers)
        Assert.True(t.ShouldSend(0, 5_000));                    // up: at once
        Assert.True(t.ShouldSend(2, 6_000));                    // dead: once
        Assert.False(t.ShouldSend(2, 6_000 + 3 * Wo173Rules.DownRepeatMs));
    }

    [Fact]
    public void The_poll_takes_the_newest_touched_npcs_within_the_window()
    {
        var touched = new Dictionary<string, long>
        {
            ["a_1"] = 100_000, ["b_2"] = 150_000, ["old_3"] = 1_000, ["bad name"] = 150_000,
        };
        var c = Wo173Rules.DownCandidates(touched, 160_000);
        Assert.Equal(new[] { "b_2", "a_1" }, c);
        var many = Enumerable.Range(0, 50).ToDictionary(i => $"n_{i}", i => (long)(100_000 + i));
        Assert.Equal(Wo173Rules.DownPollMax, Wo173Rules.DownCandidates(many, 120_000).Count);
    }

    [Fact]
    public void The_copy_shows_the_hosts_outcome()
    {
        Assert.Equal(Wo173Rules.CopyShow.HitReaction, Wo173Rules.ShowFor(HitVerdict.Hit));
        Assert.Equal(Wo173Rules.CopyShow.Block, Wo173Rules.ShowFor(HitVerdict.Blocked));
        Assert.Equal(Wo173Rules.CopyShow.PerfectBlock, Wo173Rules.ShowFor(HitVerdict.PerfectBlock));
        Assert.Equal(Wo173Rules.CopyShow.BlockBroken, Wo173Rules.ShowFor(HitVerdict.Broken));
        Assert.Equal(Wo173Rules.CopyShow.Nothing, Wo173Rules.ShowFor(HitVerdict.None));
    }
}
