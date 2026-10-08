// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-161: the hit verdict -- the wire, the swing ledger, exactly-once, the shown-or-reason rule (docs/WO-161-findings.md).</summary>
public class Wo161Tests
{
    // ---------------------------------------------------------------- the wire

    [Fact]
    public void The_verdict_pair_is_in_the_join_table_host_only_with_its_bounds()
    {
        Assert.Equal(((byte)0x72, (byte)0x73), (Protocol.HitVerdictUp, Protocol.HitVerdictDown));
        var row = Protocol.JoinWireFor(Protocol.HitVerdictUp)!.Value;
        Assert.Equal(Protocol.HitVerdictDown, row.Down);
        Assert.Equal(Protocol.JoinFrom.Host, row.From);
        Assert.Equal(Protocol.JoinHeaderLen + Protocol.HitVerdictFixedLen, row.Min);
        Assert.Equal(Protocol.JoinHeaderLen + Protocol.HitVerdictFixedLen + Protocol.MaxNpcNameLen, row.Max);
        Assert.Single(Protocol.JoinWire, x => x.Up == Protocol.HitVerdictUp);
        Assert.DoesNotContain(Protocol.JoinWire, x => x.Up != Protocol.HitVerdictUp && (x.Up is 0x72 || x.Down is 0x73));
        Assert.True(Protocol.IsJoinDown(Protocol.HitVerdictDown, Protocol.JoinHeaderLen + Protocol.HitVerdictFixedLen + 1));
        Assert.False(Protocol.IsJoinDown(Protocol.HitVerdictDown, Protocol.JoinHeaderLen + Protocol.HitVerdictFixedLen));   // a down is one byte longer (the source)
        Assert.Equal(10, Protocol.Version);   // append-only: no bump
    }

    [Theory]
    [InlineData(HitVerdict.Hit, 0, 3, 41u, 6.3f, 29.5f, "ttkc_man_23")]
    [InlineData(HitVerdict.Blocked, 1, 5, 7u, 0f, 24.3f, "tpod_lapkaUCestyZTachova_man_1")]
    [InlineData(HitVerdict.Hit, 4, 0, 0u, 12.5f, 0f, "")]                       // the sampler's fallback: nobody named
    [InlineData(HitVerdict.Parried, 1, 6, 9u, 0f, 0f, "bandit_7")]               // the reserved victim verdicts decode too
    [InlineData(HitVerdict.Missed, 1, 2, 9u, 0f, 0f, "bandit_7")]
    public void A_verdict_round_trips_through_the_down_frame(HitVerdict v, int flagBits, int zone, uint swing, float hp, float st, string name)
    {
        byte flags = (byte)flagBits;
        if (name.Length == 0) flags |= Protocol.HitFlagNoAttacker;
        var m = new HitVerdictMsg(123456u, v, flags, (byte)zone, swing, hp, st, name);
        var up = m.BuildUp(target: 3);
        Assert.Equal(Protocol.HitVerdictUp, up[0]);
        Assert.Equal(up.Length - 3, BinaryPrimitives.ReadUInt16LittleEndian(up.AsSpan(1)));
        Assert.Equal(3, up[3]);                                                  // the target
        Assert.Equal(123456u, BinaryPrimitives.ReadUInt32LittleEndian(up.AsSpan(4)));
        var row = Protocol.JoinWireFor(up[0])!.Value;
        int payloadLen = up.Length - 3;
        Assert.InRange(payloadLen, row.Min, row.Max);
        // the relay prefixes the source id and keeps the rest verbatim
        var down = new byte[1 + payloadLen]; down[0] = 9; up.AsSpan(3).CopyTo(down.AsSpan(1));
        Assert.True(Protocol.IsJoinDown(Protocol.HitVerdictDown, down.Length));
        Assert.True(HitVerdictMsg.TryDecodeDown(down, out byte src, out var got));
        Assert.Equal(9, src);
        Assert.Equal(m, got);
    }

    [Fact]
    public void The_longest_verdict_is_the_widest_name_and_widest_numbers_and_fits_its_row()
    {
        string name = new('n', Protocol.MaxNpcNameLen);
        var m = new HitVerdictMsg(uint.MaxValue, HitVerdict.Hit, Protocol.HitFlagSwingKnown | Protocol.HitFlagMissile, 6, uint.MaxValue, 999.99f, 999.99f, name);
        var up = m.BuildUp(0xFE);
        var row = Protocol.JoinWireFor(up[0])!.Value;
        Assert.Equal(row.Max, up.Length - 3);
        Assert.True(HitVerdictMsg.TryDecode(uint.MaxValue, up.AsSpan(3 + Protocol.JoinHeaderLen), out var got));
        Assert.Equal(m, got);
    }

    [Fact]
    public void A_builder_refuses_what_a_receiver_would_refuse()
    {
        HitVerdictMsg Ok() => new(1, HitVerdict.Hit, Protocol.HitFlagSwingKnown, 2, 5, 1f, 1f, "npc_1");
        Assert.Throws<ArgumentOutOfRangeException>(() => (Ok() with { HitId = 0 }).BuildUp(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => (Ok() with { Verdict = HitVerdict.None }).BuildUp(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => (Ok() with { Verdict = (HitVerdict)9 }).BuildUp(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => (Ok() with { Health = float.NaN }).BuildUp(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => (Ok() with { Stamina = -1f }).BuildUp(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => (Ok() with { Health = 1000.5f }).BuildUp(1));
        foreach (var bad in new[] { "a b", "x\ny", "q\"); os.exit()--", "näme", new string('x', 65), "a;b", "../etc" })
            Assert.Throws<ArgumentException>(() => (Ok() with { Attacker = bad }).BuildUp(1));
    }

    /// <summary>Hand-built bodies (what a hostile or damaged peer could put on the wire) -- each one is refused.</summary>
    [Fact]
    public void A_hostile_or_damaged_body_never_decodes()
    {
        byte[] Good(string name = "ttkc_man_23", byte flags = Protocol.HitFlagSwingKnown)
        {
            var nb = System.Text.Encoding.ASCII.GetBytes(name);
            var b = new byte[Protocol.HitVerdictFixedLen + nb.Length];
            b[0] = Protocol.HitVerdictWire; b[1] = (byte)HitVerdict.Hit; b[2] = nb.Length == 0 ? Protocol.HitFlagNoAttacker : flags; b[3] = 3;
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), 5);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(8), 10f);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(12), 2f);
            b[16] = (byte)nb.Length;
            nb.CopyTo(b, Protocol.HitVerdictFixedLen);
            return b;
        }
        Assert.True(HitVerdictMsg.TryDecode(1, Good(), out _));                    // the control: the hand-built body is valid
        Assert.True(HitVerdictMsg.TryDecode(1, Good(""), out _));
        Assert.False(HitVerdictMsg.TryDecode(0, Good(), out _));                   // hit id 0
        Assert.False(HitVerdictMsg.TryDecode(1, Good()[..^1], out _));            // truncated name
        Assert.False(HitVerdictMsg.TryDecode(1, Good()[..10], out _));            // truncated fixed part
        Assert.False(HitVerdictMsg.TryDecode(1, [], out _));
        var extra = Good().Concat(new byte[] { 0 }).ToArray(); Assert.False(HitVerdictMsg.TryDecode(1, extra, out _));   // trailing byte
        var ver = Good(); ver[0] = 2; Assert.False(HitVerdictMsg.TryDecode(1, ver, out _));
        foreach (byte bad in new byte[] { 0, 5, 200 }) { var v = Good(); v[1] = bad; Assert.False(HitVerdictMsg.TryDecode(1, v, out _), $"verdict {bad}"); }
        var fl = Good(); fl[2] |= 0x80; Assert.False(HitVerdictMsg.TryDecode(1, fl, out _));                  // an unknown flag bit
        var z = Good(); z[3] = 7; Assert.False(HitVerdictMsg.TryDecode(1, z, out _));                         // a zone past Lower
        foreach (float badf in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.5f, 1000.01f, 1e30f })
        {
            var h = Good(); BinaryPrimitives.WriteSingleLittleEndian(h.AsSpan(8), badf); Assert.False(HitVerdictMsg.TryDecode(1, h, out _), $"health {badf}");
            var s = Good(); BinaryPrimitives.WriteSingleLittleEndian(s.AsSpan(12), badf); Assert.False(HitVerdictMsg.TryDecode(1, s, out _), $"stamina {badf}");
        }
        foreach (var bad in new[] { "a b", "x\ny", "q\"); os.exit()--", "a;b", "../etc", "tab\t", "nul\0x" })
        {
            var nb = System.Text.Encoding.Latin1.GetBytes(bad);
            var b = new byte[Protocol.HitVerdictFixedLen + nb.Length];
            Good("x").AsSpan(0, Protocol.HitVerdictFixedLen).CopyTo(b); b[16] = (byte)nb.Length; nb.CopyTo(b, Protocol.HitVerdictFixedLen);
            Assert.False(HitVerdictMsg.TryDecode(1, b, out _), $"name {bad.Replace("\0", "\\0")}");
        }
        var hi = Good("x"); hi[Protocol.HitVerdictFixedLen] = 0xC3; Assert.False(HitVerdictMsg.TryDecode(1, hi, out _));   // a non-ASCII byte
        var over = new byte[Protocol.HitVerdictFixedLen + Protocol.MaxNpcNameLen + 1];
        Good("x").AsSpan(0, Protocol.HitVerdictFixedLen).CopyTo(over); over[16] = (byte)(Protocol.MaxNpcNameLen + 1);
        for (int i = Protocol.HitVerdictFixedLen; i < over.Length; i++) over[i] = (byte)'a';
        Assert.False(HitVerdictMsg.TryDecode(1, over, out _));                                                // a 65-character name
        var mismatch = Good(); mismatch[16] = 3; Assert.False(HitVerdictMsg.TryDecode(1, mismatch, out _));    // the length byte lies
        var named = Good("npc_1", Protocol.HitFlagNoAttacker); Assert.False(HitVerdictMsg.TryDecode(1, named, out _));   // NoAttacker with a name
        var unnamed = Good(""); unnamed[2] = Protocol.HitFlagSwingKnown; Assert.False(HitVerdictMsg.TryDecode(1, unnamed, out _));   // a name-less hit not flagged
    }

    // ---------------------------------------------------------------- the verdict of a measured hit

    [Theory]
    [InlineData(0.0f, 24.0f, HitVerdict.Blocked)]    // the field: hp -0.0 st -24.0 (the replayed guard absorbed it)
    [InlineData(0.04f, 23.0f, HitVerdict.Blocked)]
    [InlineData(0.0f, 0.5f, HitVerdict.Blocked)]
    [InlineData(6.3f, 29.5f, HitVerdict.Hit)]        // the field: hp -6.3 st -29.5
    [InlineData(8.9f, 0.0f, HitVerdict.Hit)]
    [InlineData(0.05f, 24.0f, HitVerdict.Hit)]       // the edge: a loss of 0.05 is a loss
    [InlineData(0.0f, 0.49f, HitVerdict.Hit)]        // damage is never classed away: a tiny stamina-only blow is still sent
    [InlineData(0.0f, 0.0f, HitVerdict.Hit)]
    public void A_measured_hit_is_blocked_only_when_the_guard_took_all_of_it(float hp, float st, HitVerdict want) =>
        Assert.Equal(want, Wo161Rules.Classify(hp, st));

    // ---------------------------------------------------------------- the host's swing ledger and numbering

    [Fact]
    public void The_ledger_gives_each_swing_an_id_and_matches_a_hit_to_its_npcs_latest_swing()
    {
        var l = new Wo161SwingLedger();
        var rowA = Guid.NewGuid(); var rowB = Guid.NewGuid();
        Assert.Equal(1u, l.Record("guard_a", rowA, 1000));
        Assert.Equal(2u, l.Record("guard_b", rowB, 1100));
        Assert.Equal(3u, l.Record("guard_a", rowB, 2000));
        var s = l.Latest("guard_a", 2300)!.Value;                                   // the latest swing, 0.3 s old: the hit's
        Assert.Equal((3u, rowB), (s.Id, s.Row));
        Assert.Equal(2u, l.Latest("guard_b", 1300)!.Value.Id);                      // a different NPC: its own swing
        Assert.Null(l.Latest("guard_a", 2000 + Wo161Rules.SwingWindowMs + 1));     // older than the window: no swing
        Assert.NotNull(l.Latest("guard_a", 2000 + Wo161Rules.SwingWindowMs));
        Assert.Null(l.Latest("nobody", 2000));
        Assert.Null(l.Latest("guard_a", 1999));                                    // a clock that ran backwards matches nothing
    }

    [Fact]
    public void The_ledger_is_bounded()
    {
        var l = new Wo161SwingLedger();
        for (int i = 0; i < 1000; i++) l.Record("npc_" + i, Guid.Empty, 1000 + i);
        Assert.InRange(l.Npcs, 1, 256);
        for (int i = 0; i < 100; i++) l.Record("one", Guid.Empty, 5000 + i);
        Assert.Equal(1u + 1000 + 99, l.Latest("one", 5099)!.Value.Id);
        l.Clear(); Assert.Equal(0, l.Npcs);
    }

    [Fact]
    public void Hit_ids_start_at_one_never_repeat_and_never_read_zero_even_across_the_wrap()
    {
        var ids = new Wo161HitIds();
        Assert.Equal(new[] { 1u, 2u, 3u }, new[] { ids.Next(), ids.Next(), ids.Next() });
        var wrap = new Wo161HitIds(uint.MaxValue - 1);
        Assert.Equal(uint.MaxValue, wrap.Next());
        Assert.Equal(1u, wrap.Next());   // 0 is skipped
        Assert.Equal(2u, wrap.Next());
    }

    // ---------------------------------------------------------------- the victim: once, and shown-or-reason

    [Fact]
    public void A_hit_id_is_applied_once_whatever_the_network_does()
    {
        var d = new Wo161Dedupe();
        Assert.True(d.Accept(1, 10, 1000));
        Assert.False(d.Accept(1, 10, 1001));                                       // a duplicate frame: no second damage
        Assert.False(d.Accept(1, 10, 1000 + Wo161Rules.DupWindowMs));
        Assert.True(d.Accept(2, 10, 1002));                                        // the same id from another host is another hit
        Assert.True(d.Accept(1, 11, 1003));
        Assert.True(d.Accept(1, 10, 1000 + Wo161Rules.DupWindowMs + 1));           // a restarted host's ids start again, after the window
        d.Clear();
        Assert.True(d.Accept(1, 11, 1004));                                        // a new connection forgets
    }

    [Fact]
    public void The_dedupe_memory_is_bounded()
    {
        var d = new Wo161Dedupe();
        for (uint i = 1; i <= 5000; i++) Assert.True(d.Accept(1, i, 1000 + i));
        Assert.False(d.Accept(1, 5000, 6001));   // the newest are still remembered
    }

    [Theory]
    [InlineData(true, false, false, 300L, 310L, null, true, "-")]                       // the row played 0.3 s before the verdict
    [InlineData(true, false, false, -200L, -200L, null, true, "-")]                     // 0.2 s after: the stream's order is not exact
    [InlineData(true, false, false, 1500L, 1500L, null, true, "-")]
    [InlineData(false, false, false, 300L, 300L, null, true, "-")]                      // the victim saw it though the host's flag said none
    [InlineData(false, false, false, null, null, null, false, "no-swing-captured")]     // the field's 26 %: the host's DLL captured no row
    [InlineData(true, false, false, null, null, null, false, "row-not-received")]      // the host sent one, none came
    [InlineData(true, false, false, null, 400L, "unknown-row", false, "row-refused-unknown-row")]
    [InlineData(true, false, false, null, 400L, "no-entity", false, "row-refused-no-entity")]
    [InlineData(true, false, false, null, 400L, null, false, "row-not-played")]
    [InlineData(true, false, false, 9000L, 9000L, null, false, "row-stale")]            // a row from long before is not this hit's
    [InlineData(false, false, true, null, null, null, false, "no-attacker")]
    [InlineData(false, true, false, null, null, null, false, "missile")]
    [InlineData(true, false, true, 300L, 300L, null, true, "-")]                        // shown wins over a missing name
    public void Every_damage_line_has_a_shown_swing_or_a_reason(bool known, bool missile, bool noAttacker, long? played, long? recv, string? refused, bool shown, string reason)
    {
        var (s, why) = Wo161Rules.Judge(known, missile, noAttacker, played, recv, refused);
        Assert.Equal((shown, reason), (s, why));
        Assert.True(s || why != "-", "a hit with no shown swing always names a reason");
    }

    /// <summary>The whole rule on one stream: hits applied exactly once, none dropped for lacking a swing, the counters tell why.</summary>
    [Fact]
    public void A_stream_of_verdicts_applies_each_once_drops_none_and_counts_the_unshown()
    {
        var dedupe = new Wo161Dedupe(); var stats = new Wo161Stats();
        float hp = 0, st = 0; int applied = 0;
        void Receive(HitVerdictMsg m, long now, long? played)
        {
            if (!dedupe.Accept(1, m.HitId, now)) { stats.Dup(); return; }
            var (shown, why) = Wo161Rules.Judge(m.SwingKnown, m.Missile, m.NoAttacker, played, played, null);
            bool apply = m.Health > 0 || m.Stamina > 0;
            if (apply) { applied++; hp += m.Health; st += m.Stamina; }
            stats.In(apply, m.Verdict, m.Health, m.Stamina, shown, why);
        }
        var h1 = new HitVerdictMsg(1, HitVerdict.Hit, Protocol.HitFlagSwingKnown, 3, 11, 16.4f, 40f, "guard_a");
        var h2 = new HitVerdictMsg(2, HitVerdict.Blocked, Protocol.HitFlagSwingKnown, 2, 12, 0f, 37.1f, "guard_a");
        var h3 = new HitVerdictMsg(3, HitVerdict.Blocked, 0, 3, 0, 0f, 37.2f, "guard_a");                       // no swing was captured for this blow
        Receive(h1, 1000, 300); Receive(h1, 1010, 300); Receive(h2, 2000, 280); Receive(h3, 3000, null); Receive(h2, 3010, 280);
        Assert.Equal(3, applied);                                                  // three blows, never five
        Assert.Equal(16.4f, hp, 3);
        Assert.Equal(40f + 37.1f + 37.2f, st, 3);
        Assert.Equal(2, stats.Dups);
        Assert.Equal((2L, 1L), (stats.Shown, stats.NotShown));
        string line = stats.Line();
        Assert.Contains("applied=3", line); Assert.Contains("dup_ignored=2", line); Assert.Contains("hit=1", line); Assert.Contains("blocked=2", line);
        Assert.Contains("not_shown_why=[no-swing-captured:1]", line);
        Assert.Contains("dmg_hp=16.4", line);
    }

    [Fact]
    public void The_reserved_verdicts_with_no_damage_apply_nothing_and_are_not_refusals()
    {
        var stats = new Wo161Stats();
        stats.In(applied: false, HitVerdict.Parried, 0f, 0f, shown: true, "-");
        stats.In(applied: false, HitVerdict.Missed, 0f, 0f, shown: true, "-");
        stats.In(applied: false, HitVerdict.Hit, 5f, 0f, shown: true, "-");     // a real one that could not be applied (down, waking, no DLL): a refusal
        string line = stats.Line();
        Assert.Contains("applied=0", line); Assert.Contains("no_damage=2", line); Assert.Contains("refused=1", line);
    }

    [Fact]
    public void The_stats_line_is_quiet_until_something_happens_and_counts_a_fallback()
    {
        var s = new Wo161Stats();
        Assert.True(s.Quiet);
        s.RowPlayed();
        Assert.True(s.Quiet);                       // rows alone are no reason to speak
        s.Fallback();
        Assert.False(s.Quiet);
        Assert.Contains("out_fallback=1", s.Line());
        Assert.Contains("rows_played=1", s.Line());
    }

    [Fact]
    public void The_flags_byte_carries_exactly_the_three_facts()
    {
        Assert.Equal((byte)0, Wo161Rules.Flags(false, false, false));
        Assert.Equal((byte)1, Wo161Rules.Flags(true, false, false));
        Assert.Equal((byte)2, Wo161Rules.Flags(false, true, false));
        Assert.Equal((byte)4, Wo161Rules.Flags(false, false, true));
        Assert.Equal((byte)7, Wo161Rules.Flags(true, true, true));
    }
}
