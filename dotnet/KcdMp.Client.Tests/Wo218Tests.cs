// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using KcdMp.Client;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-218: the pipe's batch and the queue's pressure -- the agent's pure rules, each with the case that must not pass.</summary>
public class Wo218Tests
{
    [Fact]
    public void Every_switch_ships_on_and_the_twin_names_turn_one_off()
    {
        Assert.All(W218Switch.Names, n => Assert.False(W218Switch.TwinOff(null, n)));
        Assert.True(W218Switch.TwinOff("pipe_batch", "pipe_batch"));
        Assert.False(W218Switch.TwinOff("pipe_batch", "pipe_shed"));
        Assert.All(W218Switch.Names, n => Assert.True(W218Switch.TwinOff("all", n)));
        Assert.False(W218Switch.TwinOff("route_skip", "pipe_batch"));   // WO-217's names are not WO-218's
        Assert.DoesNotContain("pipe_batch", W218Switch.NativeNames);     // the agent's own: the DLL serves a batch whenever one comes
    }

    [Fact]
    public void A_batch_reply_splits_into_its_sub_replies_in_order_and_only_for_its_own_id()
    {
        // [ok 1][id 0x0102][n 2] (0xAC len 5 [1 0 133 0 9]) (0xA6 len 4 [1 0 2 0])
        byte[] r = [1, 0x02, 0x01, 2, 0xAC, 5, 0, 1, 0, 133, 0, 9, 0xA6, 4, 0, 1, 0, 2, 0];
        var s = Wo218Rules.SplitReply(r, 0x0102);
        Assert.NotNull(s);
        Assert.Equal(2, s!.Count);
        Assert.Equal(0xAC, s[0].Type);
        Assert.Equal(new byte[] { 1, 0, 133, 0, 9 }, s[0].Body);
        Assert.Equal(0xA6, s[1].Type);
        Assert.Null(Wo218Rules.SplitReply(r, 0x0103));                         // another batch's answer
        Assert.Null(Wo218Rules.SplitReply([0, 0x02, 0x01, 0], 0x0102));        // the DLL refused it (malformed)
        Assert.Null(Wo218Rules.SplitReply(r[..^1], 0x0102));                   // cut short
        Assert.Null(Wo218Rules.SplitReply([.. r, 7], 0x0102));                 // trailing bytes
    }

    static byte[] State(uint eid, byte cm, byte fill)
    {
        var rec = new byte[6 + Protocol.NpcAnimScopeLen];
        BitConverter.GetBytes(eid).CopyTo(rec, 0);
        rec[4] = cm; rec[5] = 1;
        for (int i = 6; i < rec.Length; i++) rec[i] = fill;
        return rec;
    }

    static byte[] Post(params byte[][] recs) => [Protocol.Wo208OpStreamIn, (byte)recs.Length, .. recs.SelectMany(x => x)];

    [Fact]
    public void Two_waiting_npc_state_posts_merge_into_each_bodys_newest_state()
    {
        var older = Post(State(1, 0, 0x11), State(2, 0, 0x22));
        var newer = Post(State(2, 1, 0x99), State(3, 0, 0x33));
        var m = Wo218Rules.MergeStreamIn(older, newer);
        Assert.NotNull(m);
        var st = Wo218Rules.ReadStates(m!);
        Assert.NotNull(st);
        Assert.Equal(new uint[] { 1, 2, 3 }, st!.Select(x => x.Eid).ToArray());   // the older order, the new body after
        Assert.Equal(0x99, st[1].Rec[6]);                                          // body 2: the newer state wins
        Assert.Equal(1, st[1].Rec[4]);
        Assert.Equal(Protocol.Wo208OpStreamIn, m![0]);
        Assert.Null(Wo218Rules.MergeStreamIn(older, newer[..^1]));                 // a malformed post is never merged
    }

    static byte[] Anim(params (string Name, int Scopes)[] bodies)
    {
        var ms = new MemoryStream();
        ms.WriteByte((byte)bodies.Length);
        foreach (var (n, sc) in bodies)
        {
            ms.WriteByte((byte)n.Length); ms.Write(Encoding.ASCII.GetBytes(n)); ms.WriteByte(0); ms.WriteByte((byte)sc);
            ms.Write(new byte[sc * Protocol.NpcAnimScopeLen]);
        }
        return ms.ToArray();
    }

    [Fact]
    public void A_shed_frame_keeps_only_the_bodies_it_keeps_and_never_rewrites_a_frame_it_keeps_whole()
    {
        var a = Anim(("near_1", 2), ("far_1", 1), ("far_2", 0));
        Assert.Equal(new[] { "near_1", "far_1", "far_2" }, Wo218Rules.NpcAnimNames(a));
        var f = Wo218Rules.FilterNpcAnim(a, n => n.StartsWith("near"));
        Assert.NotNull(f);
        Assert.Equal(new[] { "near_1" }, Wo218Rules.NpcAnimNames(f!));
        Assert.Null(Wo218Rules.FilterNpcAnim(a, _ => true));        // nothing shed: the frame as it came
        Assert.Empty(Wo218Rules.NpcAnimNames(a[..^1]));               // malformed: no names
    }

    [Fact]
    public void The_queue_sheds_only_superseded_far_state_first_and_never_near_bodies()
    {
        Assert.Equal(0, Wo218Rules.ShedLevel(500));
        Assert.Equal(1, Wo218Rules.ShedLevel(501));
        Assert.Equal(2, Wo218Rules.ShedLevel(1001));
        Assert.Equal(3, Wo218Rules.ShedLevel(2001));
        // level 0: nothing, however far or superseded
        Assert.Equal(Wo218Rules.Shed.Keep, Wo218Rules.ShedVerdict(0, 200, true, 0));
        // level 1: a superseded far state only; the newest far state and every near state go on
        Assert.Equal(Wo218Rules.Shed.Superseded, Wo218Rules.ShedVerdict(1, 41, true, 0));
        Assert.Equal(Wo218Rules.Shed.Keep, Wo218Rules.ShedVerdict(1, 41, false, 0));
        Assert.Equal(Wo218Rules.Shed.Keep, Wo218Rules.ShedVerdict(1, 39, true, 0));
        // level 2: a far body at most once a second
        Assert.Equal(Wo218Rules.Shed.FarRate, Wo218Rules.ShedVerdict(2, 60, false, 500));
        Assert.Equal(Wo218Rules.Shed.Keep, Wo218Rules.ShedVerdict(2, 60, false, 1500));
        Assert.Equal(Wo218Rules.Shed.Keep, Wo218Rules.ShedVerdict(2, 30, true, 0));
        // level 3: the 20-40 m band too; under 20 m never
        Assert.Equal(Wo218Rules.Shed.Mid, Wo218Rules.ShedVerdict(3, 30, true, 5000));
        Assert.Equal(Wo218Rules.Shed.Keep, Wo218Rules.ShedVerdict(3, 19, true, 0));
        // a body with no fresh position (its stream older than 10 s) counts as far: only its superseded state is shed at level 1
        Assert.Equal(Wo218Rules.Shed.Superseded, Wo218Rules.ShedVerdict(1, double.PositiveInfinity, true, 0));
        Assert.Equal(Wo218Rules.Shed.Keep, Wo218Rules.ShedVerdict(1, double.PositiveInfinity, false, 0));
    }

    static Wo218Rules.ActUnit Row(string name, byte peer = 0, byte kind = Protocol.ActivityKindNpc) =>
        new(Protocol.ActivityHostDown, 1, kind, name, peer, new ActivityRow(peer, name, default), null, 0);

    static Wo218Rules.ActUnit Extra(string name, byte kind, string frag = "") =>
        new(Protocol.ActivityExtraDown, 1, kind, name, 0, null, new ExtraRow { Name = name, Fragment = frag }, 0);

    [Fact]
    public void Waiting_activity_rows_keep_each_bodys_newest_state_and_every_one_shot()
    {
        var units = new List<Wo218Rules.ActUnit>
        {
            Row("smith"), Extra("smith", Protocol.ExtraKindHands), Extra("smith", Protocol.ExtraKindOneShot, "a"),
            Row("cook"), Row("smith"), Extra("smith", Protocol.ExtraKindOneShot, "b"), Extra("smith", Protocol.ExtraKindHands),
            Row("", 3, Protocol.ActivityKindPlayer), Row("", 3, Protocol.ActivityKindPlayer),
        };
        var c = Wo218Rules.CoalesceActivity(units);
        Assert.Equal(6, c.Count);   // smith row, smith hands, shot a, cook row, shot b, the player's row
        Assert.Equal(2, c.Count(u => !u.IsState));
        Assert.Equal(new[] { "a", "b" }, c.Where(u => !u.IsState).Select(u => u.Extra!.Fragment).ToArray());
        Assert.Single(c, u => u.Body == "peer:3");
        Assert.Same(units[6].Extra, c[1].Extra);   // the newest hands row, at the first one's place
    }

    [Fact]
    public void A_bodys_queue_replaces_its_waiting_state_row_of_the_same_kind_and_never_a_one_shot()
    {
        var pending = new List<Wo218Rules.ActUnit> { Row("smith"), Extra("smith", Protocol.ExtraKindHands), Extra("smith", Protocol.ExtraKindOneShot, "a") };
        Assert.Equal(0, Wo218Rules.ReplaceAt(pending, Row("smith")));                                     // the newer activity row
        Assert.Equal(1, Wo218Rules.ReplaceAt(pending, Extra("smith", Protocol.ExtraKindHands)));          // the newer hands
        Assert.Equal(-1, Wo218Rules.ReplaceAt(pending, Extra("smith", Protocol.ExtraKindOneShot, "b")));  // a one-shot waits in order
        Assert.Equal(-1, Wo218Rules.ReplaceAt(pending, Extra("smith", Protocol.ExtraKindGaits)));         // a kind not waiting: appended
    }
}
