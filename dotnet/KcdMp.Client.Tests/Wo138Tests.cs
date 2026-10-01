// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Text;

namespace KcdMp.Client.Tests;

/// <summary>WO-138: the native NPC sender's wire, the pause announcement, hold versus lost, the levers (docs/WO-138-findings.md).</summary>
public class Wo138Tests
{
    // ---- the native sender ----------------------------------------------------

    private static byte[] StreamFrame(params (byte Flags, float X, float Y, float Z, float Rot, float Hp, string Name)[] rows)
    {
        var ms = new MemoryStream();
        ms.WriteByte((byte)rows.Length);
        Span<byte> f = stackalloc byte[4];
        foreach (var r in rows)
        {
            ms.WriteByte(r.Flags);
            foreach (var v in new[] { r.X, r.Y, r.Z, r.Rot, r.Hp }) { BinaryPrimitives.WriteSingleLittleEndian(f, v); ms.Write(f); }
            var n = Encoding.ASCII.GetBytes(r.Name);
            ms.WriteByte((byte)n.Length); ms.Write(n);
        }
        return ms.ToArray();
    }

    [Fact]
    public void Stream_frame_rows_decode_in_the_DLLs_order()
    {
        var b = StreamFrame((0x04 | 0x20, 1760.55f, 1982.15f, 41.75f, -1.811f, 100f, "tzel_olbram"), (0x80, 1f, 2f, 3f, 0f, 55.5f, "horse_1"));
        Assert.True(Wo138Codec.TryParseStream(b, out var rows));
        Assert.Equal(2, rows.Count);
        Assert.Equal(new Wo138Row(0x24, 1760.55f, 1982.15f, 41.75f, -1.811f, 100f, "tzel_olbram"), rows[0]);
        Assert.Equal("horse_1", rows[1].Name);
        Assert.Equal(0x80, rows[1].Flags);
    }

    [Fact]
    public void Stream_frame_rejects_truncation_trailing_bytes_and_bad_names()
    {
        var b = StreamFrame((0, 1, 2, 3, 4, 5, "tzel_man_5"));
        Assert.False(Wo138Codec.TryParseStream(b.AsSpan(0, b.Length - 1), out _));
        Assert.False(Wo138Codec.TryParseStream([.. b, 0], out _));
        var zeroName = (byte[])b.Clone(); zeroName[1 + 21] = 0;
        Assert.False(Wo138Codec.TryParseStream(zeroName, out _));
        Assert.True(Wo138Codec.TryParseStream([0], out var none));
        Assert.Empty(none);
        Assert.False(Wo138Codec.TryParseStream([], out _));
    }

    [Fact]
    public void Config_body_is_the_15_bytes_native_op_1_reads()
    {
        var b = Wo138Codec.ConfigBody(true, new Wo138Cfg(100, 2000, 50, true, 60, 150, 12), 200);
        Assert.Equal(15, b.Length);
        Assert.Equal(1, b[0]);
        Assert.Equal(100, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(1)));
        Assert.Equal(2000, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(3)));
        Assert.Equal(50, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(5)));
        Assert.Equal(1, b[7]);
        Assert.Equal(60, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(8)));
        Assert.Equal(150, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(10)));
        Assert.Equal(12, b[12]);
        Assert.Equal(200, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(13)));
        Assert.Equal(0, Wo138Codec.ConfigBody(false, Wo138Cfg.Default)[0]);
    }

    [Fact]
    public void Track_bodies_split_one_generation_and_keep_every_name()
    {
        var names = Enumerable.Range(1, 150).Select(i => ($"tzel_extra_man_{i:000}", (byte)(i % 7 == 0 ? 1 : 0))).ToList();
        var bodies = Wo138Codec.TrackBodies(42, names, 900);
        Assert.True(bodies.Count > 1);
        var got = new List<(string, byte)>();
        for (int p = 0; p < bodies.Count; p++)
        {
            var b = bodies[p];
            Assert.True(b.Length <= 900);
            Assert.Equal(42, BinaryPrimitives.ReadUInt16LittleEndian(b));
            Assert.Equal(p, b[2]);
            Assert.Equal(bodies.Count, b[3]);
            int o = 5;
            for (int i = 0; i < b[4]; i++) { byte fl = b[o]; int nl = b[o + 1]; got.Add((Encoding.ASCII.GetString(b, o + 2, nl), fl)); o += 2 + nl; }
            Assert.Equal(b.Length, o);
        }
        Assert.Equal(names, got);
        var empty = Wo138Codec.TrackBodies(1, []);
        Assert.Single(empty);
        Assert.Equal(new byte[] { 1, 0, 0, 1, 0 }, empty[0]);
    }

    [Fact]
    public void Track_line_parses_parts_flags_and_the_empty_set()
    {
        Assert.True(Wo138Codec.TryParseTrack("7 0 2 horse_tzel_1:1,tzel_maid:0", out ushort g, out int part, out int parts, out var names));
        Assert.Equal((ushort)7, g); Assert.Equal(0, part); Assert.Equal(2, parts);
        Assert.Equal([("horse_tzel_1", (byte)1), ("tzel_maid", (byte)0)], names);
        Assert.True(Wo138Codec.TryParseTrack("8 0 1 -", out _, out _, out _, out var none));
        Assert.Empty(none);
        Assert.False(Wo138Codec.TryParseTrack("8 1 1 a:0", out _, out _, out _, out _));      // part past parts
        Assert.False(Wo138Codec.TryParseTrack("8 0 1 bad name:0", out _, out _, out _, out _)); // a space in a name
        Assert.False(Wo138Codec.TryParseTrack("8 0 1 ev'il:0", out _, out _, out _, out _));
        Assert.False(Wo138Codec.TryParseTrack("x 0 1 a:0", out _, out _, out _, out _));
    }

    [Fact]
    public void Cfg_line_parses_the_mods_settings()
    {
        Assert.True(Wo138Codec.TryParseCfg("100 2000 50 1 60 150 12", out var c));
        Assert.Equal(new Wo138Cfg(100, 2000, 50, true, 60, 150, 12), c);
        Assert.False(Wo138Codec.TryParseCfg("5 2000 50 1 60 150 12", out _));   // emit < 20 ms
        Assert.False(Wo138Codec.TryParseCfg("100 2000 50 1 60", out _));
    }

    [Fact]
    public void Status_decodes_the_native_48_byte_struct()
    {
        var b = new byte[48];
        b[0] = 1; b[1] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), 11);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), 10);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), 31);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), 3454);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), 1178);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(16), 39);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(18), 42);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(20), 157);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(22), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(24), 74);
        b[26] = 1; b[27] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(32), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(36), 0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(38), 4);
        b[40] = 1; b[41] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(42), 18);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(44), 99999);
        Assert.True(Wo138Codec.TryParseStatus(b, out var s));
        Assert.True(s.On); Assert.Equal(2, s.World); Assert.Equal(11, s.Tracked); Assert.Equal(10, s.Resolved);
        Assert.Equal(31, s.RowsLastSec); Assert.Equal(3454u, s.RowsTotal); Assert.Equal(1178u, s.Ticks);
        Assert.Equal(39, s.LastTickAgeMs); Assert.Equal(42, s.CostP50Us); Assert.Equal(157, s.CostMaxUs);
        Assert.Equal(74, s.Fps); Assert.True(s.GateArmed); Assert.False(s.GateOn); Assert.Equal(3u, s.Declined);
        Assert.Equal(9u, s.PauseCalls); Assert.Equal(0x10, s.HeldMask); Assert.Equal(4, s.LastSource);
        Assert.True(s.LastPause); Assert.True(s.HoldAll); Assert.Equal(18, s.AttrP50Us); Assert.Equal(99999u, s.Frames);
        Assert.True(s.Sending);
        Assert.False((s with { LastTickAgeMs = 1500 }).Sending);   // on, but its tick is not running
        Assert.False((s with { On = false }).Sending);
        Assert.False(Wo138Codec.TryParseStatus(b.AsSpan(0, 47), out _));
    }

    [Fact]
    public void World_frame_decodes_and_frozen_is_world_2()
    {
        Assert.True(Wo138Codec.TryParseWorld([2, 0, 0, 0x10, 0], out var w));
        Assert.True(w.Frozen); Assert.Equal(0x10, w.HeldMask);
        Assert.True(Wo138Codec.TryParseWorld([1, 1, 0, 0xFF, 0xFF], out var s));
        Assert.False(s.Frozen);
        Assert.False(Wo138Codec.TryParseWorld([3, 0, 0, 0, 0], out _));
        Assert.False(Wo138Codec.TryParseWorld([0, 0], out _));
    }

    [Fact]
    public void Lua_npc_state_lines_are_dropped_only_while_the_DLL_streams_and_never_a_resync()
    {
        Assert.False(Wo138Codec.DropLuaLine(false, 0));
        Assert.True(Wo138Codec.DropLuaLine(true, 0));
        Assert.True(Wo138Codec.DropLuaLine(true, 0x01 | 0x20));
        Assert.False(Wo138Codec.DropLuaLine(true, 0x40));          // the resync burst passes
        Assert.False(Wo138Codec.DropLuaLine(true, 0x40 | 0x01));
    }

    // ---- the pause announcement ------------------------------------------------

    [Fact]
    public void Pause_state_byte_is_zero_when_running_and_the_reasons_otherwise()
    {
        Assert.Equal(0, Wo138Codec.Reasons(false, false, false, false, false, false, false));
        Assert.Equal(Wo138Codec.ReasonInventory, Wo138Codec.Reasons(false, true, false, false, false, false, false));
        byte all = Wo138Codec.Reasons(true, true, true, true, true, true, true);
        Assert.Equal(0x7F, all);
        Assert.Equal("menu+inventory+dialogue+cutscene+load+skip-time+frozen", Wo138Codec.Describe(all));
        Assert.Equal("running", Wo138Codec.Describe(0));
        // An old receiver's "entered" (1) is the menu bit: the same byte means the same thing.
        Assert.Equal(KcdMp.Wire.Protocol.PauseStateEntered, Wo138Codec.ReasonMenu);
        Assert.Equal(KcdMp.Wire.Protocol.PauseStateExited, (byte)0);
    }

    // ---- hold versus lost ------------------------------------------------------

    [Fact]
    public void The_joiner_holds_while_the_host_is_paused_and_its_link_alive()
    {
        long now = 100_000;
        Assert.True(Wo138Codec.Hold(Wo138Codec.ReasonInventory, now, now - 1000, now - 30_000));   // 30 s inventory, link 1 s old
        Assert.True(Wo138Codec.Hold(Wo138Codec.ReasonDialogue, now, now - 5900, now - 1000));
    }

    [Fact]
    public void The_stream_really_lost_is_not_held()
    {
        long now = 100_000;
        Assert.False(Wo138Codec.Hold(Wo138Codec.ReasonMenu, now, now - 6001, now - 10_000));   // paused, but nothing from the host for 6 s
        Assert.False(Wo138Codec.Hold(Wo138Codec.ReasonMenu, now, 0, now - 1000));             // never heard from
        Assert.False(Wo138Codec.Hold(0, now, now - 100, 0));                                  // running: the 3 s rule
        Assert.False(Wo138Codec.Hold(Wo138Codec.ReasonMenu, now, now - 100, now - 16 * 60 * 1000));   // a 16 min pause: the cap
    }

    // ---- the levers ------------------------------------------------------------

    [Fact]
    public void Levers_only_in_a_session_with_a_partner_here()
    {
        Assert.True(Wo138Codec.LeversOn(true, true, true, true));
        Assert.False(Wo138Codec.LeversOn(false, true, true, true));   // single player: no relay
        Assert.False(Wo138Codec.LeversOn(true, true, false, true));   // hosting alone, nobody joined: menus pause as usual
        Assert.False(Wo138Codec.LeversOn(true, false, true, true));   // no DLL
        Assert.False(Wo138Codec.LeversOn(true, true, true, false));   // mp_w138_levers off
    }

    [Fact]
    public void The_gate_declines_the_ESC_menu_and_a_video_and_the_levers_body_carries_the_mask()
    {
        Assert.Equal((1u << 7) | (1u << 4), Wo138Codec.DefaultMask);
        var b = Wo138Codec.LeversBody(true, Wo138Codec.DefaultMask);
        Assert.Equal(5, b.Length);
        Assert.Equal(1, b[0]);
        Assert.Equal(0x90u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(1)));
    }

    [Fact]
    public void Anchors_body_caps_at_eight()
    {
        var a = Enumerable.Range(0, 12).Select(i => ((float)i, (float)i, 0f)).ToList();
        var b = Wo138Codec.AnchorsBody(a);
        Assert.Equal(8, b[0]);
        Assert.Equal(1 + 8 * 12, b.Length);
        Assert.Equal(7f, BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(1 + 7 * 12)));
    }
}
