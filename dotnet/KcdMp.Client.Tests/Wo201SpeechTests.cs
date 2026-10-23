// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-201 Act V F1: the DLL's line-capture reply (the broadcast and the F2 vote panel are retired by WO-204 III.2).</summary>
public class Wo201SpeechTests
{
    [Fact]
    public void ThePollReply()
    {
        var b = new List<byte> { 2 };
        void Line(uint eid, float dur, byte flags, float dist, ushort age, string path, string text, string name)
        {
            b.AddRange(BitConverter.GetBytes(eid)); b.AddRange(BitConverter.GetBytes(dur)); b.Add(flags); b.AddRange(BitConverter.GetBytes(dist));
            b.AddRange(BitConverter.GetBytes(age)); b.Add((byte)path.Length); b.AddRange(Encoding.UTF8.GetBytes(path));
            b.Add((byte)text.Length); b.AddRange(Encoding.UTF8.GetBytes(text)); b.Add((byte)name.Length); b.AddRange(Encoding.UTF8.GetBytes(name));
        }
        Line(0x7777, 2.5f, 1, 4.0f, 30, "Localization/dialog/a/b/dude_x.ogg", "Hello.", "Dude");
        Line(0x2F1A4, 3.0f, 0, 12.5f, 60, "Localization/dialog/a/b/sems_y.ogg", "", "tsem_seminsr");
        var l = Wo201Rules.ParseSpeechPoll(b.ToArray());
        Assert.Equal(2, l.Count);
        Assert.True(l[0].Eid == 0x7777 && l[0].FromDialogue && l[0].Text == "Hello." && l[0].Name == "Dude");
        Assert.True(l[1].Name == "tsem_seminsr" && !l[1].FromDialogue && Math.Abs(l[1].Dist - 12.5f) < 0.01f && l[1].AgeMs == 60);
        Assert.Empty(Wo201Rules.ParseSpeechPoll(new byte[] { 1, 2, 3 }));   // a short reply: nothing, never a throw
    }

    [Fact]
    public void B11_TheRangedSteps()
    {
        Assert.Equal("Fire", Wo201Rules.RangedFragment(3));
        Assert.Equal("Load", Wo201Rules.RangedFragment(4));
        Assert.Equal("", Wo201Rules.RangedFragment(2));   // the aim hold needs the shooting's own tags: not shown
        Assert.Equal("l_crossbow+medium+goatsfoot", Wo201Rules.RangedTags(4, false));
        Assert.Equal("l_bow", Wo201Rules.RangedTags(3, true));
        Assert.Equal("ranged-step", Protocol.W164KindName(Protocol.W164RangedStep));
    }
}
