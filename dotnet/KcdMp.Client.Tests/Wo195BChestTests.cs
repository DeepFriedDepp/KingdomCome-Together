// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-195B 2a: a joiner's unlocked chest rides the door channel -- DoorAsk, flags bit 1 = a chest (Stash), bit 0 = the
/// unlock claim. The wire stays append-only: a frame without the bit is a door exactly as before.
/// </summary>
public class Wo195BChestTests
{
    // A frame as a peer that predates the chest bit builds it: the layout of DoorEvent, flags at [5].
    static byte[] OldDoorFrame(byte flags, string name = "AnimDoor_old", sbyte dir = 1)
    {
        var n = System.Text.Encoding.ASCII.GetBytes(name);
        var b = new byte[DoorEvent.FixedLen + n.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b, 5u);
        b[4] = (byte)dir; b[5] = flags;
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(6), 1f);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(10), 2f);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(14), 3f);
        b[18] = (byte)n.Length;
        n.CopyTo(b, DoorEvent.FixedLen);
        return b;
    }

    [Fact]
    public void The_chest_bit_is_bit_1_and_the_unlock_claim_stays_bit_0()
    {
        Assert.Equal(0x01, DoorEvent.FlagLocked);
        Assert.Equal(0x02, DoorEvent.FlagStash);
        Assert.Equal(0, DoorEvent.FlagLocked & DoorEvent.FlagStash);
    }

    [Fact]
    public void A_chest_ask_round_trips_with_both_bits()
    {
        var ev = new DoorEvent(4242u, 0, DoorEvent.FlagStash | DoorEvent.FlagLocked, 812.5f, -1450.25f, 31.75f, "d0f3a91c4");
        var bytes = ev.ToBytes();
        Assert.Equal(3, bytes[5]);                       // on the wire: flags byte 3
        Assert.True(DoorEvent.TryFromBytes(bytes, out var back));
        Assert.Equal(ev, back);
        Assert.True(back.IsStash);
        Assert.True(back.IsLocked);                      // bit 0 on an ask = the unlock claim
        Assert.Equal((sbyte)0, back.Dir);                // a chest does not move
    }

    [Fact]
    public void A_chest_event_is_the_same_size_as_a_door_event()
    {
        var door = new DoorEvent(1u, 1, DoorEvent.FlagLocked, 1, 2, 3, "d0f3a91c4").ToBytes();
        var chest = new DoorEvent(1u, 0, DoorEvent.FlagStash | DoorEvent.FlagLocked, 1, 2, 3, "d0f3a91c4").ToBytes();
        Assert.Equal(door.Length, chest.Length);
        Assert.Equal(DoorEvent.FixedLen + 9, chest.Length);   // the 9-character name key Lua sends
        Assert.True(DoorEvent.IsDoorName("d0f3a91c4"));
    }

    [Theory]
    [InlineData((byte)0, false)]
    [InlineData((byte)1, true)]
    public void A_frame_from_an_older_peer_with_only_bit_0_is_still_a_door(byte flags, bool bit0)
    {
        Assert.True(DoorEvent.TryFromBytes(OldDoorFrame(flags), out var e));
        Assert.False(e.IsStash);
        Assert.Equal(bit0, e.IsLocked);
        Assert.Equal("AnimDoor_old", e.Name);
        Assert.Equal((sbyte)1, e.Dir);
    }

    [Theory]
    [InlineData((byte)0x04)]
    [InlineData((byte)0x80)]
    [InlineData((byte)0xFC)]
    public void Bits_above_the_chest_bit_never_make_a_chest(byte flags)
    {
        Assert.True(DoorEvent.TryFromBytes(OldDoorFrame(flags), out var e));
        Assert.False(e.IsStash);
        Assert.Equal(flags, e.Flags);
    }

    [Fact]
    public void An_older_host_reads_a_chest_ask_as_a_door_ask_at_that_pivot()
    {
        // What a peer that does not know the bit does with flags 3: its decode is "bit 0 = unlock" and nothing else,
        // so it asks its door lookup for the pivot -- no chest knowledge to act on, it finds no door and refuses.
        var chest = new DoorEvent(1u, 0, DoorEvent.FlagStash | DoorEvent.FlagLocked, 5f, 6f, 7f, "d0f3a91c4");
        Assert.True(DoorEvent.TryFromBytes(chest.ToBytes(), out var e));
        Assert.Equal(1, (e.Flags & DoorEvent.FlagLocked) != 0 ? 1 : 0);
        Assert.Equal((5f, 6f, 7f), (e.X, e.Y, e.Z));
    }

    [Fact]
    public void The_lua_chest_ask_parses_with_the_chest_bit()
    {
        var ask = Wo151Rules.ParseDoorEvent("ask d0f3a91c4 0 3 812.50 -1450.25 31.75", 9u);
        Assert.NotNull(ask);
        Assert.True(ask!.Value.Ask);
        Assert.True(ask.Value.Ev.IsStash);
        Assert.True(ask.Value.Ev.IsLocked);
        Assert.Equal(new DoorEvent(9u, 0, DoorEvent.FlagStash | DoorEvent.FlagLocked, 812.5f, -1450.25f, 31.75f, "d0f3a91c4"), ask.Value.Ev);

        var noClaim = Wo151Rules.ParseDoorEvent("ask d0f3a91c4 0 2 1.00 2.00 3.00", 9u);   // a chest event with no unlock claim still parses
        Assert.True(noClaim!.Value.Ev.IsStash);
        Assert.False(noClaim.Value.Ev.IsLocked);
    }

    [Theory]
    [InlineData("ask AnimDoor7 1 0 1.00 2.00 3.00", false, false)]
    [InlineData("ask AnimDoor7 -1 1 1.00 2.00 3.00", false, true)]
    [InlineData("state AnimDoor7 -1 1 1.00 2.00 3.00", false, true)]
    [InlineData("state AnimDoor7 1 0 1.00 2.00 3.00", false, false)]
    public void The_lua_door_event_without_the_chest_bit_is_still_a_door(string arg, bool stash, bool lockBit)
    {
        var p = Wo151Rules.ParseDoorEvent(arg, 1u);
        Assert.NotNull(p);
        Assert.Equal(stash, p!.Value.Ev.IsStash);
        Assert.Equal(lockBit, p.Value.Ev.IsLocked);
    }

    [Theory]
    [InlineData("state d0f3a91c4 0 2 1 2 3")]     // a host's chest is never sent as a state
    [InlineData("state d0f3a91c4 0 3 1 2 3")]
    [InlineData("ask d0f3a91c4 0 4 1 2 3")]       // no other flag value
    [InlineData("ask d0f3a91c4 0 -1 1 2 3")]
    [InlineData("ask d0f3a91c4 0 x 1 2 3")]
    public void A_chest_flag_where_it_does_not_belong_is_dropped(string arg)
    {
        Assert.Null(Wo151Rules.ParseDoorEvent(arg, 1u));
    }

    [Fact]
    public void A_chest_event_is_labelled_as_one_in_the_log_line_and_a_door_as_before()
    {
        var chest = new DoorEvent(1u, 0, DoorEvent.FlagStash | DoorEvent.FlagLocked, 1, 2, 3, "d0f3a91c4");
        var door = new DoorEvent(1u, 1, DoorEvent.FlagLocked, 1, 2, 3, "AnimDoor1");
        Assert.StartsWith("chest=d0f3a91c4 ", chest.ToString());
        Assert.StartsWith("door=AnimDoor1 ", door.ToString());
    }

    [Fact]
    public void The_chest_rides_the_existing_door_ask_kind_no_new_kind_is_added()
    {
        Assert.Equal(17, (int)ActionKind.DoorAsk);
        Assert.Equal(16, (int)ActionKind.DoorState);
        Assert.Equal(18, (int)ActionKind.AvatarHit);   // the last kind is unchanged
        var top = Enum.GetValues<ActionKind>().Max();
        Assert.Equal(ActionKind.AvatarHit, top);
    }
}
