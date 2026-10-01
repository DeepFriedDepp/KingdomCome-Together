// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-137: shared quests -- the read, the ordered apply, dedupe, refusals, the talk lines, the kill switch (docs/WO-137-findings.md).</summary>
public class Wo137Tests
{
    private const string Deer = "Barbora.trosecko.hledaniPsa.h.prozkoumat_misto_prepadeni.prozkoumatSrnku";
    private const string Quest = "Barbora.trosecko.hledaniPsa";

    private static QuestChange Change(uint seq = 7, byte flags = QuestChange.FNotify | QuestChange.FOldOk | QuestChange.FNewOk,
                                      int old = 1, int nw = 2, string port = "SetDone", string type = "Progress", string path = Deer) =>
        new(seq, flags, old, nw, port, type, path, Quest.Length);

    /// <summary>The DLL's 0x9E body (native wo137.h).</summary>
    private static byte[] Frame(QuestChange c)
    {
        var port = Encoding.ASCII.GetBytes(c.Port); var type = Encoding.ASCII.GetBytes(c.Type); var path = Encoding.ASCII.GetBytes(c.Path);
        var b = new byte[4 + 1 + 4 + 4 + 1 + port.Length + 1 + type.Length + 2 + path.Length + 2];
        int o = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(b, c.Seq); o += 4;
        b[o++] = c.Flags;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), c.Old); o += 4;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), c.New); o += 4;
        b[o++] = (byte)port.Length; port.CopyTo(b, o); o += port.Length;
        b[o++] = (byte)type.Length; type.CopyTo(b, o); o += type.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), (ushort)path.Length); o += 2;
        path.CopyTo(b, o); o += path.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), (ushort)c.QuestLen);
        return b;
    }

    // ---------------------------------------------------------------- the read (the DLL's record)

    [Fact]
    public void A_detector_record_parses_whole_and_names_its_quest()
    {
        var c = Change();
        Assert.True(QuestChange.TryParse(Frame(c), out var got));
        Assert.Equal(c, got);
        Assert.Equal(Quest, got.Quest);
        Assert.True(got.Notify && got.OldOk && got.NewOk);
        Assert.False(got.Mirror || got.Cascade);
    }

    [Fact]
    public void A_short_or_padded_record_is_refused()
    {
        var f = Frame(Change());
        Assert.False(QuestChange.TryParse(f.AsSpan(0, f.Length - 1), out _));
        Assert.False(QuestChange.TryParse([.. f, 0], out _));
        Assert.False(QuestChange.TryParse(f.AsSpan(0, 10), out _));
        var noPath = Frame(Change(path: "x"));
        BinaryPrimitives.WriteUInt16LittleEndian(noPath.AsSpan(noPath.Length - 2 - 1 - 2), 0);   // pathLen 0
        Assert.False(QuestChange.TryParse(noPath, out _));
    }

    // ---------------------------------------------------------------- what goes, what does not

    [Fact]
    public void The_host_sends_quest_changes_but_not_streaming_dlc_silent_or_foreign_ones()
    {
        Assert.Null(Wo137Rules.HostSendVeto(Change()));
        Assert.Null(Wo137Rules.HostSendVeto(Change(flags: QuestChange.FNotify | QuestChange.FCascade)));   // a cascade is the host's world too
        Assert.Null(Wo137Rules.HostSendVeto(Change(flags: QuestChange.FNotify | QuestChange.FMirror)));    // a request it applied: every joiner hears it
        Assert.Equal("per-machine", Wo137Rules.HostSendVeto(Change(type: "Streaming")));
        Assert.Equal("per-machine", Wo137Rules.HostSendVeto(Change(type: "OnOffFocusCamControlEffect")));
        Assert.Equal("per-machine", Wo137Rules.HostSendVeto(Change(type: "OnOffFocusCamControl")));   // observed j1: the joiner's own camera focus
        Assert.Equal("per-machine", Wo137Rules.HostSendVeto(Change(path: Quest + ".h.streamprofileshandling.x.y")));
        Assert.Equal("dlc", Wo137Rules.HostSendVeto(Change(path: "Barbora.trosecko.zavodniPodkovy.a.b") with { QuestLen = 29 }));
        Assert.Equal("silent", Wo137Rules.HostSendVeto(Change(flags: 0)));
        Assert.Equal("not-a-quest", Wo137Rules.HostSendVeto(Change() with { QuestLen = 0 }));
        Assert.Equal("bad-path", Wo137Rules.HostSendVeto(Change(path: "Foreign.module.x")));
    }

    [Fact]
    public void The_joiner_asks_only_for_its_own_root_steps()
    {
        Assert.Null(Wo137Rules.JoinerAskVeto(Change()));
        Assert.Equal("mirror", Wo137Rules.JoinerAskVeto(Change(flags: QuestChange.FNotify | QuestChange.FMirror)));
        Assert.Equal("cascade", Wo137Rules.JoinerAskVeto(Change(flags: QuestChange.FNotify | QuestChange.FCascade)));
        Assert.Equal("no-port", Wo137Rules.JoinerAskVeto(Change(port: "")));
        Assert.Equal("per-machine", Wo137Rules.JoinerAskVeto(Change(type: "ExtrasStreaming")));
        Assert.Equal("silent", Wo137Rules.JoinerAskVeto(Change(flags: 0)));
    }

    [Theory]
    [InlineData("Barbora.kutnohorsko.navstevaLekare", true)]
    [InlineData("Barbora.kutnohorsko.navstevaLekare.h.x", true)]
    [InlineData("Barbora.kutnohorsko.kovarske_mikroquesty.katuvSleh.a", true)]
    [InlineData("Barbora.trosecko.zavodniPodkovy", true)]
    [InlineData("Barbora.trosecko.zavodniPodkovyX.a", false)]
    [InlineData("Barbora.dlc_pack.quest.a", true)]
    [InlineData(Deer, false)]
    public void Dlc_stays_out(string path, bool dlc) => Assert.Equal(dlc, Wo137Rules.IsDlc(path));

    // ---------------------------------------------------------------- refusals and dedupe (the host's verdict)

    [Fact]
    public void The_host_judges_a_request_against_its_own_value()
    {
        Assert.Equal(Wo137Rules.Verdict.Apply, Wo137Rules.Judge(true, 1, 1, 2));      // at the joiner's start: applied
        Assert.Equal(Wo137Rules.Verdict.Already, Wo137Rules.Judge(true, 2, 1, 2));    // already there: counted once
        Assert.Equal(Wo137Rules.Verdict.Refused, Wo137Rules.Judge(true, 3, 1, 2));    // a step already past (or elsewhere)
        Assert.Equal(Wo137Rules.Verdict.Refused, Wo137Rules.Judge(true, 0, 1, 2));    // not reached yet
        Assert.Equal(Wo137Rules.Verdict.Refused, Wo137Rules.Judge(false, 1, 1, 2));   // unreadable: never guessed
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(255, true)]
    [InlineData(0, false)]
    [InlineData(5, false)]
    public void Only_a_sleeping_module_or_no_answer_is_tried_again(byte result, bool retry) => Assert.Equal(retry, Wo137Rules.Retryable(result));

    // ---------------------------------------------------------------- the texts on the wire

    [Fact]
    public void A_change_crosses_as_text_and_back()
    {
        var c = Change(seq: 4242, old: -1, nw: 3, port: "SetAborted");
        Assert.True(Wo137Rules.TryParseChangeText(Wo137Rules.ChangeText(c), out var got));
        Assert.Equal(c with { Type = "" }, got);   // the type is the receiver's own (its DLL reads it)
        var noPort = Change(port: "");
        Assert.True(Wo137Rules.TryParseChangeText(Wo137Rules.ChangeText(noPort), out var np));
        Assert.Equal("", np.Port);
    }

    [Fact]
    public void A_request_and_its_result_cross_as_text_and_back()
    {
        var r = Change(seq: 0);
        Assert.True(Wo137Rules.TryParseRequestText(Wo137Rules.RequestText(r), out var got));
        Assert.Equal(r with { Type = "" }, got);
        foreach (var v in Wo137Rules.Verdicts)
        {
            Assert.True(Wo137Rules.TryParseResultText(Wo137Rules.ResultText(v, 2, "SetDone", Deer), out var verdict, out int hv, out string hp, out string path));
            Assert.Equal((v, 2, "SetDone", Deer), (verdict, hv, hp, path));
        }
        Assert.True(Wo137Rules.TryParseResultText(Wo137Rules.ResultText("refused", 0, "", Deer), out _, out _, out string none, out _));
        Assert.Equal("", none);
        Assert.False(Wo137Rules.TryParseResultText("maybe 2 SetDone " + Deer, out _, out _, out _, out _));
    }

    [Theory]
    [InlineData("7 1 1 2 SetDone 27 Barbora.trosecko.hledaniPsa.h.x\") end System.Quit() --")]
    [InlineData("7 1 1 2 Set\"Done 27 Barbora.trosecko.hledaniPsa.h.x")]
    [InlineData("7 1 1 2 SetDone 27 Barbora.trosecko.hledaniPsa.h x")]
    [InlineData("7 1 1 2 SetDone 999 Barbora.trosecko.hledaniPsa.h.x")]
    [InlineData("7 1 1 2 SetDone -3 Barbora.trosecko.hledaniPsa.h.x")]
    [InlineData("-7 1 1 2 SetDone 27 Barbora.trosecko.hledaniPsa.h.x")]
    [InlineData("7 1 one 2 SetDone 27 Barbora.trosecko.hledaniPsa.h.x")]
    [InlineData("")]
    public void A_malformed_or_hostile_change_text_is_refused(string text) => Assert.False(Wo137Rules.TryParseChangeText(text, out _));

    [Theory]
    [InlineData("1 1 2 - 27 " + Deer)]                   // a request always names its port
    [InlineData("1 1 2 SetDone 0 " + Deer)]               // and a quest
    [InlineData("1 1 2 SetDone 27 Barbora.x")]            // too short to be under a quest
    public void A_request_without_a_port_or_a_quest_is_refused(string text) => Assert.False(Wo137Rules.TryParseRequestText(text, out _));

    [Fact]
    public void A_checkpoint_splits_into_parts_that_fit_and_parse_back_whole()
    {
        var entries = Enumerable.Range(0, 60)
            .Select(i => new Wo137Rules.CheckpointEntry($"{Quest}.h.module_{i}.state{i}", i % 4, i % 3 == 0 ? "" : "SetActive"))
            .ToList();
        var texts = Wo137Rules.CheckpointTexts(entries, 400);
        Assert.True(texts.Count > 1);
        Assert.All(texts, t => Assert.True(t.Length <= 400, $"{t.Length}"));
        var back = new List<Wo137Rules.CheckpointEntry>();
        for (int i = 0; i < texts.Count; i++)
        {
            Assert.True(Wo137Rules.TryParseCheckpointText(texts[i], out int part, out int nparts, out var got));
            Assert.Equal((i + 1, texts.Count), (part, nparts));
            back.AddRange(got);
        }
        Assert.Equal(entries, back);
        Assert.Empty(Wo137Rules.CheckpointTexts([]));
        Assert.False(Wo137Rules.TryParseCheckpointText("2 1 1:-:" + Deer, out _, out _, out _));   // part past the count
        Assert.False(Wo137Rules.TryParseCheckpointText("1 1 1:-:" + Deer + " 2:Set\"x:" + Deer, out _, out _, out _));
    }

    [Fact]
    public void Talk_mode_and_resync_texts_are_checked()
    {
        Assert.True(Wo137Rules.TryParseTalkText(Wo137Rules.TalkText(true, "tzel_olbram"), out bool on, out string npc));
        Assert.True(on); Assert.Equal("tzel_olbram", npc);
        Assert.False(Wo137Rules.TryParseTalkText("on tzel_olbram\")", out _, out _));
        Assert.False(Wo137Rules.TryParseTalkText("maybe tzel_olbram", out _, out _));
        Assert.True(Wo137Rules.TryParseModeText("off mp_quest_sync-off", out bool mon, out string why));
        Assert.False(mon); Assert.Equal("mp_quest_sync-off", why);
        Assert.True(Wo137Rules.TryParseResyncText("joined", out _));
        Assert.False(Wo137Rules.TryParseResyncText("Joined!", out _));
        Assert.False(Wo137Rules.TryParseResyncText("", out _));
    }

    // ---------------------------------------------------------------- the ordered apply

    [Fact]
    public void The_queue_keeps_the_hosts_order_and_takes_each_change_once()
    {
        var q = new QuestApplyQueue();
        Assert.True(q.Enqueue(Change(seq: 1, nw: 1)));
        Assert.True(q.Enqueue(Change(seq: 2, nw: 2)));
        Assert.False(q.Enqueue(Change(seq: 2, nw: 2)));   // a duplicate
        Assert.False(q.Enqueue(Change(seq: 1, nw: 1)));   // older than the newest taken
        Assert.Equal(2, q.Count);
        Assert.Equal(2, q.Duplicates);
        Assert.True(q.TryPeek(out var first)); Assert.Equal(1u, first.Seq);
        Assert.True(q.Remove(first));
        Assert.True(q.TryPeek(out var second)); Assert.Equal(2u, second.Seq);
        Assert.Equal(2u, q.LastSeq);
    }

    [Fact]
    public void A_correction_put_in_front_while_an_apply_runs_is_not_lost()
    {
        var q = new QuestApplyQueue();
        q.Enqueue(Change(seq: 5));
        Assert.True(q.TryPeek(out var applying));
        var fix = new QuestChange(0, QuestChange.FNotify, 3, 2, "SetDone", "", Deer, Quest.Length);
        q.EnqueueFront(fix);                 // the host refused a request meanwhile
        Assert.True(q.Remove(applying));     // the applied change leaves, not the front
        Assert.True(q.TryPeek(out var next));
        Assert.Equal(fix, next);
    }

    [Fact]
    public void A_reset_drops_the_old_world_and_counts_from_the_start()
    {
        var q = new QuestApplyQueue { MaxQueued = 3 };
        for (uint i = 1; i <= 5; i++) q.Enqueue(Change(seq: i));
        Assert.Equal(3, q.Count);
        Assert.Equal(2, q.Dropped);
        Assert.Equal(3, q.Reset());
        Assert.Equal(0, q.Count);
        Assert.Equal(0u, q.LastSeq);
        Assert.True(q.Enqueue(Change(seq: 1)));   // a new host world counts from 1
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, "loading", true)]
    [InlineData(false, "post-load", true)]
    [InlineData(false, "preparing", false)]   // the host's save is still being made: what comes now is in it
    [InlineData(false, null, false)]          // this game's own world: a host change is not for it
    public void Host_changes_are_kept_only_for_the_hosts_world(bool joined, string? phase, bool keep) =>
        Assert.Equal(keep, Wo137Rules.KeepHostChange(joined, phase));

    [Theory]
    [InlineData(true, 2, true, 2, true)]
    [InlineData(true, 2, true, 3, false)]    // a newer value is on its way: left out, compared next time
    [InlineData(false, 0, true, 2, false)]   // never sent this session
    [InlineData(true, 2, false, 2, false)]   // unreadable
    public void A_checkpoint_entry_is_a_value_and_the_port_that_made_it(bool seen, int sent, bool ok, int live, bool use) =>
        Assert.Equal(use, Wo137Rules.CheckpointConsistent(seen, sent, ok, live));

    // ---------------------------------------------------------------- the kill switch, Godwin, loads

    [Fact]
    public void The_mirror_runs_only_with_the_switch_on_henry_played_and_no_load()
    {
        // host: a partner connected
        Assert.True(Wo137Rules.MirrorActive(false, true, true, false, 0, 1, true));
        Assert.False(Wo137Rules.MirrorActive(false, true, true, false, 0, 0, true));    // alone
        Assert.False(Wo137Rules.MirrorActive(false, false, true, false, 0, 1, true));   // mp_quest_sync off: the kill switch
        Assert.False(Wo137Rules.MirrorActive(true, true, true, false, 0, 1, true));     // a load
        Assert.False(Wo137Rules.MirrorActive(false, true, true, false, 1, 1, true));    // a Godwin stretch (S3)
        // joiner: follows the host's announced mode
        Assert.True(Wo137Rules.MirrorActive(false, true, false, true, 0, 0, true));
        Assert.False(Wo137Rules.MirrorActive(false, true, false, true, 0, 0, false));   // the host's switch is off
        Assert.False(Wo137Rules.MirrorActive(false, false, false, true, 0, 0, true));   // its own switch is off
        Assert.False(Wo137Rules.MirrorActive(false, true, false, true, 1, 0, true));
        Assert.False(Wo137Rules.MirrorActive(false, true, false, false, 0, 1, true));   // neither role
    }

    // ---------------------------------------------------------------- the engine's dialogue lines (talking)

    [Fact]
    public void The_dialogue_lines_are_read_as_the_engine_writes_them()
    {
        Assert.True(Wo137Rules.TryParseQuestLine("Soul 'Dude' requested dialog. Assigned id is 241", out var r));
        Assert.Equal(("request", 241), (r.Kind, r.Id));
        Assert.True(Wo137Rules.TryParseQuestLine("Attempting to start new dialogue (runtime id '241') with souls 'Ex: Dude; Ex: tzel_olbram'", out var a));
        Assert.Equal("attempt", a.Kind); Assert.Equal(["Dude", "tzel_olbram"], a.Souls);
        Assert.True(Wo137Rules.TryParseQuestLine("Attempting to start new dialogue (runtime id '5') with souls 'Ex: tzel_man_3 - meta override: SITUACE_POZDRAVY; Ex: tzel_maid - meta override: SITUACE_POZDRAVY'", out var m));
        Assert.Equal(["tzel_man_3", "tzel_maid"], m.Souls);
        Assert.True(Wo137Rules.TryParseQuestLine("[ID: 241] Dialog ending [Ex0: Dude Ex1: tzel_olbram state: CLEANUP flags: 9104]", out var e));
        Assert.Equal(("end", 241), (e.Kind, e.Id)); Assert.Equal(["Dude", "tzel_olbram"], e.Souls);
        // the second end form (observed WO-137 j1: a dialogue interrupted at its topic menu)
        Assert.True(Wo137Rules.TryParseQuestLine("[ID: 79] Dialog ends but no response was played. (Forced: 'N') [Ex0: Dude Ex1: tvez_bozena state: CLEANUP flags: 2113289]", out var e2));
        Assert.Equal(("end", 79), (e2.Kind, e2.Id)); Assert.Equal(["Dude", "tvez_bozena"], e2.Souls);
        Assert.False(Wo137Rules.TryParseQuestLine("[ID: 79] Dialog interrupted. [Ex0: Dude Ex1: tvez_bozena state: WAITING_FOR_INTERACTION flags: 2113289]", out _));
        Assert.True(Wo137Rules.TryParseQuestLine("Switching to player 1", out var p));
        Assert.Equal(("player", 1), (p.Kind, p.Player));
        Assert.False(Wo137Rules.TryParseQuestLine("Soul 'Dude' requested dialog. Assigned id is x", out _));
        Assert.False(Wo137Rules.TryParseQuestLine("[ID: 9] something else", out _));
        Assert.True(Wo137Rules.TryParseQuestLine("Attempting to start new dialogue (runtime id '7') with souls 'Ex: bad\"name'", out var bad));
        Assert.Empty(bad.Souls);   // a name that is not an engine name never reaches Lua
    }

    [Fact]
    public void The_new_join_channel_types_are_gated_by_side()
    {
        Assert.True(Protocol.IsJoinDown(Protocol.QuestHostDown, Protocol.JoinHeaderLen + 1 + Protocol.LootFixedLen + 1));
        Assert.True(Protocol.IsJoinDown(Protocol.QuestAskDown, Protocol.JoinHeaderLen + 1 + Protocol.LootFixedLen + 1));
        Assert.Equal("resync", Protocol.QuestAskName(Protocol.QuestAskResync));
        Assert.Equal("checkpoint", Protocol.QuestHostName(Protocol.QuestHostCheckpoint));
    }
}
