// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-201 Act V F1 speech_share: the rules (who sends what, the wire under 200 bytes, the DLL's poll reply).</summary>
public class Wo201SpeechTests
{
    [Fact]
    public void WhoSends()
    {
        Assert.True(Wo201Rules.SpeechSendable(true, false, false, 5, 20));    // the host's NPC chatter 5 m from the joiner
        Assert.False(Wo201Rules.SpeechSendable(true, false, false, 25, 20));  // at 25 m: none
        Assert.False(Wo201Rules.SpeechSendable(true, true, true, -1, 20));    // an unknown distance: never
        Assert.True(Wo201Rules.SpeechSendable(false, true, true, 5, 20));     // the joiner's Henry in his conversation
        Assert.True(Wo201Rules.SpeechSendable(false, false, true, 5, 20));    // the NPC in the joiner's conversation
        Assert.False(Wo201Rules.SpeechSendable(false, false, false, 5, 20));  // a copy's own chatter on the joiner: the host's to say
    }

    [Fact]
    public void TheWire_UnderTwoHundredBytes_RoundTrips()
    {
        string path = "Localization/dialog/trosecko/zacatek_jizdy/sems_s_zacatek_jizdy__semin_1_KWg9.ogg";
        string w = Wo201Rules.SpeechWire("tsem_seminsr", 3200, 120, path, new string('x', 400));
        Assert.True(Encoding.UTF8.GetByteCount(w) <= Wo201Rules.SpeechMaxBytes);
        Assert.True(Wo201Rules.TryParseSpeechWire(w, out var l));
        Assert.Equal("tsem_seminsr", l.Speaker); Assert.Equal(3200, l.DurMs); Assert.Equal(120, l.AgeMs); Assert.Equal(path, l.Path);
        string w2 = Wo201Rules.SpeechWire("@me", 1500, 0, path, "");
        Assert.True(Wo201Rules.TryParseSpeechWire(w2, out var l2) && l2.Speaker == "@me" && l2.Text == "");
        Assert.False(Wo201Rules.TryParseSpeechWire("v1|x|100|0|../../evil.ogg|", out _));          // a dialog voice file, nothing else
        Assert.False(Wo201Rules.TryParseSpeechWire("v1|x|100|0|Localization/a.wav|", out _));
        Assert.False(Wo201Rules.TryParseSpeechWire("v2|x|100|0|Localization/a.ogg|", out _));
        Assert.Equal("speech-line", Protocol.W164KindName(Protocol.W164SpeechLine));
    }

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

    [Fact]
    public void TheVote_Wire_Range_Rows()
    {
        Assert.True(Wo201Rules.VoteInRange(12, 30) && !Wo201Rules.VoteInRange(31, 30) && Wo201Rules.VoteInRange(500, 0) && !Wo201Rules.VoteInRange(-1, 30));
        Assert.Equal("Ask about the bandits", Wo201Rules.PlainOption("<font color='#ff0'>Ask about the bandits</font>"));
        string w = Wo201Rules.VoteOptionsWire(7, new[] { "Yes", "No", "Leave" });
        Assert.True(Wo201Rules.TryParseVoteOptions(w, out uint seq, out var opts) && seq == 7 && opts.Count == 3 && opts[2] == "Leave");
        Assert.True(Wo201Rules.TryParseVoteCast(Wo201Rules.VoteCastWire(7, 2, "Partner"), out uint s2, out int idx, out string voter) && s2 == 7 && idx == 2 && voter == "Partner");
        Assert.False(Wo201Rules.TryParseVoteCast("v1|7|9|Partner", out _, out _, out _));
        Assert.True(Wo201Rules.TryParseVoteClose(Wo201Rules.VoteCloseWire(7, "Yes, I'll go."), out uint s3, out string ch) && s3 == 7 && ch == "Yes, I'll go.");
        var p = new List<byte> { 0, 0, 0, 0, 0, 0, 0, 0, 2 };
        foreach (var t in new[] { "<b>One</b>", "Two" }) { p.Add(0); p.Add((byte)t.Length); p.AddRange(Encoding.UTF8.GetBytes(t)); }
        var rows = Wo201Rules.ParseVoteRows(p.ToArray(), 9);
        Assert.Equal(new[] { "One", "Two" }, rows);
        p[8] = 255;
        Assert.Empty(Wo201Rules.ParseVoteRows(p.ToArray(), 9));   // unchanged: nothing new
        Assert.Equal("vote-options", Protocol.W164KindName(Protocol.W164VoteOptions));
    }
}
