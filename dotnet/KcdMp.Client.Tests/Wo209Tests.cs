// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Text;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-209: the switches, the DLL's switch word, the opponent mirror's wire and its translation.</summary>
public class Wo209Tests
{
    [Fact]
    public void Every_switch_ships_on_and_maps_to_its_native_bit()
    {
        Assert.Equal(31u, Wo209Rules.ConfigBits(_ => true));
        Assert.Equal(16u, Wo209Rules.ConfigBits(n => n == "quest_wake_with_host"));
        Assert.Equal(1u, Wo209Rules.ConfigBits(n => n == "copy_opponent_mirror"));
        Assert.Equal(2u, Wo209Rules.ConfigBits(n => n == "copy_attack_ai"));
        Assert.Equal(4u, Wo209Rules.ConfigBits(n => n == "copy_paired_engine"));
        Assert.Equal(8u, Wo209Rules.ConfigBits(n => n == "copy_oneshot_request"));
        Assert.True(Wo209Rules.Default("copy_opponent_mirror", null, null));
        Assert.False(Wo209Rules.Default("copy_opponent_mirror", "copy_opponent_mirror", null));   // a twin
        Assert.Equal(134, Protocol.Wo209OpConfig);
        Assert.Equal(137, Protocol.Wo209OpOppIn);
        Assert.Equal(0x7C, Protocol.NpcOppUp);
        Assert.NotNull(Protocol.JoinWireFor(Protocol.NpcOppUp));   // the relay's table has it
    }

    [Theory]
    [InlineData("copy_opponent_mirror=off", true, "copy_opponent_mirror", false)]
    [InlineData("copy_attack_ai=on", true, "copy_attack_ai", true)]
    [InlineData("copy_attack_ai=maybe", false, "", false)]
    [InlineData("conv_autojoin=on", false, "", false)]
    [InlineData(null, false, "", false)]
    public void The_mods_cfg_event_parses(string? arg, bool ok, string name, bool on)
    {
        Assert.Equal(ok, Wo209Rules.TryParseCfg(arg, out string n, out bool v));
        if (ok) { Assert.Equal(name, n); Assert.Equal(on, v); }
    }

    private static byte[] Dll(params (string Name, byte Kind, uint Opp, string OppName)[] bodies)
    {
        var ms = new MemoryStream();
        ms.WriteByte((byte)bodies.Length);
        foreach (var b in bodies)
        {
            var nm = Encoding.ASCII.GetBytes(b.Name); var on = Encoding.ASCII.GetBytes(b.OppName);
            ms.WriteByte((byte)nm.Length); ms.Write(nm); ms.WriteByte(b.Kind);
            var e = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(e, b.Opp); ms.Write(e);
            ms.WriteByte((byte)on.Length); ms.Write(on);
        }
        return ms.ToArray();
    }

    [Fact]
    public void The_host_names_a_figure_by_its_peer_and_the_joiner_puts_every_opponent_in_its_own_world()
    {
        // the host: bandit 1 fights the host, bandit 3 the joiner's figure (entity 0x7FF66 = peer 2), bandit 4 another NPC, bandit 5 nobody now
        byte[] dll = Dll(("tkop_vrahBandit_1", 1, 0x7777, ""), ("tkop_vrahBandit_3", 2, 0x7FF66, ""), ("tkop_vrahBandit_4", 3, 0x31111, "ttkc_guard_2"),
                         ("tkop_vrahBandit_5", 0, 0, ""), ("tkop_vrahBandit_6", 2, 0x12345, ""));
        Assert.True(Wo209Rules.TryHostWire(dll, e => e == 0x7FF66 ? (byte)2 : (byte)0xFF, out byte[] wire, out int bodies));
        Assert.Equal(5, bodies);
        // the joiner (peer 2; the host is peer 1, its figure here 0x7FF65): copies 0x100.. by name, the guard's copy 0x200
        uint Copy(string n) => n switch { "tkop_vrahBandit_1" => 0x101, "tkop_vrahBandit_3" => 0x103, "tkop_vrahBandit_4" => 0x104,
                                          "tkop_vrahBandit_5" => 0x105, "tkop_vrahBandit_6" => 0x106, "ttkc_guard_2" => 0x200, _ => 0 };
        Assert.True(Wo209Rules.TryJoinerOp(wire, 2, 1, Copy, g => g == 1 ? 0x7FF65u : 0u, out byte[] op, out int known, out int unknown));
        Assert.Equal(5, known);
        Assert.Equal(0, unknown);   // bandit 6's figure no peer owns: the host told it as nobody
        Assert.Equal(1 + known * 9, op.Length);
        var recs = Enumerable.Range(0, known).Select(i => (BinaryPrimitives.ReadUInt32LittleEndian(op.AsSpan(1 + i * 9)), op[1 + i * 9 + 4],
                                                             BinaryPrimitives.ReadUInt32LittleEndian(op.AsSpan(1 + i * 9 + 5)))).ToList();
        Assert.Contains((0x101u, Wo209Rules.HereEntity, 0x7FF65u), recs);   // the host's player there = the host's figure here
        Assert.Contains((0x103u, Wo209Rules.HerePlayer, 0u), recs);         // the joiner's figure there = the local player here
        Assert.Contains((0x104u, Wo209Rules.HereEntity, 0x200u), recs);     // another NPC = its copy here
        Assert.Contains((0x105u, Wo209Rules.HereNone, 0u), recs);           // nobody
        Assert.Contains((0x106u, Wo209Rules.HereNone, 0u), recs);           // a figure no peer owns: nobody
    }

    [Fact]
    public void An_opponent_this_world_cannot_name_tells_nothing_for_that_copy()
    {
        byte[] dll = Dll(("tkop_vrahBandit_4", 3, 0x31111, "ttkc_guard_2"));
        Assert.True(Wo209Rules.TryHostWire(dll, _ => 0xFF, out byte[] wire, out _));
        Assert.True(Wo209Rules.TryJoinerOp(wire, 2, 1, n => n == "tkop_vrahBandit_4" ? 0x104u : 0u, _ => 0u, out byte[] op, out int known, out int unknown));
        Assert.Equal(0, known); Assert.Equal(1, unknown); Assert.Equal(1, op.Length);
    }

    [Fact]
    public void Malformed_bytes_are_refused()
    {
        Assert.False(Wo209Rules.TryHostWire([], _ => 0, out _, out _));
        Assert.False(Wo209Rules.TryHostWire([1, 3, (byte)'a'], _ => 0, out _, out _));
        byte[] bad = Dll(("a", 9, 0, ""));
        Assert.False(Wo209Rules.TryHostWire(bad, _ => 0, out _, out _));
        Assert.False(Wo209Rules.TryJoinerOp([1, 1, (byte)'a', 1], 2, 1, _ => 1, _ => 1, out _, out _, out _));
        byte[] spaced = Dll(("a b", 0, 0, ""));
        Assert.False(Wo209Rules.TryHostWire(spaced, _ => 0, out _, out _));
    }

    [Fact]
    public void No_clock_correction_is_written_over_the_games_own_time_lapse()
    {
        Assert.False(Wo209Rules.RatioWriteAllowed(true, true));    // the joiner's own sleep runs: nothing written
        Assert.True(Wo209Rules.RatioWriteAllowed(false, true));    // no skip: the correction as before
        Assert.True(Wo209Rules.RatioWriteAllowed(true, false));    // twin: the reset is written into the sleep (it ended early)
        Assert.Contains("clock_quiet_in_skip", Wo209Rules.SwitchNames);
        Assert.Equal(0u, Wo209Rules.ConfigBits(n => n == "clock_quiet_in_skip"));   // the agent's own: no DLL bit
    }

    [Fact]
    public void The_later_of_two_forced_starts_of_one_talk_gives_way_and_a_tie_is_the_hosts()
    {
        string a = Wo203CamRules.AnnounceText(429, "normal", true, "sukem", ["sukem"], true);
        string tagged = Wo209Rules.WithForcedTag(a, "otazky_na_suka_1", 1_000_500);
        Assert.True(Wo209Rules.TryParseForcedTag(tagged, out string p, out long t));
        Assert.Equal("otazky_na_suka_1", p); Assert.Equal(1_000_500, t);
        Assert.True(Wo203CamRules.TryParseAnnounce(tagged, out int cid, out _, out _, out _) && cid == 429);   // still a v1 announce
        Assert.False(Wo209Rules.TryParseForcedTag(a, out _, out _));                                           // an untagged one: no tag
        Assert.False(Wo209Rules.TryParseForcedTag(Wo203CamRules.AnnounceText(1, "normal", true, "x", ["x"], false), out _, out _));
        // the bench's case: the joiner's at 12:14:56.2, the host's within the same second -> the host gives way, the joiner keeps his
        Assert.True(Wo209Rules.ForcedTalkYields(true, "otazky_na_suka_1", "otazky_na_suka_1", 1_000_700, 1_000_200, iAmHost: true));
        Assert.False(Wo209Rules.ForcedTalkYields(true, "otazky_na_suka_1", "otazky_na_suka_1", 1_000_200, 1_000_700, iAmHost: false));
        // the later one gives way, whoever it is
        Assert.True(Wo209Rules.ForcedTalkYields(true, "x", "x", 1_010_000, 1_000_000, iAmHost: false));
        Assert.False(Wo209Rules.ForcedTalkYields(true, "x", "x", 1_000_000, 1_010_000, iAmHost: true));
        Assert.False(Wo209Rules.ForcedTalkYields(true, "x", "y", 1_010_000, 1_000_000, iAmHost: true));   // another dialogue: both run
        // twin: off, both copies run
        Assert.False(Wo209Rules.ForcedTalkYields(false, "x", "x", 1_000_700, 1_000_200, iAmHost: true));
    }

    [Fact]
    public void The_stale_sweep_keeps_a_copy_the_mirror_still_has_fighting()
    {
        byte[] dll = Dll(("tkop_vrahBandit_6", 2, 0x7FF66, ""), ("tkop_vrahBandit_2", 0, 0, ""));
        Assert.True(Wo209Rules.TryHostWire(dll, e => e == 0x7FF66 ? (byte)2 : (byte)0xFF, out byte[] wire, out _));
        var kinds = Wo209Rules.WireKinds(wire);
        Assert.Equal([("tkop_vrahBandit_6", Wo209Rules.HostFigure), ("tkop_vrahBandit_2", Wo209Rules.HostNone)], kinds);
        Assert.True(Wo209Rules.StaleKeeps(true, Wo209Rules.HostFigure, 2000));     // d1-defence-early's bandit 6: kept, not removed and re-added
        Assert.False(Wo209Rules.StaleKeeps(true, Wo209Rules.HostFigure, 5000));    // the stream went quiet: the fight is over
        Assert.False(Wo209Rules.StaleKeeps(true, Wo209Rules.HostNone, 100));       // the host's NPC fights nobody
        Assert.False(Wo209Rules.StaleKeeps(false, Wo209Rules.HostFigure, 100));    // twin: released as before
        Assert.Empty(Wo209Rules.WireKinds([2, 1, (byte)'a', 0, 0xFF, 0]));          // malformed
    }

    [Fact]
    public void A_host_verdict_waits_for_the_copys_own_blow_only_with_B_on()
    {
        Assert.True(Wo209Rules.VerdictWaits(true, true, true));
        Assert.False(Wo209Rules.VerdictWaits(false, true, true));   // twin: applied at once, as before (and the local blow then counts twice)
        Assert.False(Wo209Rules.VerdictWaits(true, false, true));   // the host judges everything: nothing here to wait for
        Assert.False(Wo209Rules.VerdictWaits(true, true, false));   // no attacker named
        Assert.True(Wo209Rules.VerdictWaitMs <= 500);
    }

    [Fact]
    public void Adds_only_a_copy_whose_host_NPC_still_fights_stays_in_the_joiners_skirmish()
    {
        Assert.True(Wo209Rules.KeepEngaged(true, true, false));    // turned to the host: still in combat
        Assert.True(Wo209Rules.KeepEngaged(true, false, true));    // CombatMode 0 against a player (WO-147), a target: still fighting
        Assert.False(Wo209Rules.KeepEngaged(true, false, false));  // the fight's end: released
        // twin: the mirror off, released on the retarget as before (WO-187: "it leaves my skirmish")
        Assert.False(Wo209Rules.KeepEngaged(false, true, true));
    }
}
