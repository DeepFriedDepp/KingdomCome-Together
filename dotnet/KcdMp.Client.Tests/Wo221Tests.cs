// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

public class Wo221Tests
{
    [Fact]
    public void Keepers_parse_only_names_and_ids()
    {
        var p = Wo221Rules.ParseKeepers("ttkc_man_11=520, ttkc_baska=503,bad name=1,x=,=5,y=-3,z=1000000,ttkc_man_11=7");
        Assert.Equal(2, p.Count);
        Assert.Equal("ttkc_man_11", p[0].Key); Assert.Equal(520, p[0].Value);
        Assert.Equal("ttkc_baska", p[1].Key); Assert.Equal(503, p[1].Value);
        Assert.Empty(Wo221Rules.ParseKeepers("-"));
        Assert.Empty(Wo221Rules.ParseKeepers(null));
    }

    [Fact]
    public void Keepers_texts_round_trip_in_parts()
    {
        var pairs = Enumerable.Range(0, 120).Select(i => new KeyValuePair<string, int>($"ttkc_keeper_number_{i:D3}", 500 + i)).ToList();
        var texts = Wo221Rules.KeepersTexts(pairs, 300);
        Assert.True(texts.Count > 1);
        var back = new List<KeyValuePair<string, int>>();
        for (int i = 0; i < texts.Count; i++)
        {
            Assert.True(Wo221Rules.TryParseKeepersPart(texts[i], out int part, out int nparts, out var ps));
            Assert.Equal(i + 1, part); Assert.Equal(texts.Count, nparts);
            Assert.All(texts[i].Split(' ', 3)[2], c => Assert.True(c != ' '));
            back.AddRange(ps);
        }
        Assert.Equal(Wo221Rules.KeepersCsv(pairs), Wo221Rules.KeepersCsv(back));
        Assert.Equal(new[] { "1 1 -" }, Wo221Rules.KeepersTexts(new List<KeyValuePair<string, int>>()));
        Assert.True(Wo221Rules.TryParseKeepersPart("1 1 -", out _, out _, out var none)); Assert.Empty(none);
        Assert.False(Wo221Rules.TryParseKeepersPart("2 1 a=1", out _, out _, out _));
        Assert.False(Wo221Rules.TryParseKeepersPart("1 1 nothing-here", out _, out _, out _));
    }

    [Fact]
    public void Shop_op_bodies()
    {
        Assert.Equal(new byte[] { 0x08, 0x02, 0, 0, 3, (byte)'a', (byte)'b', (byte)'c' }, Wo221Rules.OpenBody(520, "abc"));
        Assert.Equal(new byte[] { 2, (byte)'h', (byte)'i' }, Wo221Rules.CloseBody("hi"));
    }

    [Fact]
    public void Switch_names_and_twin()
    {
        Assert.Contains("combat_action_end", W221Switch.NativeNames);
        Assert.Contains("shop_keeper", W221Switch.AgentNames);
        Assert.True(W221Switch.TwinOff("all", "shop_keeper"));
        Assert.True(W221Switch.TwinOff("drive, shop_keeper", "shop_keeper"));
        Assert.False(W221Switch.TwinOff("drive", "shop_keeper"));
        Assert.Equal(0b0101u, W221Switch.NativeBits(n => n is "combat_action_end" or "drive"));
    }
}
