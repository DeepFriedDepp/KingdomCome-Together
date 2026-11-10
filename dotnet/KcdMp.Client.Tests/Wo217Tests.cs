// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using KcdMp.Client;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-217: the agent's pure rules -- the switch word, B.1's opponents of this player, C's wire checks -- each with the case that must not pass.</summary>
public class Wo217Tests
{
    [Fact]
    public void Every_switch_ships_on_and_the_twin_names_turn_one_off()
    {
        Assert.All(W217Switch.Names, n => Assert.False(W217Switch.TwinOff(null, n)));
        Assert.True(W217Switch.TwinOff("loot_live", "loot_live"));
        Assert.False(W217Switch.TwinOff("loot_live", "item_flags"));
        Assert.All(W217Switch.Names, n => Assert.True(W217Switch.TwinOff("all", n)));
        Assert.False(W217Switch.TwinOff("no_brains", "placement"));   // WO-216's names are not WO-217's
    }

    [Fact]
    public void Route_skip_drops_the_hosts_route_lines_only_while_this_machines_route_replay_is_off()
    {
        Assert.Contains("route_skip", W217Switch.Names);
        Assert.DoesNotContain("route_skip", W217Switch.NativeNames);   // the agent's own: no native bit, the DLL's word unchanged
        Assert.True(Wo217Rules.SkipRoute(routeSkipOn: true, mpRouteOn: false));    // the shipped default: mp_route off -> nothing reads them
        Assert.False(Wo217Rules.SkipRoute(routeSkipOn: true, mpRouteOn: true));    // the replay on: every line still goes in
        Assert.False(Wo217Rules.SkipRoute(routeSkipOn: false, mpRouteOn: false));  // the twin: 0.64.3's way
        Assert.True(W217Switch.TwinOff("all", "route_skip"));
    }

    [Fact]
    public void The_native_word_has_the_dlls_bit_order()
    {
        // wo217_rules.h: seat_writer 1, placement 2, ghost_far 4, kd_follow 8, long_names 16, layer_watch 32, loot_live 64, item_flags 128,
        // encounter_world 256, carts 512, dialog_twins 1024
        Assert.Equal(0x7FFu, W217Switch.NativeBits(_ => true));
        Assert.Equal(0x400u, W217Switch.NativeBits(n => n == "dialog_twins"));
        Assert.Equal(0x001u, W217Switch.NativeBits(n => n == "seat_writer"));
        Assert.Equal(0x040u, W217Switch.NativeBits(n => n == "loot_live"));
        Assert.Equal(0x200u, W217Switch.NativeBits(n => n == "carts"));
    }

    private static byte[] OppWire(params (string name, byte kind, byte ghost, string opp)[] rows)
    {
        var ms = new MemoryStream();
        ms.WriteByte((byte)rows.Length);
        foreach (var (name, kind, ghost, opp) in rows)
        {
            ms.WriteByte((byte)name.Length); ms.Write(Encoding.ASCII.GetBytes(name));
            ms.WriteByte(kind); ms.WriteByte(ghost);
            ms.WriteByte((byte)opp.Length); ms.Write(Encoding.ASCII.GetBytes(opp));
        }
        return ms.ToArray();
    }

    [Fact]
    public void B1_names_only_the_host_NPCs_that_fight_this_players_figure()
    {
        var w = OppWire(("bandit_1", Wo209Rules.HostFigure, 2, ""), ("bandit_2", Wo209Rules.HostFigure, 3, ""), ("bandit_3", Wo209Rules.HostPlayer, 0xFF, ""),
                        ("guard_1", Wo209Rules.HostNpc, 0xFF, "bandit_1"), ("bandit_4", Wo209Rules.HostNone, 0xFF, ""));
        var r = Wo217Rules.OpponentsOfMe(w, 2);
        Assert.Equal(5, r.Count);
        Assert.Equal(new[] { "bandit_1" }, r.Where(x => x.Me).Select(x => x.Name));   // another peer's figure, the host, an NPC: not mine
        Assert.Empty(Wo217Rules.OpponentsOfMe(w, 0xFF).Where(x => x.Me));             // no ghost id yet: nobody is mine
        Assert.Empty(Wo217Rules.OpponentsOfMe(w[..^2], 2));                            // malformed: nothing
    }

    [Fact]
    public void C_items_on_the_wire_are_checked_field_by_field()
    {
        string cls = "4cea28a0-0814-405a-bf24-4fd711f7eb63";
        var ok = Wo217LootRules.ParseItems($"userdata__0000012A:{cls}:1:0.8500:0:1:ttac_blacksmith,x2:{cls}:12:1:1:0:-");
        Assert.NotNull(ok);
        Assert.Equal(2, ok!.Count);
        Assert.True(ok[0].Stolen); Assert.Equal("ttac_blacksmith", ok[0].Owner);
        Assert.True(ok[1].Worn); Assert.Equal("-", ok[1].Owner);
        Assert.Empty(Wo217LootRules.ParseItems("-")!);
        Assert.Null(Wo217LootRules.ParseItems($"user data:{cls}:1:1:0:0:-"));          // a space would break the text's fields
        Assert.Null(Wo217LootRules.ParseItems($"x1:not-a-guid:1:1:0:0:-"));
        Assert.Null(Wo217LootRules.ParseItems($"x1:{cls}:1:1:2:0:-"));                  // worn is 0 or 1
        Assert.Null(Wo217LootRules.ParseItems($"x1:{cls}:1:1:0:0:a\"b"));               // an owner is an engine name
        string lua = Wo217LootRules.LuaItems(ok);
        Assert.Contains("owner=\"ttac_blacksmith\"", lua);
        Assert.DoesNotContain("\"\"", lua);
    }

    [Fact]
    public void E1_corpses_are_checked_field_by_field()
    {
        var ok = Wo217LootRules.ParseCorpses("bandit_1:2176.1:2091.9:110.5:1.57,ttkc_man_3:1:2:3:0");
        Assert.NotNull(ok);
        Assert.Equal(2, ok!.Count);
        Assert.Equal("bandit_1", ok[0].Name);
        Assert.Empty(Wo217LootRules.ParseCorpses("-")!);
        Assert.Null(Wo217LootRules.ParseCorpses("bad name:1:2:3:0"));
        Assert.Null(Wo217LootRules.ParseCorpses("x:1:2:3"));
        Assert.Null(Wo217LootRules.ParseCorpses("x:NaN:2:3:0"));
        Assert.Equal("corpses", Protocol.LootHostName(Protocol.LootHostCorpses));
    }

    [Fact]
    public void F_a_cart_batch_is_checked_and_becomes_op_172s_body()
    {
        var ok = Wo217CartRules.Parse("123456 WHCart[armedCaravanMedium_cart1_5e1dc16a]|2556.600|2006.300|110.710|0.0100|-0.0200|1.5700|karavanyVeSvete_armedCaravan_soldier_1,x_2");
        Assert.NotNull(ok);
        Assert.Equal(123456u, ok!.Value.HostMs);
        Assert.Single(ok.Value.Rows);
        Assert.Equal("karavanyVeSvete_armedCaravan_soldier_1,x_2", ok.Value.Rows[0].Riders);
        Assert.Empty(Wo217CartRules.Parse("5 -")!.Value.Rows);
        Assert.Null(Wo217CartRules.Parse("x WHCart[a]|1|2|3|0|0|0|-"));
        Assert.Null(Wo217CartRules.Parse("5 WHCart a|1|2|3|0|0|0|-"));          // a space in the name
        Assert.Null(Wo217CartRules.Parse("5 WHCart\"a|1|2|3|0|0|0|-"));        // a quote would reach the Lua call
        Assert.Null(Wo217CartRules.Parse("5 WHCart[a]|1|2|NaN|0|0|0|-"));
        Assert.Null(Wo217CartRules.Parse("5 WHCart[a]|1|2|3|0|0|0|bad name"));
        var body = Wo217CartRules.OpBody(7, ok.Value.Rows);
        Assert.Equal(7u, BitConverter.ToUInt32(body, 0));
        Assert.Equal(1, body[4]);
        Assert.Equal(ok.Value.Rows[0].Name.Length, body[5]);
        Assert.Equal(4 + 1 + 1 + ok.Value.Rows[0].Name.Length + 24, body.Length);
        Assert.Equal("carts", Protocol.LootHostName(Protocol.LootHostCarts));
        Assert.Equal("1 a|1;b|2", Wo217CartRules.Trim("1 a|1;b|2;c|3", 10));
    }

    [Fact]
    public void E2_a_prop_row_is_checked_field_by_field()
    {
        var ok = Wo217PropRules.Parse("campfire_1|FireplaceSmartObject|-|objects/camp/fire.cgf|2176.10|2091.90|110.50|0|0|1.57");
        Assert.NotNull(ok);
        Assert.Equal("objects/camp/fire.cgf", ok!.Model);
        Assert.Null(Wo217PropRules.Parse("x|Player|-|-|1|2|3|0|0|0"));                     // not a prop class
        Assert.Null(Wo217PropRules.Parse("x\"y|BasicEntity|-|-|1|2|3|0|0|0"));            // a quote would reach Lua
        Assert.Null(Wo217PropRules.Parse("x|BasicEntity|-|-|1|2|3|0|0"));                  // a field short
        Assert.Equal("prop", Protocol.LootHostName(Protocol.LootHostProp));
        Assert.Equal("prop-gone", Protocol.LootHostName(Protocol.LootHostPropGone));
    }

    [Fact]
    public void C_kinds_are_new_and_named()
    {
        Assert.Equal("take-request", Protocol.LootAskName(Protocol.LootAskTakeReq));
        Assert.Equal("body-list", Protocol.LootHostName(Protocol.LootHostBodyList));
        Assert.Equal("take-verdict", Protocol.LootHostName(Protocol.LootHostTakeVerdict));
        Assert.NotEqual(Protocol.LootAskTakeReq, Protocol.LootAskTakedown);
        Assert.NotEqual(Protocol.LootHostBodyList, Protocol.LootHostBuild);
    }
}
