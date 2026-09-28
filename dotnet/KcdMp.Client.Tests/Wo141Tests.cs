using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-141: activities and animal attacks -- the wire, the activity read's shape, the DLL's frames and
/// ops, when activities run, the leave-first rule (fight, talk, knockout, death), what an avatar is given, the
/// kinds mask, the player's one-shots (docs/WO-141-findings.md).</summary>
public class Wo141Tests
{
    private static ActivityState Bed(ulong obj) => new(ActivityState.Lying, 0, obj, ActivityState.NoUnstance, 0, ActivityState.NoMinigame, 0, 0);
    private static ActivityState Seat(ulong obj) => new(ActivityState.Sitting, 0, obj, ActivityState.NoUnstance, 0, ActivityState.NoMinigame, 0, 0);
    private static ActivityState Unst(ushort id, ulong loc) => new(0, 0, 0, id, loc, ActivityState.NoMinigame, 0, 0);

    // ---------------------------------------------------------------- the wire

    [Fact]
    public void Activities_ride_the_join_channel_host_out_and_joiner_in()
    {
        var h = Protocol.JoinWireFor(Protocol.ActivityHostUp)!.Value;
        Assert.Equal(Protocol.ActivityHostDown, h.Down);
        Assert.Equal(Protocol.JoinFrom.Host, h.From);
        var p = Protocol.JoinWireFor(Protocol.ActivityPeerUp)!.Value;
        Assert.Equal(Protocol.ActivityPeerDown, p.Down);
        Assert.Equal(Protocol.JoinFrom.Joiner, p.From);
        Assert.Equal(Protocol.JoinHeaderLen + Protocol.ActivityBodyMin, h.Min);
        Assert.Equal(Protocol.JoinHeaderLen + Protocol.ActivityBodyMax, h.Max);
        Assert.Equal(((byte)0x6A, (byte)0x6B, (byte)0x6C, (byte)0x6D), (Protocol.ActivityHostUp, Protocol.ActivityHostDown, Protocol.ActivityPeerUp, Protocol.ActivityPeerDown));
        Assert.Equal(1, Protocol.JoinWire.Count(x => x.Up is 0x6A or 0x6B));
        Assert.Equal(1, Protocol.JoinWire.Count(x => x.Up is 0x6C or 0x6D));
        Assert.True(Protocol.ActivityBodyMax + Protocol.JoinHeaderLen + 1 < ushort.MaxValue);
    }

    [Fact]
    public void The_activity_is_30_bytes_in_the_native_layout()
    {
        var a = new ActivityState(ActivityState.Sitting, 0, 0x0102030405060708, 195, 0x069FF2CFB2012C0F, 1, 0xAABB, 0);
        var b = a.ToBytes();
        Assert.Equal(Protocol.ActivityBytes, b.Length);
        // the same bytes native/tests/wo141_rules_tests.cpp pins
        Assert.Equal(ActivityState.Sitting, b[0]);
        Assert.Equal(0x08, b[2]); Assert.Equal(0x01, b[9]);
        Assert.Equal(195, b[10]); Assert.Equal(0, b[11]);
        Assert.Equal(0x0F, b[12]);
        Assert.Equal(1, b[20]);
        Assert.Equal(ActivityState.FlagOwnsPos, b[29]);
        Assert.True(ActivityState.TryRead(b, out var back));
        Assert.Equal(a.Normalised(), back);
        Assert.False(ActivityState.TryRead(b.AsSpan(0, 29), out _));
        b[29] = 0;   // the owns flag is the reader's own
        Assert.True(ActivityState.TryRead(b, out back));
        Assert.True(back.OwnsPosition);
        Assert.Equal(ActivityState.FlagOwnsPos, back.Flags);
    }

    [Fact]
    public void Normalised_keeps_only_what_is_synced()
    {
        var standing = new ActivityState(1, 3, 77, ActivityState.NoUnstance, 44, ActivityState.NoMinigame, 55, 0xFF).Normalised();
        Assert.True(standing.IsNone);
        Assert.Equal(ActivityState.None, standing);
        var horse = new ActivityState(5, 0, 77, ActivityState.NoUnstance, 0, ActivityState.NoMinigame, 0, 0).Normalised();
        Assert.True(horse.IsNone);   // riding is WO-40/WO-136's
        var crouch = new ActivityState(ActivityState.Crouch, 0, 0, ActivityState.NoUnstance, 0, ActivityState.NoMinigame, 0, 0).Normalised();
        Assert.False(crouch.IsNone);
        Assert.False(crouch.OwnsPosition);
        Assert.Equal(0, Seat(9).Normalised().Cart);
        Assert.Equal(2, new ActivityState(ActivityState.CartStance, 2, 9, ActivityState.NoUnstance, 0, ActivityState.NoMinigame, 0, 0).Normalised().Cart);
        Assert.True(Seat(9).OwnsPosition && Bed(9).OwnsPosition && Unst(168, 5).OwnsPosition);
        Assert.False(Seat(0).OwnsPosition);
        Assert.False(Unst(168, 0).OwnsPosition);   // an unstance on the spot: the stream keeps the body
        Assert.False(new ActivityState(0, 0, 0, ActivityState.NoUnstance, 0, 1, 0, 0).IsNone);   // the player at the grindstone
    }

    [Fact]
    public void Same_body_is_what_an_apply_changes()
    {
        Assert.True(Seat(5).SameBody(Seat(5) with { Minigame = 1 }));
        Assert.False(Seat(5).SameBody(Seat(6)));
        Assert.False(Seat(5).SameBody(Bed(5)));
        Assert.False(Unst(168, 5).SameBody(Unst(179, 5)));
        Assert.True(ActivityState.None.SameBody(new ActivityState(1, 0, 5, ActivityState.NoUnstance, 0, ActivityState.NoMinigame, 0, 0)));
    }

    [Fact]
    public void Npc_and_player_bodies_round_trip_and_bad_ones_are_refused()
    {
        var rows = new[] { new ActivityRow(Protocol.ActivityPeerNone, "rattay_guard_3", Bed(0x0A1B)), new ActivityRow(Protocol.ActivityPeerNone, "tzel_woman_2", Unst(168, 0x0C)) };
        var body = ActivityCodec.BuildBody(Protocol.ActivityKindNpc, rows);
        Assert.True(ActivityCodec.TryDecode(body, out byte kind, out var got));
        Assert.Equal(Protocol.ActivityKindNpc, kind);
        Assert.Equal(rows.Select(r => r with { A = r.A.Normalised() }), got);

        var me = ActivityCodec.BuildBody(Protocol.ActivityKindPlayer, new[] { new ActivityRow(2, "", Seat(0x33)) });
        Assert.Equal(Protocol.ActivityBodyMin, me.Length);
        Assert.True(ActivityCodec.TryDecode(me, out kind, out got));
        Assert.Equal((Protocol.ActivityKindPlayer, (byte)2, ""), (kind, got[0].Peer, got[0].Name));

        Assert.False(ActivityCodec.TryDecode(body.Concat(new byte[] { 0 }).ToArray(), out _, out _));   // trailing byte
        Assert.False(ActivityCodec.TryDecode(body.AsSpan(0, body.Length - 1), out _, out _));          // short
        var bad = (byte[])body.Clone(); bad[0] = 3;
        Assert.False(ActivityCodec.TryDecode(bad, out _, out _));                                      // not a kind
        bad = (byte[])body.Clone(); bad[1] = 0;
        Assert.False(ActivityCodec.TryDecode(bad, out _, out _));                                      // no rows
        bad = (byte[])body.Clone(); bad[5] = 0x0A;
        Assert.False(ActivityCodec.TryDecode(bad, out _, out _));                                      // a control character in a name
        var noName = new byte[Protocol.ActivityBodyMin];
        noName[0] = Protocol.ActivityKindNpc; noName[1] = 1; noName[2] = Protocol.ActivityPeerNone; noName[3] = 0;
        Bed(1).Write(noName.AsSpan(4));
        Assert.False(ActivityCodec.TryDecode(noName, out _, out _));                                   // an NPC row needs its name
        Assert.Throws<ArgumentOutOfRangeException>(() => ActivityCodec.BuildBody(Protocol.ActivityKindNpc, Array.Empty<ActivityRow>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => ActivityCodec.BuildBody(Protocol.ActivityKindNpc, Enumerable.Repeat(rows[0], Protocol.ActivityMaxRows + 1).ToList()));
        Assert.Throws<ArgumentException>(() => ActivityCodec.BuildBody(Protocol.ActivityKindNpc, new[] { new ActivityRow(0xFF, new string('x', Protocol.MaxNpcNameLen + 1), Bed(1)) }));
    }

    // ---------------------------------------------------------------- the DLL's shapes

    [Fact]
    public void The_dll_frame_parses_exactly()
    {
        var rows = new List<Wo141DllRow> { new(Wo141Codec.KindNpc, "rattay_guard_3", Bed(0x0A1B).Normalised()), new(Wo141Codec.KindPlayer, "", Unst(195, 0x0F).Normalised()) };
        var f = Wo141Codec.BuildFrame(rows);
        Assert.True(Wo141Codec.TryParseFrame(f, out var got));
        Assert.Equal(rows, got);
        Assert.False(Wo141Codec.TryParseFrame(f.Concat(new byte[] { 1 }).ToArray(), out _));
        Assert.False(Wo141Codec.TryParseFrame(f.AsSpan(0, f.Length - 1).ToArray(), out _));
        var bad = (byte[])f.Clone(); bad[1] = 9;
        Assert.False(Wo141Codec.TryParseFrame(bad, out _));   // not a kind
        Assert.False(Wo141Codec.TryParseFrame(Wo141Codec.BuildFrame(new List<Wo141DllRow> { new(Wo141Codec.KindNpc, "", Bed(1)) }), out _));
        Assert.True(Wo141Codec.TryParseFrame(new byte[] { 0 }, out var none) && none.Count == 0);
    }

    [Fact]
    public void The_ops_bodies()
    {
        Assert.Equal(new byte[] { 3, (byte)'a', (byte)'b', (byte)'c' }, Wo141Codec.Name("abc"));
        Assert.Equal(new byte[] { 0 }, Wo141Codec.Name(""));   // op 5: the local player
        var na = Wo141Codec.NameAndActivity("kcd2mp_1", Seat(9));
        Assert.Equal(1 + 8 + Protocol.ActivityBytes, na.Length);
        Assert.True(ActivityState.TryRead(na.AsSpan(9), out var a) && a == Seat(9).Normalised());
        var show = Wo141Codec.Show("housekeeper_faceWash", "SmartObjectHolder6[WaterTub.x]", 60);
        Assert.Equal(60, show[0]);
        Assert.Equal(20, show[1]);
        Assert.Equal("housekeeper_faceWash", System.Text.Encoding.ASCII.GetString(show, 2, 20));
        Assert.Equal(30, show[22]);
        Assert.Equal(1 + 1 + 20 + 1 + 30, show.Length);
        Assert.Throws<ArgumentException>(() => Wo141Codec.Name(new string('x', 64)));
        // a real trough holder's name is 75 characters (the live bug: the NPC-name limit refused it)
        const string trough = "SmartObjectHolder6[WaterTub.WaterTub3_554b41ef-bbe9-47f2-8a38-9a851785cfcc]";
        var real = Wo141Codec.Show("housekeeper_faceWash", trough, 60);
        Assert.Equal(75, real[22]);
        Assert.Equal(trough, System.Text.Encoding.ASCII.GetString(real, 23, 75));
        Assert.Throws<ArgumentException>(() => Wo141Codec.Show("housekeeper_faceWash", new string('x', 256), 60));
    }

    // ---------------------------------------------------------------- when it runs, who captures

    [Fact]
    public void Activities_run_in_the_shared_world_with_a_partner_and_not_during_a_load()
    {
        Assert.True(Wo141Rules.Active(true, true, false, false, false));
        Assert.True(Wo141Rules.Active(true, false, true, false, false));
        Assert.False(Wo141Rules.Active(false, true, false, false, false));   // mp_activities off
        Assert.False(Wo141Rules.Active(true, false, false, false, false));   // no partner / not the shared world
        Assert.False(Wo141Rules.Active(true, true, false, true, false));     // a load
        Assert.False(Wo141Rules.Active(true, false, true, false, true));     // a joiner in his own world
        Assert.Equal(0b11, Wo141Rules.CaptureMask(true, true));    // the host: its NPCs and its player
        Assert.Equal(0b10, Wo141Rules.CaptureMask(true, false));   // a joiner: his own body only (the NPCs are the host's)
        Assert.Equal(0, Wo141Rules.CaptureMask(false, true));
    }

    // ---------------------------------------------------------------- the leave-first rule

    [Fact]
    public void A_copy_leaves_its_activity_for_a_fight_a_talk_a_knockout_or_a_death()
    {
        Assert.Null(Wo141Rules.BlockReason(false, false, false));
        Assert.Equal("fight", Wo141Rules.BlockReason(true, false, false));
        Assert.Equal("talk", Wo141Rules.BlockReason(false, false, true));
        Assert.Equal("down", Wo141Rules.BlockReason(false, true, false));   // knocked out or dead (WO-136's Down: either bit)
        Assert.Equal("down", Wo141Rules.BlockReason(true, true, true));     // down beats everything
        Assert.Equal("fight", Wo141Rules.BlockReason(true, false, true));   // a fight beats a talk
        Assert.True(Wo136Rules.Down(Protocol.NpcStateFlagDead));
    }

    // ---------------------------------------------------------------- avatars, kinds, batches

    [Fact]
    public void An_avatar_is_given_the_activity_but_never_the_crouch()
    {
        var crouch = new ActivityState(ActivityState.Crouch, 0, 0, ActivityState.NoUnstance, 0, ActivityState.NoMinigame, 0, 0);
        Assert.True(Wo141Rules.ForAvatar(crouch).IsNone);
        Assert.Equal(Seat(7).Normalised(), Wo141Rules.ForAvatar(Seat(7)));
        Assert.Equal(Unst(195, 3).Normalised(), Wo141Rules.ForAvatar(Unst(195, 3)));
        Assert.Equal("kcd2mp_0", Wo141Rules.AvatarName(0));
        Assert.Equal("kcd2mp_12", Wo141Rules.AvatarName(12));
    }

    [Fact]
    public void The_kinds_mask_turns_parts_off()
    {
        var both = new ActivityState(ActivityState.Sitting, 0, 9, 168, 5, ActivityState.NoMinigame, 0, 0);
        Assert.Equal(both.Normalised(), Wo141Rules.Filter(both, Wo141Rules.KindsAll));
        var stanceOnly = Wo141Rules.Filter(both, Wo141Rules.KindStance);
        Assert.Equal((ActivityState.Sitting, ActivityState.NoUnstance), (stanceOnly.Stance, stanceOnly.Unstance));
        var unstOnly = Wo141Rules.Filter(both, Wo141Rules.KindUnstance);
        Assert.Equal(((byte)0, (ushort)168), (unstOnly.Stance, unstOnly.Unstance));
        Assert.True(Wo141Rules.Filter(both, 0).IsNone);
    }

    [Fact]
    public void Rows_go_out_twelve_to_a_message()
    {
        var rows = Enumerable.Range(0, 25).Select(i => new ActivityRow(0xFF, $"npc_{i}", Bed(1))).ToList();
        var b = Wo141Rules.Batches(rows).ToList();
        Assert.Equal(new[] { 12, 12, 1 }, b.Select(x => x.Count));
        Assert.Equal("npc_24", b[2][0].Name);
        Assert.Empty(Wo141Rules.Batches(new List<ActivityRow>()));
    }

    // ---------------------------------------------------------------- the player's one-shots

    [Fact]
    public void Only_the_troughs_wash_has_an_npc_activity_to_show_it()
    {
        Assert.Equal(("housekeeper_faceWash", (byte)60), Wo141Rules.OneShot("WashFace"));   // observed: 5.2 s for the player
        Assert.Null(Wo141Rules.OneShot("SharpeningMinigame"));
        Assert.Null(Wo141Rules.OneShot("washface"));
        Assert.Null(Wo141Rules.OneShot(""));
    }
}
