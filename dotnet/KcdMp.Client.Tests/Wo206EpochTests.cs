// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;
using Xunit.Sdk;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-206 E: epochs (Wo206Rules, Wo206Epochs, Wo206Scenes) and Part C's APPLIED log (Wo206Applied). Every check has its twin: the same
/// scenario with mp_epoch_check off (or the fix taken away), on which the check's own assertion is shown to FAIL.
/// </summary>
public class Wo206EpochTests
{
    private const byte Host = 1, Joiner = 2;

    // ---------------------------------------------------------------- wire helpers (the frames as the joiner's reader gets them)

    private static byte[] Down(byte src, byte[] up) { var d = new byte[1 + up.Length - 3]; d[0] = src; up.AsSpan(3).CopyTo(d.AsSpan(1)); return d; }

    private static byte[] EpochFrame(byte src, uint world, uint join, string why) =>
        Down(src, new LootMsg(Protocol.W203Epoch, 7, Wo206Rules.EpochText(world, join, why)).BuildUp(Protocol.W203Up, Protocol.JoinTargetHost));

    private static byte[] NpcState(byte src, string name)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var p = new byte[2 + n.Length + Protocol.NpcStateFixedTail];
        p[0] = src; p[1] = (byte)n.Length; n.CopyTo(p, 2);
        return p;
    }

    private static byte[] NpcDamage(byte src, string name, bool fatal)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var p = new byte[2 + n.Length + Protocol.NpcDamageFixedTail];
        p[0] = src; p[1] = (byte)n.Length; n.CopyTo(p, 2);
        if (fatal) p[2 + n.Length + 8] = Protocol.NpcDamageFlagFatal;
        return p;
    }

    private static byte[] Join(byte src, byte type, byte kind, string text) => Down(src, new LootMsg(kind, 3, text).BuildUp(type, Joiner));

    /// <summary>The joiner's reader: an epoch frame is learned at read; any other frame is stamped with what is known at that moment.</summary>
    private static uint Read(Wo206Epochs joiner, byte[] frame, int type)
    {
        if (Wo206Rules.TryReadEpochFrame(type, frame, out _, out uint w, out uint j, out string why) && why != Wo206Rules.Ack) { joiner.LearnWorld(w, j); return 0; }
        return joiner.StampFor(frame[0], host: false);
    }

    /// <summary>The host loads (world epoch bumped) and its announcement reaches the joiner's reader.</summary>
    private static void HostLoads(Wo206Epochs host, Wo206Epochs joiner)
    {
        uint e = host.BumpWorld();
        Read(joiner, EpochFrame(Host, e, host.Join, "load"), Protocol.W203Down);
    }

    private static (Wo206Epochs Host, Wo206Epochs Joiner) Session(bool check)
    {
        var h = new Wo206Epochs { Check = check };
        var j = new Wo206Epochs { Check = check };
        h.EnsureHost();
        Read(j, EpochFrame(Host, h.World, h.Join, "peer"), Protocol.W203Down);
        return (h, j);
    }

    // ---------------------------------------------------------------- switches, text, kinds

    [Fact]
    public void The_switches_epoch_check_on_and_the_research_switch_off()
    {
        Assert.True(Wo206Rules.DefaultOn("epoch_check"));
        Assert.False(Wo206Rules.DefaultOn("research_death_from_health"));
        Assert.True(Wo206Rules.TryParseCfg("epoch_check=off", out var n, out bool on));
        Assert.Equal("epoch_check", n); Assert.False(on);
        Assert.False(Wo206Rules.TryParseCfg("epoch_check=maybe", out _, out _));
        Assert.False(Wo206Rules.TryParseCfg("nope=on", out _, out _));
        Assert.True(new Wo206Epochs().Check);
    }

    [Fact]
    public void The_announcement_is_kind_11_beside_the_digest_checkpoint_10()
    {
        Assert.Equal((byte)11, Protocol.W203Epoch);
        Assert.Equal("epoch", Protocol.W203KindName(11));
        Assert.Equal("digest-checkpoint", Protocol.W203KindName(10));   // WO-206 C: the digest's checkpoint marker
        Assert.Equal("v1|4|9|load", Wo206Rules.EpochText(4, 9, "load"));
        Assert.True(Wo206Rules.TryParseEpoch("v1|4|9|ack|more", out uint w, out uint j, out string why));   // later fields may be appended
        Assert.Equal((4u, 9u, "ack"), (w, j, why));
        Assert.False(Wo206Rules.TryParseEpoch("v2|4|9|load", out _, out _, out _));
        Assert.False(Wo206Rules.TryParseEpoch("v1|-4|9|load", out _, out _, out _));
        Assert.True(Wo206Rules.TryReadEpochFrame(Protocol.W203Down, EpochFrame(Host, 5, 6, "tick"), out byte src, out w, out j, out why));
        Assert.Equal((Host, 5u, 6u, "tick"), (src, w, j, why));
        Assert.False(Wo206Rules.TryReadEpochFrame(Protocol.W203Down, Join(Host, Protocol.W203Up, Protocol.W203JoinProgress, "v1|0000000a|world|3"), out _, out _, out _, out _));
    }

    [Fact]
    public void The_guarded_frames_and_their_categories()
    {
        Assert.Equal(Wo206Cat.Npc, Wo206Rules.WorldCategory(Protocol.NpcStateDown, NpcState(Host, "ttkc_man_1")));
        Assert.Equal(Wo206Cat.Npc, Wo206Rules.WorldCategory(Protocol.NpcDamageDown, NpcDamage(Host, "ttkc_man_1", fatal: false)));
        Assert.Equal(Wo206Cat.Death, Wo206Rules.WorldCategory(Protocol.NpcDamageDown, NpcDamage(Host, "ttkc_man_1", fatal: true)));
        Assert.Equal(Wo206Cat.Quest, Wo206Rules.WorldCategory(Protocol.QuestHostDown, Join(Host, Protocol.QuestHostUp, Protocol.QuestHostChange, "x")));
        Assert.Equal(Wo206Cat.Quest, Wo206Rules.WorldCategory(Protocol.QuestHostDown, Join(Host, Protocol.QuestHostUp, Protocol.QuestHostCheckpoint, "x")));
        Assert.Equal(Wo206Cat.Quest, Wo206Rules.WorldCategory(Protocol.QuestAskDown, Join(Joiner, Protocol.QuestAskUp, Protocol.QuestAskRequest, "x")));
        Assert.Equal(Wo206Cat.Loot, Wo206Rules.WorldCategory(Protocol.LootHostDown, Join(Host, Protocol.LootHostUp, Protocol.LootHostLedger, "x")));
        Assert.Null(Wo206Rules.WorldCategory(Protocol.QuestHostDown, Join(Host, Protocol.QuestHostUp, Protocol.QuestHostMode, "on x")));
        Assert.Null(Wo206Rules.WorldCategory(Protocol.LootHostDown, Join(Host, Protocol.LootHostUp, Protocol.LootHostBodyState, "x")));
        Assert.Null(Wo206Rules.WorldCategory(Protocol.Ghost, new byte[18]));
    }

    // ---------------------------------------------------------------- world epoch: a frame read before the host's load, applied after

    /// <summary>The joiner reads a host frame, then the host's load announcement; the frame reaches its apply point after that (a backlog
    /// in the processor, the WO-136 load hold, the quest queue). Returns whether it is applied.</summary>
    private static (bool Applied, Wo206Epochs J) StaleWorldWrite(bool check, int type, byte[] frame)
    {
        var (h, j) = Session(check);
        uint stamp = Read(j, frame, type);
        Assert.False(j.WorldStale(stamp));          // fresh at read
        HostLoads(h, j);
        var cat = Wo206Rules.WorldCategory(type, frame)!.Value;
        return (j.AdmitWorld(cat, stamp), j);
    }

    private static void AssertDropped((bool Applied, Wo206Epochs J) r, Wo206Cat cat)
    {
        Assert.False(r.Applied);
        Assert.Equal(1, r.J.DroppedWorld(cat));
        Assert.Equal(0, r.J.AppliedOld);
    }

    private static void AssertTwinApplied((bool Applied, Wo206Epochs J) twin, Wo206Cat cat)
    {
        Assert.True(twin.Applied);
        Assert.Equal(1, twin.J.AppliedOld);                         // the twin run's proof that a stale item got through
        Assert.ThrowsAny<XunitException>(() => AssertDropped(twin, cat));   // the check's own assertion fails on the twin
    }

    public static TheoryData<int, string> WorldWrites => new()
    {
        { Protocol.NpcStateDown, "npc" }, { Protocol.QuestHostDown, "quest" }, { Protocol.LootHostDown, "loot" }, { Protocol.NpcDamageDown, "death" },
    };

    private static byte[] FrameOf(int type) => type switch
    {
        Protocol.NpcStateDown => NpcState(Host, "ttkc_man_1"),
        Protocol.QuestHostDown => Join(Host, Protocol.QuestHostUp, Protocol.QuestHostChange, "x"),
        Protocol.LootHostDown => Join(Host, Protocol.LootHostUp, Protocol.LootHostLedger, "x"),
        _ => NpcDamage(Host, "ttkc_man_1", fatal: true),
    };

    [Theory, MemberData(nameof(WorldWrites))]
    public void A_world_write_of_an_older_world_epoch_is_dropped_and_counted(int type, string what)
    {
        var cat = Wo206Rules.WorldCategory(type, FrameOf(type))!.Value;
        Assert.Equal(what, cat.ToString().ToLowerInvariant());
        AssertDropped(StaleWorldWrite(true, type, FrameOf(type)), cat);
    }

    [Theory, MemberData(nameof(WorldWrites))]
    public void Twin_with_the_check_off_the_old_world_write_is_applied_and_counted(int type, string what)
    {
        _ = what;
        var cat = Wo206Rules.WorldCategory(type, FrameOf(type))!.Value;
        AssertTwinApplied(StaleWorldWrite(false, type, FrameOf(type)), cat);
    }

    [Fact]
    public void A_frame_read_after_the_announcement_is_the_new_world_and_lands()
    {
        var (h, j) = Session(true);
        HostLoads(h, j);
        uint stamp = Read(j, NpcState(Host, "ttkc_man_1"), Protocol.NpcStateDown);
        Assert.True(j.AdmitWorld(Wo206Cat.Npc, stamp));
        Assert.Equal(0, j.DroppedWorld(Wo206Cat.Npc));
        Assert.True(j.AdmitWorld(Wo206Cat.Npc, 0));   // unstamped (no announcement yet): never judged
    }

    /// <summary>The quest queue (WO-137): a host change queued under one world, drained after the host loaded.</summary>
    private static (bool Applied, Wo206Epochs J) QueuedQuestChange(bool check)
    {
        var (h, j) = Session(check);
        var q = new QuestApplyQueue();
        uint stamp = Read(j, Join(Host, Protocol.QuestHostUp, Protocol.QuestHostChange, "x"), Protocol.QuestHostDown);
        Assert.True(q.Enqueue(new QuestChange(5, 0, 0, 1, "SetDone", "int", "q_test.state1", 6) { WorldEpoch = stamp }));
        HostLoads(h, j);
        Assert.True(q.TryPeek(out var c));
        return (j.AdmitWorld(Wo206Cat.Quest, c.WorldEpoch), j);
    }

    [Fact]
    public void A_queued_quest_change_from_before_the_hosts_load_never_lands() => AssertDropped(QueuedQuestChange(true), Wo206Cat.Quest);

    [Fact]
    public void Twin_a_queued_quest_change_lands_with_the_check_off() => AssertTwinApplied(QueuedQuestChange(false), Wo206Cat.Quest);

    /// <summary>The host's side: a joiner's frame sent before that joiner heard of the host's load (its ack) is the old world's.</summary>
    private static (bool Applied, Wo206Epochs J) JoinerFrameBeforeItsAck(bool check)
    {
        var (h, j) = Session(check);
        h.NoteAck(Joiner, j.World);
        HostLoads(h, j);                                         // the host bumped; the joiner's ack of it has not arrived yet
        uint stamp = h.StampFor(Joiner, host: true);             // a death announcement the joiner sent before hearing of the load
        var r = (h.AdmitWorld(Wo206Cat.Death, stamp), h);
        h.NoteAck(Joiner, j.World);                              // the ack arrives: what it sends from now on is the new world's
        Assert.True(h.AdmitWorld(Wo206Cat.Death, h.StampFor(Joiner, host: true)));
        return r;
    }

    [Fact]
    public void The_host_drops_a_joiners_death_sent_before_it_knew_the_hosts_load() => AssertDropped(JoinerFrameBeforeItsAck(true), Wo206Cat.Death);

    [Fact]
    public void Twin_the_host_applies_it_with_the_check_off() => AssertTwinApplied(JoinerFrameBeforeItsAck(false), Wo206Cat.Death);

    // ---------------------------------------------------------------- join epoch

    /// <summary>A joiner message of attempt k that arrives after the host aborted k. Returns whether the host takes it.</summary>
    private static (bool Taken, Wo206Epochs H) LateMessageOfAbortedAttempt(bool check, string kind)
    {
        var h = new Wo206Epochs { Check = check };
        const uint k = 0x1234abcd;
        uint e1 = h.StartJoin(k);
        Assert.False(h.JoinAttemptStale(k));
        uint e2 = h.EndJoin(k, aborted: true);
        Assert.Equal(e1 + 1, e2);
        return (h.AdmitJoin(kind, h.JoinAttemptStale(k)), h);
    }

    private static void AssertJoinDropped((bool Taken, Wo206Epochs H) r, string kind)
    {
        Assert.False(r.Taken);
        Assert.Equal(1, r.H.DroppedJoin);
        Assert.Equal(1, r.H.DroppedJoinKind(kind));
    }

    [Theory, InlineData("progress"), InlineData("ready"), InlineData("ack"), InlineData("done"), InlineData("request")]
    public void A_join_message_of_an_aborted_attempt_is_dropped_per_kind(string kind) => AssertJoinDropped(LateMessageOfAbortedAttempt(true, kind), kind);

    [Theory, InlineData("progress"), InlineData("ready")]
    public void Twin_with_the_check_off_it_is_taken_and_counted_applied_old(string kind)
    {
        var twin = LateMessageOfAbortedAttempt(false, kind);
        Assert.True(twin.Taken);
        Assert.Equal(1, twin.H.AppliedOld);
        Assert.ThrowsAny<XunitException>(() => AssertJoinDropped(twin, kind));
    }

    [Fact]
    public void The_live_attempt_is_taken_a_new_one_is_not_judged_and_a_ready_closes_without_a_bump()
    {
        var h = new Wo206Epochs();
        Assert.False(h.JoinAttemptStale(77));        // never started here: the join's own match decides
        uint e = h.StartJoin(77);
        Assert.True(h.AdmitJoin("ack", h.JoinAttemptStale(77)));
        Assert.Equal(e, h.EndJoin(77, aborted: false));   // ready: no new epoch ...
        Assert.True(h.JoinAttemptStale(77));         // ... but the attempt is closed: a late progress of it is not "loading"
        Assert.True(h.StartJoin(78) > e);
    }

    /// <summary>
    /// WO-202 III.3's shape: the host starts join k, the joiner reports "loading" (its LeashState and its JoinProgress), the host aborts
    /// k; the joiner's late "loading" for k arrives (both kinds). Returns whether the host shows the joiner loading afterwards.
    /// </summary>
    private static (bool ShowsLoading, Wo206Epochs H) LateLoadingAfterAbort(bool check)
    {
        var h = new Wo206Epochs { Check = check };
        var j = new Wo206Epochs { Check = check };
        h.EnsureHost();
        const uint k = 0xbeef0001;
        h.StartJoin(k);
        Read(j, EpochFrame(Host, h.World, h.Join, "join-start"), Protocol.W203Down);
        uint loadingStamp = Wo206Rules.JoinerStateStamp(j.Join, rewindEpoch: 1, loadingOnlyFromRewind: false);
        Assert.True(h.NoteJoinerState(Joiner, loadingStamp, loading: true));
        Assert.True(h.ShowsJoinerLoading(Joiner));
        h.EndJoin(k, aborted: true);                                 // the host aborted k (WO-202 III.3: at 20 s)
        h.StaleJoinerViews();
        // late: both sent before the joiner heard of the abort
        h.AdmitJoin("progress", h.JoinAttemptStale(k));
        h.NoteJoinerState(Joiner, loadingStamp, loading: true);
        return (h.ShowsJoinerLoading(Joiner), h);
    }

    [Fact]
    public void III_3_after_an_abort_the_host_never_shows_the_joiner_loading()
    {
        var r = LateLoadingAfterAbort(true);
        Assert.False(r.ShowsLoading);
        Assert.Equal(1, r.H.DroppedJoinKind("progress"));
        Assert.Equal(1, r.H.DroppedJoinKind("state"));
    }

    [Fact]
    public void Twin_III_3_with_the_check_off_the_host_shows_the_joiner_loading()
    {
        var twin = LateLoadingAfterAbort(false);
        Assert.True(twin.ShowsLoading);
        Assert.Equal(2, twin.H.AppliedOld);
        Assert.ThrowsAny<XunitException>(() => Assert.False(twin.ShowsLoading));
    }

    [Fact]
    public void A_reloading_that_outlived_its_rejoin_is_stamped_with_the_epoch_it_began_under()
    {
        // the host reloads at join epoch 3 (the joiner keeps 3 for its rewinding); its rejoin starts (4) and is aborted (5)
        var h = new Wo206Epochs();
        h.EnsureHost(); h.StartJoin(1); h.EndJoin(1, true);
        uint rewindEpoch = h.Join;                   // 3
        h.StartJoin(2); h.EndJoin(2, true);          // 5
        uint stuck = Wo206Rules.JoinerStateStamp(knownJoinEpoch: h.Join, rewindEpoch, loadingOnlyFromRewind: true);
        Assert.Equal(rewindEpoch, stuck);
        Assert.False(h.NoteJoinerState(Joiner, stuck, loading: true));
        Assert.False(h.ShowsJoinerLoading(Joiner));
        // a joiner in a join of its own (or at the menu) carries the epoch it knows
        Assert.Equal(h.Join, Wo206Rules.JoinerStateStamp(h.Join, rewindEpoch, loadingOnlyFromRewind: false));
    }

    [Fact]
    public void The_joiners_ui_never_shows_a_status_of_an_attempt_not_live_here()
    {
        Assert.True(Wo206Rules.JoinerStatusStale(Protocol.JoinStateWaitingReady, liveAttempt: false, everOurs: true));   // "World received. Loading..."
        Assert.False(Wo206Rules.JoinerStatusStale(Protocol.JoinStateWaitingReady, liveAttempt: true, everOurs: true));
        Assert.False(Wo206Rules.JoinerStatusStale(Protocol.JoinStateResumed, liveAttempt: false, everOurs: true));     // its end is still told
        Assert.True(Wo206Rules.JoinerStatusStale(Protocol.JoinStateResumed, liveAttempt: false, everOurs: false));
        Assert.False(Wo206Rules.JoinerStatusStale(Protocol.JoinStateReloading, liveAttempt: false, everOurs: false));  // session-wide states
        var twin = new Wo206Epochs { Check = false };
        Assert.True(twin.AdmitJoin("status", Wo206Rules.JoinerStatusStale(Protocol.JoinStateWaitingReady, false, true)));
        Assert.Equal(1, twin.AppliedOld);
    }

    // ---------------------------------------------------------------- scene instance

    /// <summary>The partner's play 5 of a scene reached 90 s and ended; its play 9 runs (samples at 5 and 6 s); then one late sample of
    /// play 5 at 90 s arrives. Returns the clock estimate (Wo202Rules.PartnerNowS over what was kept).</summary>
    private static (double Est, Wo206Epochs E) StaleSceneSample(bool check)
    {
        var e = new Wo206Epochs { Check = check };
        var v2 = new List<Wo202Rules.ClockSample>();
        void Sample(int seq, double t, long at)
        {
            if (e.AdmitScene(e.Scenes.Stale(Host, "cs_test", seq))) v2.Add(new Wo202Rules.ClockSample(t, at));
        }
        e.Scenes.Begin(Host, "cs_test", 5);
        e.Scenes.End(Host, "cs_test", 5);
        e.Scenes.Begin(Host, "cs_test", 9);
        Sample(9, 5.0, 1000);
        Sample(9, 6.0, 2000);
        Sample(5, 90.0, 2000);   // the previous play's
        return (Wo202Rules.PartnerNowS(v2, 0, 2000), e);
    }

    [Fact]
    public void A_sample_of_the_previous_play_of_the_same_scene_is_dropped()
    {
        var r = StaleSceneSample(true);
        Assert.InRange(r.Est, 5.9, 6.1);
        Assert.Equal(1, r.E.DroppedScene);
    }

    [Fact]
    public void Twin_with_the_check_off_it_moves_the_clock()
    {
        var twin = StaleSceneSample(false);
        Assert.True(twin.Est > 80);
        Assert.Equal(1, twin.E.AppliedOld);
        Assert.ThrowsAny<XunitException>(() => Assert.InRange(twin.Est, 5.9, 6.1));
    }

    [Fact]
    public void Scene_instances_a_newer_play_is_adopted_an_ended_one_is_closed()
    {
        var s = new Wo206Scenes();
        Assert.False(s.Stale(Host, "a", 3));   // no C seen (this agent started mid-scene): adopted
        Assert.True(s.Stale(Host, "a", 2));
        Assert.False(s.Stale(Host, "a", 4));
        s.End(Host, "a", 4);
        Assert.True(s.Stale(Host, "a", 4));    // after its Z
        s.Begin(Host, "a", 1);                 // a restarted partner agent counts from 1 again: its C is the current play
        Assert.False(s.Stale(Host, "a", 1));
        Assert.False(s.Stale(Joiner, "a", 1)); // another partner's plays are its own
    }

    // ---------------------------------------------------------------- APPLIED (Part C)

    private static void AssertAppliedShape(string json)
    {
        using var d = JsonDocument.Parse(json);
        var r = d.RootElement;
        string[] keys = ["t", "m", "cat", "key", "cause", "before", "after", "epoch", "seq"];
        Assert.Equal(keys, r.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(JsonValueKind.Number, r.GetProperty("t").ValueKind);
        Assert.Contains(r.GetProperty("m").GetString(), new[] { "host", "joiner" });
        Assert.Contains(r.GetProperty("cat").GetString(), new[] { "quest", "dead", "loot" });
        Assert.Equal(JsonValueKind.String, r.GetProperty("key").ValueKind);
        Assert.Equal(JsonValueKind.String, r.GetProperty("cause").ValueKind);
        foreach (string v in new[] { "before", "after" })
        {
            var e = r.GetProperty(v);
            Assert.Contains(e.ValueKind, new[] { JsonValueKind.String, JsonValueKind.Null });
            if (e.ValueKind == JsonValueKind.String) Assert.NotEqual("-", e.GetString());   // a missing value is null in the JSON ("-" is the console line's)
        }
        Assert.Equal(JsonValueKind.Number, r.GetProperty("epoch").ValueKind);
        Assert.True(r.GetProperty("epoch").TryGetInt64(out _));
        Assert.Equal(JsonValueKind.Number, r.GetProperty("seq").ValueKind);
        Assert.True(r.GetProperty("seq").TryGetInt32(out _));
    }

    [Fact]
    public void The_applied_line_and_json_have_the_exact_shape()
    {
        Assert.Equal("MP-APPLIED cat=quest key=quest_a/state b cause=host-change before=sv:3 after=sv:4 epoch=2 seq=7".Replace("state b", "state_b"),
            Wo206Applied.Line("quest", "quest_a/state b", "host-change", "sv:3", "sv:4", 2, 7));
        Assert.Equal("MP-APPLIED cat=dead key=ttkc_man_1 cause=0x31-fatal before=- after=dead epoch=1 seq=0",
            Wo206Applied.Line("dead", "ttkc_man_1", "0x31-fatal", null, "dead", 1, 0));
        string json = Wo206Applied.Json(1761200000123, false, "quest", "quest_a/state1", "host-change", "sv:3", "sv:4", 2, 7);
        Assert.Equal("{\"t\":1761200000123,\"m\":\"joiner\",\"cat\":\"quest\",\"key\":\"quest_a/state1\",\"cause\":\"host-change\",\"before\":\"sv:3\",\"after\":\"sv:4\",\"epoch\":2,\"seq\":7}", json);
        AssertAppliedShape(json);
        AssertAppliedShape(Wo206Applied.Json(1, true, "dead", "ttkc_man_1", "0x27-dead-transition", null, "dead", 3, 0));
        Assert.Equal("sv:-1", Wo206Applied.Sv(-1));
    }

    [Fact]
    public void Twin_a_record_off_the_shape_fails_the_shape_check()
    {
        // the mutation: "-" written for a missing before (the console line's convention) and the epoch as a string
        string dash = "{\"t\":1,\"m\":\"joiner\",\"cat\":\"quest\",\"key\":\"k\",\"cause\":\"c\",\"before\":\"-\",\"after\":\"sv:1\",\"epoch\":2,\"seq\":0}";
        Assert.ThrowsAny<XunitException>(() => AssertAppliedShape(dash));
        string text = "{\"t\":1,\"m\":\"joiner\",\"cat\":\"quest\",\"key\":\"k\",\"cause\":\"c\",\"before\":null,\"after\":\"sv:1\",\"epoch\":\"2\",\"seq\":0}";
        Assert.ThrowsAny<XunitException>(() => AssertAppliedShape(text));
        string reordered = "{\"m\":\"joiner\",\"t\":1,\"cat\":\"quest\",\"key\":\"k\",\"cause\":\"c\",\"before\":null,\"after\":\"sv:1\",\"epoch\":2,\"seq\":0}";
        Assert.ThrowsAny<XunitException>(() => AssertAppliedShape(reordered));
    }

    [Fact]
    public void The_applied_file_starts_empty_each_session_and_gets_one_json_line_per_write()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wo206-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, Wo206Applied.FileName);
        string other = Path.Combine(dir, "agent.log");
        try
        {
            File.WriteAllText(path, "an earlier session's line\n");
            File.WriteAllText(other, "kept");
            Wo206Applied.StartSession(path);
            Wo206Applied.SetEpochs(4, 9);
            Wo206Applied.CheckpointSeq = 12;
            Assert.Equal((4u, 9u), (Wo206Applied.WorldEpoch, Wo206Applied.JoinEpoch));
            Wo206Applied.Write(false, "loot", "chest_1/0f8c2d1e-0000-0000-0000-000000000001", "host-ledger", null, "took");
            Wo206Applied.Write(true, "quest", "q/state", "joiner-request", "sv:0", "sv:1", epoch: 3);
            Wo206Applied.EndSession();
            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            foreach (var l in lines) AssertAppliedShape(l);
            using (var d = JsonDocument.Parse(lines[0]))
            {
                Assert.Equal(4, d.RootElement.GetProperty("epoch").GetInt32());   // the current one when the item carried none
                Assert.Equal(12, d.RootElement.GetProperty("seq").GetInt32());
                Assert.Equal(JsonValueKind.Null, d.RootElement.GetProperty("before").ValueKind);
                Assert.Equal("took", d.RootElement.GetProperty("after").GetString());
            }
            using (var d = JsonDocument.Parse(lines[1])) Assert.Equal(3, d.RootElement.GetProperty("epoch").GetInt32());
            Assert.Equal("kept", File.ReadAllText(other));   // nothing else is touched
        }
        finally
        {
            Wo206Applied.EndSession();
            Wo206Applied.CheckpointSeq = 0;
            Wo206Applied.SetEpochs(0, 0);
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    // ---------------------------------------------------------------- the C3 twin's research path

    [Fact]
    public void Death_from_health_only_with_the_env_var()
    {
        // the field's case: the observer read hp 0, the engine's soul reads alive (0)
        Assert.True(Wo206Rules.DeathAnnounced(Wo206Rules.ResearchEffective(true, "1"), soulDead: 0, hp: "0.0"));
        Assert.False(Wo206Rules.DeathAnnounced(Wo206Rules.ResearchEffective(true, "1"), soulDead: 1, hp: "12.5"));   // health alone decides
        Assert.False(Wo206Rules.ResearchEffective(true, null));
        Assert.False(Wo206Rules.ResearchEffective(true, "0"));
        Assert.False(Wo206Rules.ResearchEffective(true, "true"));
        Assert.False(Wo206Rules.ResearchEffective(false, "1"));
        Assert.False(Wo206Rules.DeathFromHealth("nan"));
        Assert.False(Wo206Rules.DeathFromHealth(null));
    }

    [Fact]
    public void Twin_without_the_env_var_the_soul_decides_and_no_death_goes_out()
    {
        bool research = Wo206Rules.ResearchEffective(true, envValue: null);   // the switch on, the env var missing (a shipped machine)
        bool announced = Wo206Rules.DeathAnnounced(research, soulDead: 0, hp: "0.0");
        Assert.False(announced);
        Assert.ThrowsAny<XunitException>(() => Assert.True(announced));
        Assert.True(Wo206Rules.DeathAnnounced(research, soulDead: 1, hp: "0.0"));   // WO-202 II.3 as shipped
    }

    // ---------------------------------------------------------------- the stats line

    [Fact]
    public void The_stats_line_names_every_counter_in_order()
    {
        var (h, j) = Session(true);
        HostLoads(h, j);
        j.AdmitWorld(Wo206Cat.Loot, 1);
        string line = j.StatsLine(0);
        Assert.StartsWith("MP-W206-EPOCH join=1 world=2 scene=0 dropped_join=0 dropped_world_npc=0 dropped_world_quest=0 dropped_world_loot=1 dropped_world_death=0 dropped_scene=0 applied_old=0 ", line);
        Assert.Contains("epoch_check=on", line);
    }

    [Fact]
    public void The_join_epoch_slot_of_a_leash_state_carries_the_stamp_and_its_body_is_unchanged()
    {
        var s = new LeashState(Protocol.LeashFlagLoading, 1, 0, 0, 0, 0);
        byte[] a = s.Build(), b = s.Build(42);
        Assert.Equal(a.Length, b.Length);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(a.AsSpan(4)));
        Assert.Equal(42u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4)));
        Assert.True(a.AsSpan(8).SequenceEqual(b.AsSpan(8)));
        Assert.True(Protocol.TrySplitJoinDown(Down(Joiner, b), out _, out _, out uint stamp, out var body));
        Assert.Equal(42u, stamp);
        Assert.True(LeashState.TryDecode(body, out var back));
        Assert.Equal(s, back);
    }

    [Fact]
    public void An_aborted_join_stops_its_pipeline_and_the_twin_runs_on()
    {
        Assert.False(Wo206Rules.StopAbortedJoin(stillLive: true, twinEnv: null));   // the live attempt goes on
        Assert.True(Wo206Rules.StopAbortedJoin(stillLive: false, twinEnv: null));   // aborted (the bench: relay lost at world-sent): stop
        Assert.True(Wo206Rules.StopAbortedJoin(stillLive: false, twinEnv: "0"));
        Assert.False(Wo206Rules.StopAbortedJoin(stillLive: false, twinEnv: "1"));   // twin: the old behaviour, it places and loads
    }

    [Fact]
    public void A_request_waiting_for_the_world_is_forgotten_when_the_relay_drops()
    {
        Assert.True(Wo206Rules.ForgetRequestOnRelayLoss(0x34dc7f27, null));     // the bench: b2f-paused, the joiner never asked again
        Assert.False(Wo206Rules.ForgetRequestOnRelayLoss(0, null));             // nothing outstanding
        Assert.False(Wo206Rules.ForgetRequestOnRelayLoss(0x34dc7f27, "1"));     // twin: the old behaviour keeps it and blocks
    }

    [Fact]
    public void A_refused_poll_is_tried_again_and_the_twin_gives_up()
    {
        Assert.Equal(10_000, Wo206Rules.PollRetryDelayMs(1, null));   // the bench: one refusal during the join's hold silenced a session
        Assert.Equal(30_000, Wo206Rules.PollRetryDelayMs(3, null));
        Assert.Equal(60_000, Wo206Rules.PollRetryDelayMs(40, null));  // at most a minute
        Assert.Equal(long.MaxValue, Wo206Rules.PollRetryDelayMs(1, "1"));   // twin: never again (the old behaviour)
        Assert.True(Wo206Rules.LogPollRefusal(1));
        Assert.False(Wo206Rules.LogPollRefusal(2));
        Assert.True(Wo206Rules.LogPollRefusal(10));
    }
}
