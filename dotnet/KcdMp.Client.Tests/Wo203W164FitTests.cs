// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-203 II.5: every W164 kind's builder, at its worst case, fits the relay's row -- the test that would have caught session C
/// (the WO-201 speech line, vote options, close and cast all went out longer than the old 80-byte row; the relay dropped 463 of
/// them). Run by tools\Test-RelayTable.ps1 with the relay's table gate. Also: LootMsg.BuildUp refuses a text over its row before
/// anything is sent, so an oversize text is a logged refusal on the sender, never a silent drop on the relay.
/// </summary>
public class Wo203W164FitTests
{
    private static string N(int n, char c = 'n') => new(c, n);

    /// <summary>(kind, what, text) -- each builder at the longest text its own rules let it produce.</summary>
    private static IEnumerable<(byte Kind, string What, string Text)> WorstCases()
    {
        string npc48 = N(48), name64 = N(64, 'q');
        // WO-164
        yield return (Protocol.W164MarkPing, "mark id (16)", "abcdef0123456789");
        yield return (Protocol.W164MarkPing, "mark id (new)", Wo164Rules.NewMarkId(new Random(7)));
        yield return (Protocol.W164Torch, "torch", W164Text.Torch(255, true));
        yield return (Protocol.W164Escort, "escort (64)", W164Text.Escort(name64, true));
        // WO-165 / WO-166 / WO-167
        yield return (Protocol.W164HitOutcome, "hit outcome", W164Text.HitOutcome(uint.MaxValue, HitVerdict.PerfectBlock, 999.9f, 999.9f));
        yield return (Protocol.W164Weather, "weather", W164Text.Weather(1f, N(48, 'p')));
        yield return (Protocol.W164Travel, "travel request", W164Text.TravelRequest(uint.MaxValue, "@" + N(48, 't')));
        yield return (Protocol.W164Travel, "travel refused", W164Text.TravelRefused(uint.MaxValue, N(24, 'z')));
        yield return (Protocol.W164Travel, "travel start", W164Text.TravelStart(uint.MaxValue, W164Text.Pos(-99999.9f, -99999.9f, -99999.9f)));
        yield return (Protocol.W164Travel, "travel end", W164Text.TravelEnd(uint.MaxValue, -99999.9f, -99999.9f, -99999.9f, true));
        // WO-173 / WO-176 / WO-177
        yield return (Protocol.W164RealHit, "real hit", W164Text.RealHit(npc48, HitVerdict.Blocked, 255, true, 999.9f, 999.9f));
        yield return (Protocol.W164NpcDown, "npc down", W164Text.NpcDown(npc48, 2));
        yield return (Protocol.W164StoryFight, "story fight", npc48);
        yield return (Protocol.W164CorpseAsk, "corpse ask", W164Text.CorpseAsk(Enumerable.Range(0, 5).Select(i => (npc48, -100000, -100000))));
        yield return (Protocol.W164CorpseAnswer, "corpse answer", W164Text.CorpseAnswer(Enumerable.Range(0, 5).Select(i => (npc48, (byte)3))));
        // WO-178
        yield return (Protocol.W164QuickSaveAsk, "quicksave ask", "console");
        yield return (Protocol.W164QuickSaveAnswer, "quicksave answer", "refused " + N(40, 'r'));
        yield return (Protocol.W164QuickSaveSetting, "quicksave setting", "quicksave=off");
        // WO-179 / WO-194: the scene bus letters
        yield return (Protocol.W164SceneBus, "bus S", Wo179Rules.Start(int.MaxValue, 'V', name64));
        yield return (Protocol.W164SceneBus, "bus R", Wo179Rules.Dest(int.MaxValue, -99999.9f, -99999.9f, -99999.9f));
        yield return (Protocol.W164SceneBus, "bus E", Wo179Rules.End(int.MaxValue, "interrupt"));
        yield return (Protocol.W164SceneBus, "bus K", Wo179Rules.SkipKey(int.MaxValue));
        yield return (Protocol.W164SceneBus, "bus L", Wo179Rules.Landed(int.MaxValue, -99999.9f, -99999.9f, -99999.9f));
        yield return (Protocol.W164SceneBus, "bus W", Wo179Rules.Watch(int.MaxValue, -99999.9f, -99999.9f, -99999.9f));
        yield return (Protocol.W164SceneBus, "bus C", Wo194Rules.Started(int.MaxValue, name64));
        foreach (var piece in Wo194Rules.HandlerPieces(int.MaxValue, N(200, 'h')))
            yield return (Protocol.W164SceneBus, "bus Q", piece);
        yield return (Protocol.W164SceneBus, "bus A", Wo194Rules.Answer(int.MaxValue, int.MaxValue, name64));
        yield return (Protocol.W164SceneBus, "bus G", Wo194Rules.Runs(int.MaxValue, name64));
        yield return (Protocol.W164SceneBus, "bus P", Wo194Rules.Clock(int.MaxValue, int.MaxValue, name64));
        yield return (Protocol.W164SceneBus, "bus J", Wo194Rules.Jumped(int.MaxValue, int.MaxValue, name64));
        yield return (Protocol.W164SceneBus, "bus X", Wo194Rules.Stopped(int.MaxValue, name64));
        yield return (Protocol.W164SceneBus, "bus Z", Wo194Rules.Ended(int.MaxValue, name64));
        // WO-184 / WO-189
        yield return (Protocol.W164SaveHealth, "save paired", Wo184Rules.PairedText(uint.MaxValue, N(32, 'f')));
        yield return (Protocol.W164SaveHealth, "save unpaired", Wo184Rules.UnpairedText(int.MaxValue));
        yield return (Protocol.W164SaveHealth, "save stuck", Wo184Rules.StuckWord);
        yield return (Protocol.W164SaveHealth, "save recovered", Wo184Rules.RecoveredWord);
        yield return (Protocol.W164Profile, "profile", Wo189Rules.ProfileText('+', name64));
        yield return (Protocol.W164Profile, "profile ask", Wo189Rules.ProfileText('?', ""));
        // WO-200
        yield return (Protocol.W164Clock, "clock", $"{uint.MaxValue} 100000");
        yield return (Protocol.W164Noise, "noise", Wo200Rules.NoiseText("lockpick", -100000f, -100000f, -10000f));
        // WO-201: the host busy, the speech line, the vote, the ranged step
        yield return (Protocol.W164HostBusy, "host busy", "load 1");
        // (the speech line and the vote's three kinds are retired by WO-204 III.2 -- an older partner may still send them, at their old
        // worst cases: the speech line capped at 196 bytes, six 80-character options, an 80-character voter / choice)
        yield return (Protocol.W164SpeechLine, "speech line (retired, its old cap)", ("v1|" + N(63, 's') + "|60000|65535|Localization/dialog/" + N(170, 'v') + "|")[..196]);
        yield return (Protocol.W164VoteOptions, "vote options (retired, 6 x 80)", $"v1|{uint.MaxValue}|" + string.Join("|", Enumerable.Range(0, 6).Select(i => N(80, 'o'))));
        yield return (Protocol.W164VoteCast, "vote cast (retired)", $"v1|{uint.MaxValue}|6|" + N(80, 'v'));
        yield return (Protocol.W164VoteClose, "vote close (retired)", $"v1|{uint.MaxValue}|" + N(80, 'c'));
        yield return (Protocol.W164RangedStep, "ranged step", "v1|5");
        // WO-202: the key share
        yield return (Protocol.W164KeyShare, "key", Wo202Rules.KeyText("4a6fa310-067a-404d-9813-bd1761d1c70d")!);
        yield return (Protocol.W164KeyShare, "chest", Wo202Rules.ChestText($"{N(80, 'k')} -99999.99 -99999.99 -99999.99")!);
        // WO-204 Act V: a mark's note (16-character id, 120-character note)
        yield return (Protocol.W164MarkNote, "mark note", Wo204Rules.MarkNoteText(N(16, 'm'), N(200, 'n')));
    }

    [Fact]
    public void Every_w164_kind_builder_at_its_worst_case_fits_the_relay_row()
    {
        var row = Protocol.JoinWireFor(Protocol.W164Up)!.Value;
        var kindsSeen = new HashSet<byte>();
        foreach (var (kind, what, text) in WorstCases())
        {
            Assert.False(string.IsNullOrEmpty(text), what);
            byte[] pkt;
            try { pkt = new LootMsg(kind, uint.MaxValue, text).BuildUp(Protocol.W164Up, Protocol.JoinTargetHost); }
            catch (Exception ex) { Assert.Fail($"{Protocol.W164KindName(kind)} ({what}, {text.Length} chars) is refused: {ex.Message}"); return; }
            int len = BinaryPrimitives.ReadUInt16LittleEndian(pkt.AsSpan(1));
            Assert.Equal(pkt.Length - 3, len);
            Assert.True(len >= row.Min && len <= row.Max, $"{Protocol.W164KindName(kind)} ({what}) is {len} bytes, the row takes {row.Min}..{row.Max}");
            Assert.True(Protocol.IsUpLenOk(Protocol.W164Up, len), what);            // the relay's gate
            Assert.True(Protocol.IsJoinDown(Protocol.W164Down, len + 1), what);     // the receiving agent's gate
            kindsSeen.Add(kind);
        }
        // every kind the protocol names has a case here (append a case with a new kind)
        for (byte k = 1; k < 64; k++)
            if (!Protocol.W164KindName(k).StartsWith("unknown-", StringComparison.Ordinal))
                Assert.True(kindsSeen.Contains(k), $"W164 kind {k} ({Protocol.W164KindName(k)}) has no worst case in this test");
    }

    [Fact]
    public void BuildUp_refuses_a_text_over_its_row_before_it_is_sent()
    {
        // the W164 wire's limit: 1400 fits, 1401 is refused
        Assert.NotNull(new LootMsg(Protocol.W164SpeechLine, 1, N(Protocol.W164WireTextMax)).BuildUp(Protocol.W164Up, Protocol.JoinTargetHost));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LootMsg(Protocol.W164SpeechLine, 1, N(Protocol.W164WireTextMax + 1)).BuildUp(Protocol.W164Up, Protocol.JoinTargetHost));
        // rows narrower than LootTextMax are enforced at the sender now (they were not: 120 and 160 were the relay's alone)
        Assert.NotNull(new LootMsg(Protocol.SleepAsk, 1, N(Protocol.SleepTextMax)).BuildUp(Protocol.SleepVoteUp, Protocol.JoinTargetHost));
        var sleep = Assert.Throws<ArgumentOutOfRangeException>(() => new LootMsg(Protocol.SleepAsk, 1, N(Protocol.SleepTextMax + 1)).BuildUp(Protocol.SleepVoteUp, Protocol.JoinTargetHost));
        Assert.Contains("sleep-vote", sleep.Message);
        Assert.Contains($"{Protocol.SleepTextMax}-byte limit", sleep.Message);
        Assert.NotNull(new LootMsg(Protocol.CarryGrab, 1, N(Protocol.CarryTextMax)).BuildUp(Protocol.CarryUp, Protocol.JoinTargetHost));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LootMsg(Protocol.CarryGrab, 1, N(Protocol.CarryTextMax + 1)).BuildUp(Protocol.CarryUp, Protocol.JoinTargetHost));
        // a type the relay has no join row for is refused too (it would be dropped as an unknown or wrong-length frame)
        Assert.Throws<ArgumentOutOfRangeException>(() => new LootMsg(1, 1, "x").BuildUp(Protocol.Position, Protocol.JoinTargetHost));
        // Act I's and Act II's channels: 1400 fits both ways' row
        foreach (byte up in new[] { Protocol.ConvUp, Protocol.W203Up })
        {
            var pkt = new LootMsg(1, 1, N(1400)).BuildUp(up, 3);
            Assert.True(Protocol.IsUpLenOk(up, pkt.Length - 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LootMsg(1, 1, N(1401)).BuildUp(up, 3));
        }
    }

    [Fact]
    public void The_new_kinds_are_named_and_append_only()
    {
        Assert.Equal(((byte)0x76, (byte)0x77, (byte)0x78, (byte)0x79), (Protocol.ConvUp, Protocol.ConvDown, Protocol.W203Up, Protocol.W203Down));
        string[] conv = { "announce", "join", "snapshot", "line", "cam", "anim", "options", "vote", "chosen", "skill", "leave", "end", "timer" };
        for (int i = 0; i < conv.Length; i++) Assert.Equal(conv[i], Protocol.ConvKindName((byte)(i + 1)));
        Assert.Equal("unknown-14", Protocol.ConvKindName(14));
        Assert.Equal((byte)13, Protocol.ConvTimer);
        string[] w203 = { "defence-swing", "defence-outcome", "guard-state", "engage-early", "npc-worn", "npc-pose", "join-progress", "focus-edge", "travel-route" };
        for (int i = 0; i < w203.Length; i++) Assert.Equal(w203[i], Protocol.W203KindName((byte)(i + 1)));
        Assert.Equal("unknown-12", Protocol.W203KindName(12));   // WO-206: 10 the digest checkpoint, 11 the epochs
        Assert.Equal(10, Protocol.Version);   // no bump: new types on the join channel
    }
}
