// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

// WO-144 1.1: the phantom partner -- who counts as a partner, and who a sleep or wait vote waits for.
public class Wo144PeerTests
{
    [Fact]
    public void A_removed_ghost_stays_removed_until_the_relay_names_it_again()
    {
        var s = new PeerSet();
        Assert.True(s.Connected(1, "joiner"));
        Assert.True(s.IsLive(1));
        Assert.Equal("joiner", s.Disconnected(1));
        Assert.False(s.IsLive(1));
        Assert.True(s.IsRemoved(1));            // a late frame from 1 is dropped, never a partner again
        Assert.Empty(s.Partners(0));
        Assert.True(s.Connected(1, "someone")); // the relay reused the id for a new connection
        Assert.False(s.IsRemoved(1));
        Assert.Equal(new byte[] { 1 }, s.Partners(0));
    }

    [Fact]
    public void The_field_sequence_leaves_one_partner_not_two()
    {
        // H2 agent.log, 19:46:57-19:46:59: the joiner's new connection (1), then his old one (2) timing out.
        var s = new PeerSet();
        s.Connected(2, "joiner");   // the old connection, named when this host connected
        s.Connected(1, "joiner");   // the joiner's game restarted: a new connection
        Assert.Equal(new byte[] { 2 }, s.SameName(1, "joiner"));
        s.Disconnected(2);          // the old one goes
        Assert.Equal(new byte[] { 1 }, s.Partners(0));   // peers=1, not peers=2 all session
        Assert.True(s.IsRemoved(2));
    }

    [Fact]
    public void Partners_never_include_this_machine_and_come_in_id_order()
    {
        var s = new PeerSet();
        s.Connected(3, "c"); s.Connected(0, "host"); s.Connected(1, "a");
        Assert.Equal(new byte[] { 1, 3 }, s.Partners(0));
        s.Clear();
        Assert.Empty(s.Partners(0));
        Assert.False(s.IsRemoved(1));
    }

    [Fact]
    public void A_vote_counts_only_partners_connected_and_in_this_world()
    {
        const ushort inWorld = Protocol.LeashFlagInWorld;
        Assert.True(Wo144Rules.CountsInVote(true, true, inWorld));
        Assert.True(Wo144Rules.CountsInVote(true, true, (ushort)(inWorld | Protocol.LeashFlagDialogue)));
        Assert.False(Wo144Rules.CountsInVote(false, true, inWorld));                                        // gone
        Assert.False(Wo144Rules.CountsInVote(true, true, (ushort)(inWorld | Protocol.LeashFlagLoading)));   // loading
        Assert.False(Wo144Rules.CountsInVote(true, true, Protocol.LeashFlagSeparate));                      // its own world
        Assert.False(Wo144Rules.CountsInVote(true, true, Protocol.LeashFlagMenu));                          // the main menu, not joined
        Assert.True(Wo144Rules.CountsInVote(true, false, 0));                                               // no word yet: asked
        Assert.Equal("loading", Wo144Rules.VoteExclusion(true, true, (ushort)(inWorld | Protocol.LeashFlagLoading)));
        Assert.Equal("in its own world", Wo144Rules.VoteExclusion(true, true, Protocol.LeashFlagSeparate));
        Assert.Null(Wo144Rules.VoteExclusion(true, true, inWorld));
    }

    [Fact]
    public void A_partner_who_leaves_mid_vote_is_dropped_and_the_others_decide()
    {
        var v = new Wo140Rules.Vote { Id = 1, Asker = 0, DeadlineMs = 30_000 };
        v.Members.Add(1); v.Members.Add(2);
        v.Answer(1, "yes");
        Assert.Equal(Wo140Rules.Verdict.Pending, v.Evaluate(1_000));
        v.Left(2);
        Assert.Equal(Wo140Rules.Verdict.Go, v.Evaluate(1_000));   // not waited on, not a no
        v.Left(5);                                                // not a member: nothing
        Assert.Single(v.Dropped);

        var n = new Wo140Rules.Vote { Id = 2, Asker = 0, DeadlineMs = 30_000 };
        n.Members.Add(1); n.Members.Add(2);
        n.Answer(1, "no");
        n.Left(2);
        Assert.Equal(Wo140Rules.Verdict.No, n.Evaluate(1_000));   // a no still stops it
    }
}

// WO-144 1.4: the tutorial-era join -- money decides, items only warn, and the player is told why.
public class Wo144HenryTests
{
    private const string Money = GameBridge.MoneyClass;
    private const string Apple = "0a0a0a0a-0000-0000-0000-00000000a001";
    private const string Sword = "0c0c0c0c-0000-0000-0000-00000000c001";

    private static WhsSave.PlayerSoul Henry(bool list, params WhsSave.InvItem[] items)
    {
        var h = new WhsSave.PlayerSoul { HasItemList = list };
        h.Inventory.AddRange(items);
        return h;
    }

    [Fact]
    public void A_tutorial_era_file_that_lists_only_money_joins_with_a_warning()
    {
        // playline3/autosave003-shaped: the list holds the money item only; the live Henry wears 11 things
        var f = Henry(true, new WhsSave.InvItem("m", 0xF, Money, "amount=14030;p2=00020000"));
        string live = $"money=1403.0 items={Apple}:4;{Sword}:1;{Money}:14030 skills=";
        var (v, why) = GameBridge.JudgeHenry(f, live);
        Assert.Equal(GameBridge.HenryVerdict.Warning, v);
        Assert.Contains("tutorial-era", why);
    }

    [Fact]
    public void No_item_list_at_all_is_a_warning_even_with_money_live()
    {
        var (v, why) = GameBridge.JudgeHenry(Henry(false), $"money=10.0 items={Apple}:1 skills=");
        Assert.Equal(GameBridge.HenryVerdict.Warning, v);
        Assert.Contains("no item list", why);
    }

    [Fact]
    public void Other_money_or_an_unreadable_Henry_still_sends_him_home_with_a_reason()
    {
        var f = Henry(true, new WhsSave.InvItem("m", 0, Money, "amount=151"), new WhsSave.InvItem("a", 0, Apple, ""));
        Assert.Equal(GameBridge.HenryVerdict.Match, GameBridge.JudgeHenry(f, $"money=15.10 items={Apple}:1 skills=").Verdict);
        Assert.Equal(GameBridge.HenryVerdict.Warning, GameBridge.JudgeHenry(f, $"money=15.10 items={Apple}:2 skills=").Verdict);
        var (v, why) = GameBridge.JudgeHenry(f, $"money=15.20 items={Apple}:1 skills=");
        Assert.Equal(GameBridge.HenryVerdict.Mismatch, v);
        Assert.Contains("different money", GameBridge.HenryAbortText(why));
        var (v2, why2) = GameBridge.JudgeHenry(f, "timeout");
        Assert.Equal(GameBridge.HenryVerdict.Mismatch, v2);
        Assert.Contains("could not be read", GameBridge.HenryAbortText(why2));
    }
}

// WO-144 2.1: clothes -- under-layers first, a refusal retried on a timer, the game's own reason.
public class Wo144ClothesTests
{
    [Fact]
    public void Under_layers_go_on_before_the_pieces_that_need_them()
    {
        string[] field = { "ArmPlate01_m01_C2", "BootsAnkle03_m01_D", "GambesonLong01_m15_C3", "HoseSeparate04_m01_E", "Gloves01_m01_C1", "Hood08_m02_D", "MailShort02_m01", "Cuirass07_m01_A4" };
        var order = field.OrderBy(Wo144Rules.EquipLayer).ToList();
        Assert.True(order.IndexOf("GambesonLong01_m15_C3") < order.IndexOf("ArmPlate01_m01_C2"));   // body_cloth_padded before the plate that needs it
        Assert.True(order.IndexOf("HoseSeparate04_m01_E") < order.IndexOf("GambesonLong01_m15_C3"));
        Assert.True(order.IndexOf("MailShort02_m01") < order.IndexOf("Cuirass07_m01_A4"));
        Assert.True(order.IndexOf("Cuirass07_m01_A4") < order.IndexOf("Hood08_m02_D"));
    }

    [Fact]
    public void A_refusal_is_tried_again_after_its_back_off_not_only_when_the_outfit_changes()
    {
        var plate = Guid.NewGuid(); var belt = Guid.NewGuid();
        var u = new Wo135Rules.Unwearable();
        u.OnTarget(new HashSet<Guid> { plate, belt });
        Assert.True(u.MarkTimed(plate, 1_000));
        Assert.True(u.Skips(plate, 20_000));
        Assert.False(u.Skips(plate, 21_000));            // 20 s later: tried again
        Assert.False(u.MarkTimed(plate, 21_000));        // refused again: 60 s now
        Assert.True(u.Skips(plate, 80_000));
        Assert.False(u.Skips(plate, 81_001));
        u.Mark(belt);                                    // never an NPC's (a quick-slot belt): for this outfit
        Assert.True(u.Skips(belt, long.MaxValue));
        Assert.Equal(new long[] { 20_000, 60_000, 180_000, 600_000, 600_000 }, new[] { 1, 2, 3, 4, 9 }.Select(n => Wo144Rules.RefusalBackoffMs(n)).ToArray());
        u.OnTarget(new HashSet<Guid> { plate });         // the outfit changed: everything is tried again
        Assert.False(u.Skips(plate, 21_500));
        Assert.Equal(0, u.Count);
    }

    [Fact]
    public void The_games_own_reason_is_read_from_its_line()
    {
        var r = Wo144Rules.ParseCantEquip("[Warning] Can't equip armor 'ArmPlate01_m01_C2'. It requires 'body_cloth_padded' slot to be filled.");
        Assert.NotNull(r);
        Assert.Equal("ArmPlate01_m01_C2", r!.Value.Item);
        Assert.Equal("It requires 'body_cloth_padded' slot to be filled.", r.Value.Reason);
        Assert.Null(Wo144Rules.ParseCantEquip("[Warning] Failed to equip item from inventory [Item-43207]."));
    }
}

// WO-144 2.4 / 5: lights are the torch sync's; a joiner never claims the host.
public class Wo144LightsAndClaimTests
{
    [Fact]
    public void Torches_and_lamps_are_never_outfit_pieces()
    {
        Assert.True(Wo144Rules.IsLight(Guid.Parse("4cea28a0-0814-405a-bf24-4fd711f7eb63")));   // torch_weapon (the player's)
        Assert.True(Wo144Rules.IsLight(Guid.Parse("bdf14d9c-7264-434c-96af-748ff2779c1b")));   // lamp_tool (an NPC soul's)
        Assert.True(Wo144Rules.IsLight(Guid.Parse("d1a6946e-4184-42b7-bc15-1172e0c7de93")));   // lamp_toolFancy
        Assert.False(Wo144Rules.IsLight(Guid.Parse("34d1aa11-e0ae-4d87-95bc-bcdfd0f50d5f")));  // a coat
        Assert.False(Wo144Rules.IsLight(Guid.Parse("c164f346-0463-4116-b790-094b11274e5e")));  // a hunting sword
    }

    [Fact]
    public void A_non_hosting_agent_waits_for_the_hosts_word_before_it_claims()
    {
        var t0 = new DateTime(2026, 9, 29, 20, 0, 0, DateTimeKind.Utc);
        Assert.False(Wo144Rules.ClaimGraceOver(DateTime.MinValue, t0));            // not connected
        Assert.False(Wo144Rules.ClaimGraceOver(t0, t0.AddSeconds(2)));             // a host speaks within a second or two
        Assert.False(Wo144Rules.ClaimGraceOver(t0, t0.AddSeconds(9.9)));
        Assert.True(Wo144Rules.ClaimGraceOver(t0, t0.AddSeconds(10)));             // nobody spoke: this machine hosts
    }

    [Fact]
    public void The_live_avatar_is_found_by_its_entity_key_not_by_its_name()
    {
        // J1: two souls named kcd2mp_0 -- the live avatar and a saved one from the host's world
        var live = Guid.Parse("026314f3-d093-856b-b659-fc773649cb90");
        var saved = Guid.Parse("af1c21e2-84ae-2d41-a76c-83da0b1de138");
        var other = Guid.Parse("02a8d11d-efdf-4c8a-95f6-75d3905d95e9");
        Assert.Equal(new[] { live }, Wo144Rules.SoulsWithEntityKey(new[] { saved, other, live }, "b659fc773649cb90"));
        Assert.Equal(new[] { live }, Wo144Rules.SoulsWithEntityKey(new[] { saved, live }, "B659FC773649CB90"));
        Assert.Empty(Wo144Rules.SoulsWithEntityKey(new[] { saved, other }, "b659fc773649cb90"));
        Assert.Empty(Wo144Rules.SoulsWithEntityKey(new[] { live }, "b659fc77"));   // not a whole key
    }

    [Fact]
    public void The_outfit_is_read_back_between_packets_every_ten_seconds()
    {
        Assert.Equal(10_000, Wo144Rules.OutfitCheckMs);
    }
}
