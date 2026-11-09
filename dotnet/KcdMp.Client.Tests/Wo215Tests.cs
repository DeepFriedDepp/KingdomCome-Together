// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-215: the agent's pure rules and the switch word, each check with the case (or the twin) that must not pass.</summary>
public class Wo215Tests
{
    [Fact]
    public void Every_switch_ships_on_and_the_twin_names_turn_one_off()
    {
        Assert.All(W215Switch.Names, n => Assert.False(W215Switch.TwinOff(null, n)));
        Assert.True(W215Switch.TwinOff("route", "route"));
        Assert.False(W215Switch.TwinOff("route", "gait"));
        Assert.All(W215Switch.Names, n => Assert.True(W215Switch.TwinOff("all", n)));
        Assert.False(W215Switch.TwinOff("one_bind", "route"));   // WO-214's names are not WO-215's
    }

    [Fact]
    public void The_native_word_has_the_dlls_bit_order()
    {
        // wo215_rules.h: route 1, gait 2, ladder 4, letgo_cull 8, activity_line 16, dormant 32, ghost_fight 64, probe_all 128
        Assert.Equal(0xFFu, W215Switch.NativeBits(_ => true));
        Assert.Equal(0x01u, W215Switch.NativeBits(n => n == "route"));
        Assert.Equal(0x20u, W215Switch.NativeBits(n => n == "dormant"));
        Assert.Equal(0x80u, W215Switch.NativeBits(n => n == "probe_all"));
        Assert.Equal(0xFEu, W215Switch.NativeBits(n => n != "route"));
        Assert.Equal(W215Switch.NativeNames.Length, W215Switch.Names.Length);   // every WO-215 switch is the DLL's
    }

    [Fact]
    public void The_probe_event_parses_and_refuses_what_it_should()
    {
        Assert.True(Wo215Rules.TryParseProbe("1 ttkc_man_5 2350.5 2081.25 112.0 1.5708 0 15", out var p));
        Assert.Equal((byte)1, p.Mode);
        Assert.Equal("ttkc_man_5", p.Name);
        Assert.Equal(2350.5f, p.X);
        Assert.Equal((byte)0, p.Gait);
        Assert.Equal((ushort)15, p.Secs);
        Assert.True(Wo215Rules.TryParseProbe("1 a 0 0 0 0 128 5", out var e));    // exact, walk
        Assert.Equal((byte)128, e.Gait);
        Assert.True(Wo215Rules.TryParseProbe("3 a 0 0 0 0 3 20", out _));          // follow at a sprint
        Assert.False(Wo215Rules.TryParseProbe("5 a 0 0 0 0 0 5", out _));          // no mode 5
        Assert.False(Wo215Rules.TryParseProbe("1 a 0 0 0 0 4 5", out _));          // no gait 4
        Assert.False(Wo215Rules.TryParseProbe("1 a 0 0 NaN 0 0 5", out _));        // never a non-finite target
        Assert.False(Wo215Rules.TryParseProbe("1 a 0 0 0 0 0", out _));            // a field missing
        Assert.False(Wo215Rules.TryParseProbe("1 a 0 0 0 0 0 601", out _));        // ten minutes at most
        Assert.False(Wo215Rules.TryParseProbe(null, out _));
    }
    [Fact]
    public void The_tune_event_makes_op_153s_body()
    {
        Assert.True(Wo215Rules.TryParseTune("150 -", out var a));
        Assert.Equal(new byte[] { 150, 0, 0xFF }, a);
        Assert.True(Wo215Rules.TryParseTune("150 0 1000 150 2000 1000", out var b));
        Assert.Equal(11, b.Length);
        Assert.Equal(1000, BitConverter.ToUInt16(b, 3));
        Assert.Equal(1000, BitConverter.ToUInt16(b, 9));
        Assert.True(Wo215Rules.TryParseTune("- - - - - 500", out var c));
        Assert.Equal(0xFFFF, BitConverter.ToUInt16(c, 3));   // "-": left as it is
        Assert.Equal(500, BitConverter.ToUInt16(c, 9));
        Assert.True(Wo215Rules.TryParseTune("150 0 500 100 1000 500 100", out var d));
        Assert.Equal(13, d.Length);
        Assert.Equal(100, BitConverter.ToUInt16(d, 11));
        Assert.False(Wo215Rules.TryParseTune("150 2", out _));
        Assert.False(Wo215Rules.TryParseTune("150 0 1000 150 2000", out _));
        Assert.False(Wo215Rules.TryParseTune("150 0 99999 150 2000 1000", out _));
    }

    [Fact]
    public void The_engines_ghost_line_names_its_actor()
    {
        Assert.Equal("dummyWanderer_cuman_3", Wo215Rules.GhostActor("[Error] Combat actor init failed for actor 'dummyWanderer_cuman_3'"));
        Assert.Null(Wo215Rules.GhostActor("[Error] Combat actor init failed for actor ''"));
        Assert.Null(Wo215Rules.GhostActor("[Error] something else 'x'"));
        Assert.Null(Wo215Rules.GhostActor("[Error] Combat actor init failed for actor 'a b'"));   // never a name with a space (a Lua string's own)
        Assert.Null(Wo215Rules.GhostActor(null));
    }
}
