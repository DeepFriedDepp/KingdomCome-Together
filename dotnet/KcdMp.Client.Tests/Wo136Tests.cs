// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-136: world presence -- the load hold, stand-ins, the rider's horse, fights, outfit, torch, loot notice (docs/WO-136-findings.md).</summary>
public class Wo136Tests
{
    private static readonly Guid Wolf1 = Guid.Parse("2981ff4d-d38f-4df8-a87a-0a0e2ab1b7fe");
    private static readonly Guid Horse1 = Guid.Parse("4c687543-c385-40f7-9597-be04db4caf4a");
    private static readonly Guid Belt = Guid.Parse("7da54a04-67c4-4767-8b60-ee9211cc465e");
    private static readonly Guid Hose = Guid.Parse("993d563a-7a0b-46d9-8aba-5a9d689bfa03");

    private static BodyState2 State(BodyState2Bits bits) =>
        new(0, 0, bits, WireZone.Undefined, WireGuardStance.None, WireZone.Undefined, 0, 0, 0);

    // ---------------------------------------------------------------- Phase 1: the load hold

    [Fact]
    public void The_hold_keeps_back_every_frame_that_touches_an_npc_or_an_avatar()
    {
        Assert.True(GameBridge.Wo136HeldType(Protocol.NpcStateDown));
        Assert.True(GameBridge.Wo136HeldType(Protocol.Ghost));
        Assert.True(GameBridge.Wo136HeldType(Protocol.NpcDamageDown));
        Assert.True(GameBridge.Wo136HeldType(Protocol.ActionDown));
        Assert.False(GameBridge.Wo136HeldType(Protocol.AppearanceUp));   // an outfit waits for nothing
    }

    [Fact]
    public void The_held_npc_frames_are_keyed_by_name_and_malformed_ones_are_not()
    {
        var name = Encoding.UTF8.GetBytes("prepadeniNaCeste_wolf_1");
        var p = new byte[2 + name.Length + Protocol.NpcStateFixedTail];
        p[0] = 0; p[1] = (byte)name.Length; name.CopyTo(p, 2);
        Assert.Equal("prepadeniNaCeste_wolf_1", GameBridge.Wo136NpcKey(p));
        Assert.Null(GameBridge.Wo136NpcKey(p[..^1]));            // one byte short
        var zero = (byte[])p.Clone(); zero[1] = 0;
        Assert.Null(GameBridge.Wo136NpcKey(zero));               // no name
        Assert.Null(GameBridge.Wo136NpcKey([]));
    }

    // ---------------------------------------------------------------- Phase 2: animal stand-ins

    [Fact]
    public void A_stand_in_is_the_hosts_own_soul_and_class()
    {
        var idx = SoulIndex.FromRows(
            [("prepadeniNaCeste_wolf_1", Wolf1, "Wolf"), ("tzel_horse_1", Horse1, "Horse")],
            [(Belt, "belt_2slot", "QuickSlotContainer"), (Hose, "HoseSeparate04_m01_E", "Armor")]);
        Assert.Equal((Wolf1, "Wolf"), idx.StandIn("prepadeniNaCeste_wolf_1"));
        Assert.Equal((Wolf1, "Wolf"), idx.StandIn("PREPADENINACESTE_WOLF_1"));   // names are case-blind like the game's
        Assert.Equal((Horse1, "Horse"), idx.StandIn("tzel_horse_1"));             // the mod never spawns a horse (Lua)
        Assert.Null(idx.StandIn("nobody_1"));
        Assert.Equal(2, idx.SoulCount);
    }

    [Fact]
    public void A_refused_item_names_its_class_and_a_known_reason()
    {
        var idx = SoulIndex.FromRows([], [(Belt, "belt_2slot", "QuickSlotContainer"), (Hose, "HoseSeparate04_m01_E", "Armor")]);
        Assert.Equal("belt_2slot (QuickSlotContainer)", idx.Describe(Belt));
        Assert.NotNull(idx.UnwearableReason(Belt));
        Assert.Equal("HoseSeparate04_m01_E (Armor)", idx.Describe(Hose));
        Assert.Null(idx.UnwearableReason(Hose));                                  // armour: kcd.log says why
        var unknown = Guid.Parse("a1b2c3d4-0000-0000-0000-000000000001");
        Assert.Equal(unknown.ToString(), idx.Describe(unknown));
    }

    // ---------------------------------------------------------------- Phase 3: the rider owns the horse

    [Fact]
    public void A_ridden_horse_drops_the_hosts_stream_until_the_settle_ends()
    {
        var now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        var ridden = new Dictionary<string, DateTime> { ["tzel_horse_1"] = DateTime.MaxValue };
        Assert.True(Wo136Rules.DropRidden(ridden, "tzel_horse_1", now));
        Assert.False(Wo136Rules.DropRidden(ridden, "tzel_horse_2", now));
        ridden["tzel_horse_1"] = now + Wo136Rules.RideReturn;                      // dismounted
        Assert.True(Wo136Rules.DropRidden(ridden, "tzel_horse_1", now.AddSeconds(2.9)));
        Assert.False(Wo136Rules.DropRidden(ridden, "tzel_horse_1", now.AddSeconds(3.1)));
        Assert.Equal(TimeSpan.FromSeconds(3), Wo136Rules.RideReturn);
    }

    // ---------------------------------------------------------------- Phase 4: knockout beats engagement

    [Fact]
    public void A_host_npc_down_is_never_engaged()
    {
        Assert.True(Wo136Rules.Down(Protocol.NpcStateFlagUnconscious));
        Assert.True(Wo136Rules.Down(Protocol.NpcStateFlagDead));
        Assert.True(Wo136Rules.Down(Protocol.NpcStateFlagUnconscious | 0x80));      // a knocked-out animal
        Assert.False(Wo136Rules.Down(0x80 | 0x04 | 0x20));                         // drawn, engaged, not down
        Assert.False(Wo136Rules.MayEngage(hostDown: true));
        Assert.True(Wo136Rules.MayEngage(hostDown: false));
    }

    // ---------------------------------------------------------------- Phase 5: the torch

    [Fact]
    public void The_torch_rides_the_state_block_and_only_edges_act()
    {
        Assert.Equal((BodyState2Bits)0x20, Wo136Rules.TorchBit);
        var s = State(BodyState2Bits.Crouched);
        var lit = Wo136Rules.WithTorch(s, true);
        Assert.Equal(BodyState2Bits.Crouched | BodyState2Bits.TorchLit, lit.Bits);
        Assert.Equal(BodyState2Bits.Crouched, Wo136Rules.WithTorch(lit, false).Bits);
        Assert.True(Wo136Rules.TorchEdge(null, lit));
        Assert.Null(Wo136Rules.TorchEdge(lit, lit));
        Assert.False(Wo136Rules.TorchEdge(lit, s));
        Assert.Null(Wo136Rules.TorchEdge(null, s));
    }

    // ---------------------------------------------------------------- Phase 7: the loot notice

    [Fact]
    public void Only_another_players_take_shows_the_notice()
    {
        foreach (var v in new[] { "ok", "gone", "mine", "none" }) Assert.True(Wo136Rules.IsTakeVerdict(v));
        Assert.False(Wo136Rules.IsTakeVerdict("unknown"));
        foreach (var v in new[] { "ok", "gone", "unknown", "mine" }) Assert.True(Wo136Rules.IsItemVerdict(v));
        Assert.False(Wo136Rules.IsItemVerdict("none"));
        Assert.True(Wo136Rules.ShowsNotice("gone"));
        Assert.False(Wo136Rules.ShowsNotice("mine"));     // the field's false "Someone already took that"
        Assert.False(Wo136Rules.ShowsNotice("none"));
        Assert.False(Wo136Rules.ShowsNotice("ok"));
    }
}
