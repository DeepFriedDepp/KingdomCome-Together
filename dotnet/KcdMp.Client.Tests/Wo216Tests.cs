// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using KcdMp.Client;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-216: the agent's pure rules, the switch word and the route line on the state lane, each check with the case that must not pass.</summary>
public class Wo216Tests
{
    [Fact]
    public void Every_switch_ships_on_and_the_twin_names_turn_one_off()
    {
        Assert.All(W216Switch.Names, n => Assert.False(W216Switch.TwinOff(null, n)));
        Assert.True(W216Switch.TwinOff("route_replay", "route_replay"));
        Assert.False(W216Switch.TwinOff("route_replay", "no_brains"));
        Assert.All(W216Switch.Names, n => Assert.True(W216Switch.TwinOff("all", n)));
        Assert.False(W216Switch.TwinOff("route", "route_replay"));   // WO-215's names are not WO-216's
    }

    [Fact]
    public void The_native_word_has_the_dlls_bit_order()
    {
        // wo216_rules.h: route_replay 1, no_brains 2, activity_line 4, outfits 8, riders 16, poses 32, npc_fights 64
        Assert.Equal(0x7Fu, W216Switch.NativeBits(_ => true));
        Assert.Equal(0x01u, W216Switch.NativeBits(n => n == "route_replay"));
        Assert.Equal(0x02u, W216Switch.NativeBits(n => n == "no_brains"));
        Assert.Equal(0x40u, W216Switch.NativeBits(n => n == "npc_fights"));
    }

    private static byte[] Rec(uint reqId, byte speed)
    {
        var r = new byte[Protocol.NpcRouteRecLen];
        BitConverter.GetBytes(reqId).CopyTo(r, 0);
        r[4] = speed; r[5] = 0x05;
        return r;
    }

    private static byte[] Body(params (string Name, byte[] Rec)[] rows)
    {
        var b = new List<byte> { (byte)rows.Length };
        foreach (var (n, rec) in rows) { b.Add((byte)n.Length); b.AddRange(Encoding.UTF8.GetBytes(n)); b.AddRange(rec); }
        return [.. b];
    }

    [Fact]
    public void A_route_body_is_rekeyed_by_this_worlds_entity_ids()
    {
        var body = Body(("ttkc_man_1", Rec(7, 1)), ("nobody_here", Rec(8, 3)), ("ttkc_woman_2", Rec(9, 7)));
        var ids = new Dictionary<string, uint> { ["ttkc_man_1"] = 0x1234, ["ttkc_woman_2"] = 0x55 };
        Assert.True(Wo216Rules.TryRekeyRoute(body, n => ids.TryGetValue(n, out uint e) ? e : 0u, out var op, out int known, out int unknown));
        Assert.Equal(2, known);
        Assert.Equal(1, unknown);
        Assert.Equal(1 + 2 * (4 + Protocol.NpcRouteRecLen), op.Length);
        Assert.Equal((byte)2, op[0]);
        Assert.Equal(0x1234u, BitConverter.ToUInt32(op, 1));
        Assert.Equal(7u, BitConverter.ToUInt32(op, 5));                       // the first record follows its eid unchanged
        Assert.Equal(0x55u, BitConverter.ToUInt32(op, 1 + 4 + Protocol.NpcRouteRecLen));
        Assert.Equal((byte)7, op[1 + 4 + Protocol.NpcRouteRecLen + 4 + 4]);  // its speed
    }

    [Fact]
    public void A_route_op_goes_to_the_dll_in_pieces_under_its_pipe_limit()
    {
        var rows = Enumerable.Range(0, Protocol.NpcRouteMaxBodies).Select(i => ($"ttkc_man_{i}", Rec((uint)(100 + i), 1))).ToArray();
        Assert.True(Wo216Rules.TryRekeyRoute(Body(rows), _ => 0x77u, out var op, out int known, out _));
        Assert.Equal(Protocol.NpcRouteMaxBodies, known);
        Assert.True(1 + op.Length > Protocol.PipeMaxPayload);   // b8: one 1,346-byte frame, the connection dropped
        var pieces = Wo216Rules.RouteInPieces(op).ToList();
        Assert.Equal([16, 8], pieces.Select(p => (int)p[0]));
        Assert.All(pieces, p => Assert.True(1 + p.Length <= Protocol.PipeMaxPayload));
        Assert.All(pieces, p => Assert.Equal(1 + p[0] * (4 + Protocol.NpcRouteRecLen), p.Length));
        Assert.Equal(116u, BitConverter.ToUInt32(pieces[1], 1 + 4));   // the second piece starts at the 17th body's record
        var three = op[..(1 + 3 * (4 + Protocol.NpcRouteRecLen))];
        three[0] = 3;
        Assert.Single(Wo216Rules.RouteInPieces(three));   // a small line: one op, as before
        Assert.Empty(Wo216Rules.RouteInPieces([2, 0]));   // malformed: nothing goes
    }

    [Fact]
    public void A_malformed_route_body_is_refused()
    {
        var good = Body(("ttkc_man_1", Rec(7, 1)));
        Assert.False(Wo216Rules.TryRekeyRoute(good[..^1], _ => 1u, out _, out _, out _));          // a record short
        Assert.False(Wo216Rules.TryRekeyRoute([.. good, 0], _ => 1u, out _, out _, out _));        // a byte over
        Assert.False(Wo216Rules.TryRekeyRoute([0], _ => 1u, out _, out _, out _));                 // no body
        var bad = (byte[])good.Clone(); bad[1] = 0;                                                // an empty name
        Assert.False(Wo216Rules.TryRekeyRoute(bad, _ => 1u, out _, out _, out _));
    }

    private static byte[] AnimBody(string name, params byte[][] recs)
    {
        var b = new List<byte> { 1, (byte)name.Length };
        b.AddRange(Encoding.ASCII.GetBytes(name));
        b.Add(0); b.Add((byte)recs.Length);
        foreach (var r in recs) b.AddRange(r);
        return [.. b];
    }

    private static byte[] Rec(byte id, Action<byte[]>? fill = null)
    {
        var r = new byte[Protocol.NpcAnimScopeLen];
        r[0] = id;
        fill?.Invoke(r);
        return r;
    }

    [Fact]
    public void The_activity_line_rides_the_npc_anim_body_and_the_dll_gets_the_scopes_only()
    {
        var act = new ActivityState(ActivityState.Sitting, 0, 0x1122334455667788UL, ActivityState.NoUnstance, 0, ActivityState.NoMinigame, 0, 0);
        var body = AnimBody("ttkc_smith", Rec(0), Rec(1), Rec(Protocol.NpcAnimLineActivity, r => act.Write(r.AsSpan(1))),
                            Rec(Protocol.NpcAnimLineHands, r => { r[1] = 0xAB; r[17] = 0xCD; }));
        Assert.True(Wo216Rules.TryReadLines(body, out var lines));
        var ln = Assert.Single(lines);
        Assert.Equal("ttkc_smith", ln.Name);
        Assert.Equal(ActivityState.Sitting, ln.Activity!.Value.Stance);
        Assert.Equal(0x1122334455667788UL, ln.Activity!.Value.StanceObj);
        Assert.Equal((byte)0xAB, ln.Hands!.Left[0]);
        Assert.Equal((byte)0xCD, ln.Hands!.Right[0]);
        // the DLL's owner: two scopes, the line's records left out
        Assert.True(Wo208Rules.TryRekeyNpcAnim(body, _ => 7u, out var op, out int known, out _));
        Assert.Equal(1, known);
        Assert.Equal(2, op[1 + 4 + 1]);   // [n][eid:4][cm][nsc]
        Assert.Equal(1 + 4 + 2 + 2 * Protocol.NpcAnimScopeLen, op.Length);
        // a body without the line's records: no line, the scopes as before
        var plain = AnimBody("ttkc_man_1", Rec(0));
        Assert.True(Wo216Rules.TryReadLines(plain, out var none));
        Assert.Empty(none);
        Assert.True(Wo208Rules.TryRekeyNpcAnim(plain, _ => 7u, out var op2, out _, out _));
        Assert.Equal(1 + 4 + 2 + Protocol.NpcAnimScopeLen, op2.Length);
        // five records: refused
        Assert.False(Wo216Rules.TryReadLines(AnimBody("x", Rec(0), Rec(1), Rec(0xA1), Rec(0xA2), Rec(0xA2)), out _));
    }

    [Fact]
    public void An_exemption_parses_and_refuses_what_it_should()
    {
        Assert.True(Wo216Rules.TryParseExempt("tkop_guard_3 on 120", out var n, out bool on, out ushort s));
        Assert.Equal("tkop_guard_3", n); Assert.True(on); Assert.Equal((ushort)120, s);
        Assert.True(Wo216Rules.TryParseExempt("horse_7 off 30", out _, out bool off, out _));
        Assert.False(off);
        Assert.False(Wo216Rules.TryParseExempt("a b on 5", out _, out _, out _));         // two names
        Assert.False(Wo216Rules.TryParseExempt("x maybe 5", out _, out _, out _));        // neither on nor off
        Assert.False(Wo216Rules.TryParseExempt("x on 0", out _, out _, out _));           // no time
        Assert.False(Wo216Rules.TryParseExempt("x on 4000", out _, out _, out _));        // over an hour
        Assert.False(Wo216Rules.TryParseExempt("x;rm on 5", out _, out _, out _));        // a name the console could misread
        Assert.False(Wo216Rules.TryParseExempt(null, out _, out _, out _));
    }

    [Fact]
    public void The_route_line_is_state_on_the_npc_lane_with_its_own_kind()
    {
        Assert.True(LinkState.IsStateType(Protocol.NpcRouteUp));
        Assert.True(LinkState.IsStateType(Protocol.NpcRouteDown));
        var body = Body(("ttkc_man_1", Rec(7, 1)), ("ttkc_woman_2", Rec(9, 7)));
        var up = Protocol.BuildJoinUp(Protocol.NpcRouteUp, 2, 0, body);
        var pieces = new List<StatePiece>();
        Assert.True(LinkStateCodec.Split(up, pieces));
        Assert.Equal(2, pieces.Count);
        Assert.All(pieces, p => Assert.Equal("npc-route", p.Kind));
        Assert.Equal("ttkc_man_1", pieces[0].Body);
        Assert.Equal(1 + 10 + Protocol.NpcRouteRecLen, pieces[0].Item.Length);
        Assert.NotEqual(pieces[0].Slot, pieces[1].Slot);   // latest wins per body, not per frame
        var (items, _) = LinkStateCodec.Limits(Protocol.NpcRouteUp);
        Assert.Equal(Protocol.NpcRouteMaxBodies, items);
        Assert.Equal("npc-route", Protocol.JoinWireFor(Protocol.NpcRouteUp)!.Value.Name);
    }
}
