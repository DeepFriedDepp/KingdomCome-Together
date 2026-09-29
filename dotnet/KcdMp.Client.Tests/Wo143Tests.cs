using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-143: activities part 2 -- the wire (hands, gaits, one-shots, looks), the DLL's frames and ops, when
/// each piece runs (every switch, host and joiner), the players' minigames on the avatar, who a copy looks at here,
/// the temporary tools (docs/WO-143-findings.md).</summary>
public class Wo143Tests
{
    private const string Saw = "49200aeb-5676-45eb-9fb2-402df0d09aa9";   // carpenter_saw (the woodworker, live)
    private const string Hoe = "4d444b36-afde-42c9-8107-88ec448d4158";   // iron_hoe (the field hoers, live)

    private static ActivityState Minigame(byte type, byte stance = 0) =>
        new(stance, 0, stance != 0 ? 0x1122UL : 0, ActivityState.NoUnstance, 0, type, 0x3344, 0);

    // ---------------------------------------------------------------- the wire

    [Fact]
    public void The_extra_rows_ride_the_join_channel_from_the_host_only()
    {
        var r = Protocol.JoinWireFor(Protocol.ActivityExtraUp)!.Value;
        Assert.Equal(Protocol.ActivityExtraDown, r.Down);
        Assert.Equal(Protocol.JoinFrom.Host, r.From);
        Assert.Equal(((byte)0x6E, (byte)0x6F), (Protocol.ActivityExtraUp, Protocol.ActivityExtraDown));
        Assert.Equal(Protocol.JoinHeaderLen + Protocol.ExtraBodyMin, r.Min);
        Assert.Equal(Protocol.JoinHeaderLen + Protocol.ExtraBodyMax, r.Max);
        Assert.Equal(1, Protocol.JoinWire.Count(x => x.Up is 0x6E or 0x6F));
        Assert.True(Protocol.ExtraBodyMax + Protocol.JoinHeaderLen + 1 < ushort.MaxValue);
    }

    [Fact]
    public void A_class_id_reads_as_Lua_prints_it()
    {
        var b = ExtraRow.ClassBytes(Saw);
        // the Windows GUID layout the DLL reads out of C_Item slot 0x18 (Data1..3 little-endian, Data4 in order)
        Assert.Equal(new byte[] { 0xEB, 0x0A, 0x20, 0x49, 0x76, 0x56, 0xEB, 0x45, 0x9F, 0xB2, 0x40, 0x2D, 0xF0, 0xD0, 0x9A, 0xA9 }, b);
        Assert.Equal(Saw, ExtraRow.ClassText(b));
        Assert.Equal("-", ExtraRow.ClassText(new byte[16]));
        Assert.Equal(new byte[16], ExtraRow.ClassBytes("not a guid"));
    }

    [Fact]
    public void Every_kind_round_trips_and_a_bad_body_is_refused()
    {
        var hands = new[] { new ExtraRow { Name = "ttkc_woodworker", Right = ExtraRow.ClassBytes(Saw) }, new ExtraRow { Name = "ttkc_man_28", Left = ExtraRow.ClassBytes(Hoe) } };
        var body = ExtraCodec.BuildBody(Protocol.ExtraKindHands, hands);
        Assert.Equal(2 + 2 * (1 + 15 + 32) - 4, body.Length);   // 15- and 11-character names
        Assert.True(ExtraCodec.TryDecode(body, out byte k, out var got));
        Assert.Equal(Protocol.ExtraKindHands, k);
        Assert.Equal(Saw, ExtraRow.ClassText(got[0].Right));
        Assert.True(got[0].Left.All(x => x == 0));
        Assert.False(got[0].HandsEmpty);

        body = ExtraCodec.BuildBody(Protocol.ExtraKindGaits, new[] { new ExtraRow { Name = "ttkc_man_28", Gaits = 0x101 } });
        Assert.True(ExtraCodec.TryDecode(body, out k, out got));
        Assert.Equal((ushort)0x101, got[0].Gaits);

        var serve = new ExtraRow { Name = "ttkc_inkeeper", Fragment = "Bartender_ServeBeer", Tags = "fillDistClose,fillSideLeft", AlignGuid = 0xB76380EA26B349BA, Flags = 1 };
        body = ExtraCodec.BuildBody(Protocol.ExtraKindOneShot, new[] { serve });
        Assert.True(ExtraCodec.TryDecode(body, out k, out got));
        Assert.Equal((serve.Fragment, serve.Tags, serve.AlignGuid, serve.Flags), (got[0].Fragment, got[0].Tags, got[0].AlignGuid, got[0].Flags));
        var howl = ExtraCodec.BuildBody(Protocol.ExtraKindOneShot, new[] { new ExtraRow { Name = "ttkc_dog_5", Fragment = "Howl" } });
        Assert.True(ExtraCodec.TryDecode(howl, out _, out got));
        Assert.Equal("", got[0].Tags);
        Assert.Throws<ArgumentException>(() => ExtraCodec.BuildBody(Protocol.ExtraKindOneShot, new[] { serve, serve }));
        Assert.Throws<ArgumentException>(() => ExtraCodec.BuildBody(Protocol.ExtraKindOneShot, new[] { new ExtraRow { Name = "x" } }));

        body = ExtraCodec.BuildBody(Protocol.ExtraKindLooks, new[] { new ExtraRow { Name = "ttkc_man_5", TargetKind = Protocol.LookHostPlayer }, new ExtraRow { Name = "ttkc_man_2", TargetKind = Protocol.LookNpc, Target = "ttkc_inkeeper" } });
        Assert.True(ExtraCodec.TryDecode(body, out k, out got));
        Assert.Equal((Protocol.LookHostPlayer, ""), (got[0].TargetKind, got[0].Target));
        Assert.Equal((Protocol.LookNpc, "ttkc_inkeeper"), (got[1].TargetKind, got[1].Target));

        // refusals: an unknown kind, a truncated row, a trailing byte, a look kind past 3, a non-ASCII name, an empty name
        var bad = (byte[])body.Clone(); bad[0] = 9; Assert.False(ExtraCodec.TryDecode(bad, out _, out _));
        Assert.False(ExtraCodec.TryDecode(body.AsSpan(0, body.Length - 1), out _, out _));
        Assert.False(ExtraCodec.TryDecode(body.Concat(new byte[] { 0 }).ToArray(), out _, out _));
        bad = (byte[])body.Clone(); bad[2 + 1 + "ttkc_man_5".Length] = 4; Assert.False(ExtraCodec.TryDecode(bad, out _, out _));
        bad = ExtraCodec.BuildBody(Protocol.ExtraKindGaits, new[] { new ExtraRow { Name = "ab", Gaits = 1 } }); bad[3] = 0xC3; Assert.False(ExtraCodec.TryDecode(bad, out _, out _));
        Assert.Throws<ArgumentException>(() => ExtraCodec.BuildBody(Protocol.ExtraKindGaits, new[] { new ExtraRow { Name = "" } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExtraCodec.BuildBody(Protocol.ExtraKindGaits, Enumerable.Range(0, 13).Select(i => new ExtraRow { Name = $"n{i}" }).ToList()));
    }

    [Fact]
    public void The_biggest_body_fits_the_relay_gate()
    {
        string n64(int i) => (i.ToString("D2") + new string('n', Protocol.MaxNpcNameLen)).Substring(0, Protocol.MaxNpcNameLen);
        var looks = Enumerable.Range(0, Protocol.ExtraMaxRows).Select(i => new ExtraRow { Name = n64(i), TargetKind = Protocol.LookNpc, Target = n64(i + 40) }).ToList();
        Assert.Equal(Protocol.ExtraBodyMax, ExtraCodec.BuildBody(Protocol.ExtraKindLooks, looks).Length);
        var hands = Enumerable.Range(0, Protocol.ExtraMaxRows).Select(i => new ExtraRow { Name = n64(i), Left = ExtraRow.ClassBytes(Hoe) }).ToList();
        Assert.True(ExtraCodec.BuildBody(Protocol.ExtraKindHands, hands).Length <= Protocol.ExtraBodyMax);
        var shot = new ExtraRow { Name = n64(1), Fragment = new string('f', Protocol.ExtraFragMax), Tags = new string('t', Protocol.ExtraTagsMax), AlignGuid = 1 };
        Assert.True(ExtraCodec.BuildBody(Protocol.ExtraKindOneShot, new[] { shot }).Length <= Protocol.ExtraBodyMax);
        var smallest = ExtraCodec.BuildBody(Protocol.ExtraKindGaits, new[] { new ExtraRow { Name = "a" } });
        Assert.Equal(Protocol.ExtraBodyMin, smallest.Length);
    }

    // ---------------------------------------------------------------- the DLL's frames and ops

    [Fact]
    public void The_DLL_frames_parse_exactly()
    {
        var rows = new[] { new ExtraRow { Name = "ttkc_man_28", Left = ExtraRow.ClassBytes(Hoe) } };
        var f = Wo143Codec.Frame(Wo143DllFrame.KindHands, rows);
        Assert.True(Wo143DllFrame.TryParse(f, out var p));
        Assert.Equal(Wo143DllFrame.KindHands, p.Kind);
        Assert.Equal(Hoe, ExtraRow.ClassText(p.Rows[0].Left));
        Assert.False(Wo143DllFrame.TryParse(f.Concat(new byte[] { 1 }).ToArray(), out _));
        Assert.False(Wo143DllFrame.TryParse(f.AsSpan(0, f.Length - 1).ToArray(), out _));

        f = Wo143Codec.Frame(Wo143DllFrame.KindOneShot, new[] { new ExtraRow { Name = "ttkc_woman_15", Fragment = "HousekeeperDrinkWaterTub", Tags = "", AlignGuid = 0x8E00270C23FE4257, Flags = 1 } });
        Assert.True(Wo143DllFrame.TryParse(f, out p));
        Assert.Equal(("HousekeeperDrinkWaterTub", 0x8E00270C23FE4257UL, (byte)1), (p.Rows[0].Fragment, p.Rows[0].AlignGuid, p.Rows[0].Flags));

        f = Wo143Codec.Frame(Wo143DllFrame.KindLooks, new[] { new ExtraRow { Name = "ttkc_man_5", TargetKind = Protocol.LookPeer, Target = "1" } });
        Assert.True(Wo143DllFrame.TryParse(f, out p));
        Assert.Equal((Protocol.LookPeer, "1"), (p.Rows[0].TargetKind, p.Rows[0].Target));

        f = Wo143Codec.Frame(Wo143DllFrame.KindNeedItem, new[] { new ExtraRow { Name = "ttkc_man_28", Left = ExtraRow.ClassBytes(Hoe) } });
        Assert.True(Wo143DllFrame.TryParse(f, out p));
        Assert.Equal((Wo143DllFrame.KindNeedItem, Hoe), (p.Kind, ExtraRow.ClassText(p.Rows[0].Left)));

        // [6][nameLen][name][result][requestId:4]
        var done = new byte[] { 6, 2, (byte)'a', (byte)'b', 2, 14, 0, 0, 0 };
        Assert.True(Wo143DllFrame.TryParse(done, out p));
        Assert.Equal((Wo143DllFrame.KindShotDone, "ab", (byte)2, 14), (p.Kind, p.Rows[0].Name, p.Result, p.RequestId));
        Assert.False(Wo143DllFrame.TryParse(new byte[] { 6, 2, (byte)'a', (byte)'b', 2, 14, 0, 0 }, out _));
        Assert.False(Wo143DllFrame.TryParse(new byte[] { 7, 0 }, out _));
    }

    [Fact]
    public void The_op_bodies_are_the_native_layout()
    {
        var h = Wo143Codec.Hands("ttkc_man_28", ExtraRow.ClassBytes(Hoe), new byte[16]);
        Assert.Equal(1 + 11 + 32, h.Length);
        Assert.Equal(11, h[0]);
        Assert.Equal(ExtraRow.ClassBytes(Hoe), h.AsSpan(12, 16).ToArray());
        var g = Wo143Codec.Gaits("ttkc_man_28", 0x0102);
        Assert.Equal(new byte[] { 0x02, 0x01 }, g.AsSpan(12, 2).ToArray());
        var o = Wo143Codec.OneShot("ttkc_dog_5", "Howl", "", 0x0807060504030201, 1);
        // [nameLen][name][fragLen][frag][tagsLen][tags][align:8 LE][flags]
        Assert.Equal(1 + 10 + 1 + 4 + 1 + 0 + 8 + 1, o.Length);
        Assert.Equal(4, o[11]); Assert.Equal((byte)'H', o[12]); Assert.Equal(0, o[16]);
        Assert.Equal(0x01, o[17]); Assert.Equal(0x08, o[24]); Assert.Equal(1, o[25]);
        Assert.Throws<ArgumentException>(() => Wo143Codec.OneShot("x", "", "", 0, 0));
        Assert.Throws<ArgumentException>(() => Wo143Codec.Hands("x", new byte[15], new byte[16]));
    }

    // ---------------------------------------------------------------- when each piece runs

    [Fact]
    public void The_masks_follow_every_switch_and_the_role()
    {
        var all = Wo143Rules.Settings.AllOn;
        Assert.Equal((0, 0), Wo143Rules.Masks(all, false, true, false));   // activities off: nothing (the hands ride its apply)
        var host = Wo143Rules.Masks(all, true, true, false);
        Assert.Equal(Wo143Rules.BitHands | Wo143Rules.BitGaits | Wo143Rules.BitOneShots | Wo143Rules.BitLooks, host.Capture);
        Assert.Equal(Wo143Rules.BitAvatarShots, host.Apply);                  // the host shows the joiner's minigames on its avatar
        var joiner = Wo143Rules.Masks(all, true, false, true);
        Assert.Equal(0, joiner.Capture);
        Assert.Equal(Wo143Rules.BitHands | Wo143Rules.BitGaits | Wo143Rules.BitOneShots | Wo143Rules.BitAvatarShots, joiner.Apply);
        Assert.Equal(0, joiner.Apply & Wo143Rules.BitLooks);                  // looks are the mod's (the actor's forced look)

        Assert.Equal(0, Wo143Rules.Masks(all with { Hands = false }, true, false, true).Apply & Wo143Rules.BitHands);
        Assert.Equal(0, Wo143Rules.Masks(all with { Gaits = false }, true, true, false).Capture & Wo143Rules.BitGaits);
        Assert.Equal(0, Wo143Rules.Masks(all with { OneShots = false }, true, false, true).Apply & Wo143Rules.BitOneShots);
        Assert.Equal(0, Wo143Rules.Masks(all with { Minigames = false }, true, true, false).Apply);
        Assert.Equal(0, Wo143Rules.Masks(all with { Idles = false }, true, true, false).Capture & Wo143Rules.BitLooks);
        var none = new Wo143Rules.Settings(false, false, false, false, false);
        Assert.Equal((0, 0), Wo143Rules.Masks(none, true, true, false));
        Assert.Equal((0, 0), Wo143Rules.Masks(all, true, false, false));   // no partner, not the shared world
        Assert.True(GameBridge.W143Default);
    }

    // ---------------------------------------------------------------- the players' minigames on the avatar

    [Fact]
    public void Each_minigame_plays_its_own_loop_with_the_tag_it_needs()
    {
        Assert.Equal(("SharpeningMinigame", "sword"), Wo143Rules.AvatarLoop(Minigame(1)));
        Assert.Equal(("ReadingBook", "book"), Wo143Rules.AvatarLoop(Minigame(2)));
        Assert.Equal(("ReadingBook", "sittingNoTable+book"), Wo143Rules.AvatarLoop(Minigame(2, ActivityState.Sitting)));   // read seated: WO-141's seat stays
        Assert.Equal(("AlchemyIdle", ""), Wo143Rules.AvatarLoop(Minigame(3)));
        Assert.Equal(("PickingHerbs", ""), Wo143Rules.AvatarLoop(Minigame(4)));
        Assert.Equal(("LockpickingIdle", ""), Wo143Rules.AvatarLoop(Minigame(5)));
        Assert.Equal(("Digging", ""), Wo143Rules.AvatarLoop(Minigame(6)));
        Assert.Equal(("DiceGameIdle", "sitting"), Wo143Rules.AvatarLoop(Minigame(7, ActivityState.Sitting)));
        Assert.Equal(("BlacksmithingAnvilIdle", "sword"), Wo143Rules.AvatarLoop(Minigame(12)));
        // stone throwing is refused by the game on an avatar (H1); the rest have no body loop: the avatar stands
        foreach (byte none in new byte[] { 0, 8, 9, 10, 11, 13, ActivityState.NoMinigame }) Assert.Null(Wo143Rules.AvatarLoop(Minigame(none)));
        Assert.Equal("grindstone", Wo143Rules.MinigameName(1));
        Assert.Equal("none", Wo143Rules.MinigameName(ActivityState.NoMinigame));
        Assert.Null(Wo143Rules.AvatarLoop(ActivityState.None));
    }

    [Fact]
    public void A_minigame_starts_where_the_game_starts_the_player_at_the_same_object()
    {
        var grind = Wo143Rules.ShowFor(1, false)!;
        // with the grindstone named in the row: its aligned entry at it (the game puts the avatar on the seat)
        Assert.Equal(("SharpeningMinigameIn", "sword", true, Wo143Rules.PhaseEntry), Wo143Rules.FirstStep(grind, 0xC0FFEE));
        // without an object: the loop where the avatar stands (the entry's alignment has nothing to align to)
        Assert.Equal(("SharpeningMinigame", "sword", false, Wo143Rules.PhaseLoop), Wo143Rules.FirstStep(grind, 0));
        // reading's entry is not aligned: played where he stands, with or without an object
        Assert.Equal(("ReadingBookIn", "book", false, Wo143Rules.PhaseEntry), Wo143Rules.FirstStep(Wo143Rules.ShowFor(2, false)!, 0xC0FFEE));
        // alchemy has no entry but its loop is aligned: the loop at the table
        Assert.Equal(("AlchemyIdle", "", true, Wo143Rules.PhaseLoop), Wo143Rules.FirstStep(Wo143Rules.ShowFor(3, false)!, 0xC0FFEE));
        Assert.Equal(("AlchemyIdle", "", false, Wo143Rules.PhaseLoop), Wo143Rules.FirstStep(Wo143Rules.ShowFor(3, false)!, 0));
        Assert.Equal(("BlacksmithingToAnvil", "sword+forgeBag", true, Wo143Rules.PhaseEntry), Wo143Rules.FirstStep(Wo143Rules.ShowFor(12, false)!, 0xC0FFEE));
        Assert.Equal("SharpeningMinigameOut", grind.Out);
    }

    [Fact]
    public void After_each_step_the_game_decides_and_a_refusal_falls_back_quietly()
    {
        const int E = Wo143Rules.PhaseEntry, L = Wo143Rules.PhaseLoop, O = Wo143Rules.PhaseOut;
        Assert.Equal(("loop", L), Wo143Rules.AfterStep(E, Wo143Rules.ResultDone, true, 2000));          // seated by the entry: now the loop
        Assert.Equal(("loop", L), Wo143Rules.AfterStep(L, Wo143Rules.ResultDone, false, 4800));         // reading's loop ended by itself: again
        Assert.Equal(("stand", Wo143Rules.PhaseNone), Wo143Rules.AfterStep(L, Wo143Rules.ResultDone, false, 62));   // the game does not hold it
        Assert.Equal(("none", L), Wo143Rules.AfterStep(L, Wo143Rules.ResultInterrupted, false, 6000));  // a newer request took over
        Assert.Equal(("loop-in-place", L), Wo143Rules.AfterStep(E, Wo143Rules.ResultFailed, true, 10));  // the aligned entry refused
        Assert.Equal(("loop-in-place", L), Wo143Rules.AfterStep(E, Wo143Rules.ResultMovedAway, true, 900));   // it carried him away: undone
        Assert.Equal(("stand", Wo143Rules.PhaseNone), Wo143Rules.AfterStep(L, Wo143Rules.ResultFailed, false, 0));   // refused in place: stand
        Assert.Equal(("release", Wo143Rules.PhaseNone), Wo143Rules.AfterStep(O, Wo143Rules.ResultDone, false, 2000));
    }

    // ---------------------------------------------------------------- who a copy looks at here

    [Fact]
    public void A_look_target_is_named_the_way_this_machine_has_it()
    {
        const byte host = 0, me = 1;
        Assert.Equal("kcd2mp_0", Wo143Rules.LookTargetHere(Protocol.LookHostPlayer, "", host, me));   // the host's Henry is his avatar here
        Assert.Equal("player", Wo143Rules.LookTargetHere(Protocol.LookPeer, "1", host, me));        // at me: my own player
        Assert.Equal("kcd2mp_2", Wo143Rules.LookTargetHere(Protocol.LookPeer, "2", host, me));      // at another joiner: his avatar here
        Assert.Equal("ttkc_inkeeper", Wo143Rules.LookTargetHere(Protocol.LookNpc, "ttkc_inkeeper", host, me));
        Assert.Null(Wo143Rules.LookTargetHere(Protocol.LookNobody, "", host, me));
        Assert.Null(Wo143Rules.LookTargetHere(Protocol.LookPeer, "x", host, me));
        Assert.Null(Wo143Rules.LookTargetHere(Protocol.LookNpc, "", host, me));
    }

    [Fact]
    public void A_blocked_copy_plays_no_one_shot_and_forces_no_look()
    {
        Assert.False(Wo143Rules.Quiet(null));
        foreach (var why in new[] { "fight", "talk", "down" }) Assert.True(Wo143Rules.Quiet(why));
        Assert.Equal("fight", Wo141Rules.BlockReason(true, false, false));   // the same reasons WO-141's leave-first uses
    }

    // ---------------------------------------------------------------- temporary tools

    [Fact]
    public void A_temporary_tool_goes_once_the_hands_no_longer_want_it()
    {
        var temps = new[] { Hoe, Saw };
        Assert.Equal(new[] { Saw }, Wo143Rules.TempsToRelease(temps, ExtraRow.ClassBytes(Hoe), new byte[16]));   // the hoe stays
        Assert.Equal(new[] { Hoe, Saw }, Wo143Rules.TempsToRelease(temps, new byte[16], new byte[16]));          // empty hands: both go
        Assert.Empty(Wo143Rules.TempsToRelease(temps, ExtraRow.ClassBytes(Hoe), ExtraRow.ClassBytes(Saw)));
        Assert.Empty(Wo143Rules.TempsToRelease(Array.Empty<string>(), new byte[16], new byte[16]));
    }
}
