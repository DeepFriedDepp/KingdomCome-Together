// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-207: the queue's trace (B.1), the decision timer's rules (A.3), the keep-awake flag (C.2).</summary>
/// <summary>WO-208: the WO-207 trace's counters are process-wide (Wo207Trace is static): its tests run apart from every other class (the
/// gates' runs saw The_trace_charges_a_handlers_calls_to_its_kind fail beside other classes' readers, pass alone).</summary>
[CollectionDefinition("Wo207Trace", DisableParallelization = true)]
public class Wo207TraceCollection { }

[Collection("Wo207Trace")]
public class Wo207Tests
{
    // ---- A.3 the decision timer

    private static byte[] Op91(uint snapSeq, bool right, (uint seq, bool on, float limit, uint sinceSet, uint sinceUpd)? timer)
    {
        var b = new List<byte> { 1, 0, 255, 1 };                     // armed, current 0, no vote, 1 row
        b.AddRange(new byte[] { 0, 0 });                              // used 0, no icon
        b.AddRange(BitConverter.GetBytes(0)); b.AddRange(BitConverter.GetBytes(0)); b.Add(0);
        var text = System.Text.Encoding.UTF8.GetBytes("@dlg_x");
        b.Add((byte)text.Length); b.AddRange(text);
        b.AddRange(BitConverter.GetBytes(snapSeq)); b.Add((byte)(right ? 1 : 0));
        if (timer is { } t)
        {
            b.AddRange(BitConverter.GetBytes(t.seq)); b.Add((byte)(t.on ? 1 : 0)); b.AddRange(BitConverter.GetBytes(t.limit));
            b.AddRange(BitConverter.GetBytes(t.sinceSet)); b.AddRange(BitConverter.GetBytes(t.sinceUpd));
        }
        return [.. b];
    }

    [Fact]
    public void The_timer_tail_follows_the_snapshot()
    {
        var p = Op91(7, true, (3, true, 0.5f, 3100, 100));
        var rows = Wo204Rules.ParseOptionsPoll(p, out _, out uint seq, out bool right, out int tailAt);
        Assert.NotNull(rows);
        Assert.Equal((7u, true), (seq, right));
        var t = Wo207Rules.ParseTimerTail(p, tailAt);
        Assert.NotNull(t);
        Assert.Equal((3u, true, 0.5f), (t!.Value.Seq, t.Value.On, t.Value.Limit));
        // 0.5 fell in the 3 s between the set and the last update: 1/6 per second; 100 ms since: ~0.483 left now
        Assert.InRange(t.Value.RatePerS, 0.166f, 0.168f);
        Assert.InRange(t.Value.LeftNow, 0.481f, 0.485f);
    }

    [Fact]
    public void Twin_an_older_DLL_has_no_timer()
    {
        var p = Op91(7, true, null);
        Assert.NotNull(Wo204Rules.ParseOptionsPoll(p, out _, out _, out _, out int tailAt));
        Assert.Null(Wo207Rules.ParseTimerTail(p, tailAt));
        Assert.Null(Wo207Rules.ParseTimerTail(p, -1));
    }

    [Fact]
    public void A_timer_just_set_runs_at_no_rate_yet_and_an_off_one_shows_nothing()
    {
        var justSet = new Wo207Rules.TimerPoll(1, true, 1f, 40, 40);
        Assert.Equal(0f, justSet.RatePerS);
        Assert.Equal(1f, justSet.LeftNow);
        var off = new Wo207Rules.TimerPoll(2, false, 0.3f, 9000, 9000);
        Assert.Equal(0f, off.LeftNow);
    }

    [Fact]
    public void The_timer_text_round_trips_and_refuses_junk()
    {
        string t = Wo207Rules.TimerText(51, 2, true, 0.4567f, 0.1667f);
        Assert.Equal("v1|51|2|1|0.457|0.1667", t);
        Assert.True(Wo207Rules.TryParseTimer(t, out int cid, out int dec, out bool on, out float left, out float rate));
        Assert.Equal((51, 2, true), (cid, dec, on));
        Assert.InRange(left, 0.456f, 0.458f); Assert.InRange(rate, 0.166f, 0.167f);
        Assert.False(Wo207Rules.TryParseTimer("v1|51|2|2|0.4|0.1", out _, out _, out _, out _, out _));
        Assert.False(Wo207Rules.TryParseTimer("v1|51|2|1|x|0.1", out _, out _, out _, out _, out _));
        Assert.False(Wo207Rules.TryParseTimer("v2|51|2|1|0.4|0.1", out _, out _, out _, out _, out _));
        Assert.True(Wo207Rules.TryParseTimer("v1|1|1|1|7|99", out _, out _, out _, out left, out rate));
        Assert.Equal((1f, 10f), (left, rate));   // clamped: a display, never a reason to run wild
    }

    [Fact]
    public void A_start_and_a_clear_go_at_once_a_running_timer_every_half_second()
    {
        Assert.True(Wo207Rules.TimerDue(4, 3, true, 1000, 990));
        Assert.True(Wo207Rules.TimerDue(5, 4, false, 1000, 990));
        Assert.False(Wo207Rules.TimerDue(4, 4, true, 1400, 1000));
        Assert.True(Wo207Rules.TimerDue(4, 4, true, 1500, 1000));
        Assert.False(Wo207Rules.TimerDue(5, 5, false, 9000, 1000));   // twin: an off timer is not sent again
    }

    // ---- B.1 the trace

    [Fact]
    public async Task The_trace_charges_a_handlers_calls_to_its_kind()
    {
        Wo207Trace.Reset();
        Wo207Trace.Enqueued(0x27); Wo207Trace.Enqueued(0x27); Wo207Trace.Enqueued(0x03);
        Assert.Equal(3, Wo207Trace.WaitingTotal);
        Wo207Trace.Dequeued(0x27);
        Assert.Contains("0x27=1", Wo207Trace.WaitingLine());
        Wo207Trace.Kind.Value = 0x27;
        await Task.Run(() => Wo207Trace.Lua(Wo207Trace.Kind.Value, 900, 30));   // the AsyncLocal flows into the awaited call
        Wo207Trace.Pipe(Wo207Trace.Kind.Value, 0x9A00 | 0x5B, 12, 8);
        Wo207Trace.Frame(0x27, 40, 950);
        Wo207Trace.Kind.Value = 0;
        string line = Wo207Trace.TakeLine();
        Assert.Contains("0x27:1/40/40/950/950 lua=1/900/900/30 pipe=1/12/12/8", line);
        Assert.Contains("0x9A.5B:1/12/12/8", line);
        Assert.DoesNotContain("0x27:", Wo207Trace.TakeLine());   // taken: the next window starts empty
        Assert.Equal("act7", Wo207Trace.KindName(Wo207Trace.ActionKindBase + 7));
        Wo207Trace.Dequeued(0x99);   // twin: a kind never counted in does not go below zero
        Assert.DoesNotContain("0x99", Wo207Trace.WaitingLine());
    }

    [Fact]
    public void A_twin_switch_list_names_switches_exactly()
    {
        Assert.True(Wo207Rules.TwinOff("queue_stale_drop, queue_natives", "queue_natives"));
        Assert.False(Wo207Rules.TwinOff("queue_stale_drop", "queue_stale"));
        Assert.False(Wo207Rules.TwinOff(null, "x"));
    }

    // ---- B.2 nothing stale reaches the game

    private static byte[] Action(byte ghost, ActionKind kind, uint evMs)
    {
        var body = BitConverter.GetBytes(evMs).Concat(new byte[] { 1, 2, 3 }).ToArray();
        return new byte[] { ghost, (byte)kind, 1, 0, 0, 0, 0, 0, 0, (byte)body.Length }.Concat(body).ToArray();
    }

    [Fact]
    public void A_combat_action_older_than_its_senders_newest_position_by_more_than_a_second_is_stale()
    {
        Func<byte, uint?> pos = g => g == 1 ? 50_000u : null;
        Assert.Equal(2_500, Wo207Rules.ActionBehindMs(Action(1, ActionKind.Attack, 47_500), pos));
        Assert.True(Wo207Rules.ActionBehindMs(Action(1, ActionKind.NpcAttack, 47_500), pos) > Protocol.EventStaleMs);
        Assert.True(Wo207Rules.ActionBehindMs(Action(1, ActionKind.Attack, 49_500), pos) <= Protocol.EventStaleMs);   // fresh: kept
        Assert.Null(Wo207Rules.ActionBehindMs(Action(2, ActionKind.Attack, 1), pos));              // nothing known of that sender: kept
        Assert.Null(Wo207Rules.ActionBehindMs(Action(1, ActionKind.DoorState, 1), pos));          // twin: a door's state is never a moment
        Assert.Null(Wo207Rules.ActionBehindMs(Action(1, ActionKind.Attack, 0), pos));             // no stamp: kept
        Assert.Null(Wo207Rules.ActionBehindMs(new byte[] { 1, (byte)ActionKind.Attack, 0 }, pos)); // short: kept
    }

    // ---- B.3 the caller never waits for the game's console

    private sealed class SlowGame(int delayMs) : HttpMessageHandler
    {
        public int Calls;
        public readonly List<string> Run = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(delayMs, ct);
            lock (Run) Run.Add(Uri.UnescapeDataString(System.Text.RegularExpressions.Regex.Match(req.RequestUri!.Query, "command=([^&]*)").Groups[1].Value));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("ok") };
        }
    }

    private static string Npc(int i, int v) =>
        $"if KCD2MP_ApplyNpcState then KCD2MP_ApplyNpcState(\"npc_{i:D2}_with_a_long_name\",2176.110,2091.960,110.800,0.1921,100.0,0,0,{v},12345678) end";

    private static async Task<long> FillMs(bool decoupled, SlowGame g)
    {
        await using var t = new HttpGameTransport("http://localhost:1403", 5000, g) { Decoupled = decoupled };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int v = 0; v < 3; v++) for (int i = 0; i < 40; i++) await t.ExecuteAsync(Npc(i, v));   // 120 states of 40 bodies
        long ms = sw.ElapsedMilliseconds;
        await t.FlushAsync();
        return ms;
    }

    [Fact]
    public async Task A_full_queue_never_holds_the_caller_and_only_the_newest_state_of_a_body_goes()
    {
        var g = new SlowGame(150);
        long ms = await FillMs(true, g);
        Assert.True(ms < 150, $"the caller waited {ms} ms for a slow game");
        string all; lock (g.Run) all = string.Join("\n", g.Run);
        for (int i = 0; i < 40; i++) Assert.Contains($"npc_{i:D2}_with_a_long_name\",2176.110,2091.960,110.800,0.1921,100.0,0,0,2,", all);   // each body's newest
        Assert.True(g.Calls < 12, $"{g.Calls} commands: older states of a body were sent too");
    }

    [Fact]
    public async Task Twin_without_the_decoupling_the_caller_waits_for_the_game()
    {
        var g = new SlowGame(150);
        long ms = await FillMs(false, g);
        Assert.True(ms >= 150, $"the caller waited only {ms} ms");
    }

    // ---- D.2 the joiner's NPC triggers

    [Fact]
    public void An_area_trigger_on_a_named_npc_is_an_npc_trigger_and_one_on_the_player_never_is()
    {
        var idx = new QuestValueIndex();
        // the parent module names the souls; its child's triggers resolve against it (the henik case)
        idx.AddTriggers("Quests/Final/Barbora/trosecko/zaby.xml", "<SoulAsset Name=\"henik\" SharedSoulGuids=\"x\" />");
        idx.AddTriggers("Quests/Final/Barbora/trosecko/zaby/henik_hlida_majku.xml",
            "<AreaTrigger Name=\"areatrigger25\"><Asset Name=\"Souls\" Alias=\"henik\" /></AreaTrigger>" +
            "<AreaTrigger Name=\"areatrigger9\"><Asset Name=\"Souls\" Alias=\"player\" /></AreaTrigger>" +
            "<AreaTrigger Name=\"areatrigger10\"><Asset Name=\"Souls\" Alias=\"player_bohuta\" /></AreaTrigger>" +
            "<AreaTrigger Name=\"areatrigger11\"><Edge From=\"makearray3.Array\" To=\"Souls\" /></AreaTrigger>" +
            "<AreaTrigger Name=\"areatrigger12\"><Asset Name=\"Souls\" Alias=\"somebodyUndefined\" /></AreaTrigger>");
        // the same file names its own soul (the kovar case)
        idx.AddTriggers("Quests/Final/Barbora/trosecko/kovar.xml",
            "<SoulAsset Name=\"kovar\" SharedSoulGuids=\"y\" /><AreaTrigger Name=\"areatrigger55\" PositionY=\"-870\"><Asset Name=\"Souls\" Alias=\"kovar\" /><Asset Name=\"Areas\" Alias=\"kovarna_area\" /></AreaTrigger>");
        // twin: a soul named in a sibling module is not this trigger's
        idx.AddTriggers("Quests/Final/Barbora/kutnohorsko/x.xml", "<AreaTrigger Name=\"areatrigger1\"><Asset Name=\"Souls\" Alias=\"kovar\" /></AreaTrigger>");
        Assert.Equal(new[] { "Barbora.trosecko.kovar.areatrigger55", "Barbora.trosecko.zaby.henik_hlida_majku.areatrigger25" }, idx.NpcTriggerPaths());
    }

    // ---- C.3 a shared skip's end

    [Fact]
    public void A_held_skip_end_is_the_hosts_clock_now()
    {
        var at = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(717_000u + 150u, Wo207Rules.SkipEndTarget(717_000, at, at.AddSeconds(10), 15));   // held 10 s: the host's clock ran 150 s on
        Assert.Equal(717_000u, Wo207Rules.SkipEndTarget(717_000, at, at, 15));
        Assert.Equal(717_000u, Wo207Rules.SkipEndTarget(717_000, at, at.AddSeconds(-5), 15));          // a clock that went back: never before the end
        Assert.Equal(717_000u + 9_000u, Wo207Rules.SkipEndTarget(717_000, at, at.AddHours(2), 15));     // a stale hold: at most 10 minutes on
    }

    // ---- C.2 keep awake

    [Fact]
    public void The_keep_awake_flag_is_always_explicit()
    {
        Assert.Equal("--keep-awake", KeepAwakeSetting.AgentFlag(true));
        Assert.Equal("--no-keep-awake", KeepAwakeSetting.AgentFlag(false));
        var c = new ClientConfig();
        Assert.True(c.KeepAwake);   // an agent started without the flag keeps the game awake
        c.ApplyCommandLine(["--host", "127.0.0.1", "--no-keep-awake"]);
        Assert.False(c.KeepAwake);
        c.ApplyCommandLine(["--host", "127.0.0.1", "--keep-awake"]);
        Assert.True(c.KeepAwake);
        Assert.True(GameBridge.IsMenuSafeLua("if KCD2MP_W207KeepAwake then KCD2MP_W207KeepAwake(1, 1) end"));   // a joiner at its menu too
        Assert.Equal(BatchKind.Level, BatchPolicy.Classify("if KCD2MP_W207KeepAwake then KCD2MP_W207KeepAwake(1, 1) end").Kind);
    }

    // ---- B.5 the console's share of the queue

    [Fact]
    public void The_oldest_frame_driven_and_background_entries_are_told_apart()
    {
        var q = new BatchQueue();
        Assert.Equal((-1L, -1L), q.OldestEnqueuedMs());
        q.Add(new BatchQueue.Entry { Lua = "a()", Text = "a()", Kind = BatchKind.Edge, Encoded = 3, EnqueuedMs = 1_000, Kind207 = 0 });
        q.Add(new BatchQueue.Entry { Lua = "b()", Text = "b()", Kind = BatchKind.Edge, Encoded = 3, EnqueuedMs = 1_500, Kind207 = 0x27 });
        q.Add(new BatchQueue.Entry { Lua = "c()", Text = "c()", Kind = BatchKind.Edge, Encoded = 3, EnqueuedMs = 2_000, Kind207 = 0x6F });
        Assert.Equal((1_500L, 1_000L), q.OldestEnqueuedMs());   // the frame-driven oldest is b, the agent's own oldest is a
    }

    // ---- B.4 the queue's count

    [Fact]
    public void A_frame_taken_the_moment_it_is_written_leaves_nothing_counted()
    {
        Wo207Trace.Reset();
        // counted before the write, taken at once: nothing waits (counted after the write, 0x25 stayed at 1 all session)
        Wo207Trace.Enqueued(0x25); Wo207Trace.Dequeued(0x25);
        Assert.Equal(0, Wo207Trace.WaitingTotal);
        Assert.Equal("", Wo207Trace.WaitingLine());
        // a replayed frame taken twice (the load hold) never drives the count below nothing
        Wo207Trace.Dequeued(0x27);
        Wo207Trace.Enqueued(0x27); Wo207Trace.Enqueued(0x27);
        Assert.Equal(2, Wo207Trace.WaitingTotal);
        Assert.Equal("0x27=2", Wo207Trace.WaitingLine());
        Wo207Trace.Reset();
    }

    // WO-207 A.4: the DLL's body is [op][arguments] (the pipe's call puts the op first); its checks, as wo203_conv.cpp reads them
    private static byte[] Body(byte op, byte[] args) => [op, .. args];

    // op_speak's own checks (wo203_conv.cpp): null = a bad request; else the eid, flags, path and name it reads
    private static (uint Eid, byte Flags, string Path, string Name)? DllSpeak(byte[] b)
    {
        if (b.Length < 10) return null;
        int pl = b[9];
        if (pl == 0 || b.Length < 10 + pl) return null;
        byte flags = 0; string name = "";
        if (b.Length != 10 + pl)
        {
            if (b.Length < 12 + pl) return null;
            flags = b[10 + pl];
            int nl = b[11 + pl];
            if (b.Length != 12 + pl + nl || nl >= 64) return null;
            name = System.Text.Encoding.UTF8.GetString(b, 12 + pl, nl);
        }
        return (BitConverter.ToUInt32(b, 1), flags, System.Text.Encoding.UTF8.GetString(b, 10, pl), name);
    }

    [Fact]
    public void A_lines_voice_request_has_the_shape_the_DLL_reads_and_the_twin_does_not()
    {
        byte[] a = Wo207Rules.ConvSpeakArgs(0x3FF88, 2500, "dialog/tsem_suk_01.wem", Wo207Rules.SpeakMake)!;
        var r = DllSpeak(Body(93, Wo207Rules.ConvArgs(93, a, framed: true)));
        Assert.NotNull(r);
        Assert.Equal((0x3FF88u, Wo207Rules.SpeakMake, "dialog/tsem_suk_01.wem", ""), r!.Value);
        // the host listening: its own NPC by name
        var n = DllSpeak(Body(93, Wo207Rules.ConvArgs(93, Wo207Rules.ConvSpeakArgs(0, 2500, "p.wem", Wo207Rules.SpeakMake | Wo207Rules.SpeakByName, "ttkc_drozd")!, true)));
        Assert.Equal((0u, (byte)3, "p.wem", "ttkc_drozd"), n!.Value);
        // an older agent's request (no tail) still reads
        Assert.Equal((7u, (byte)0, "p.wem", ""), DllSpeak([93, 7, 0, 0, 0, 0, 0, 0, 0, 5, .. "p.wem"u8.ToArray()])!.Value);
        // the doubled op (the conv_op_frame twin): refused
        Assert.Null(DllSpeak(Body(93, Wo207Rules.ConvArgs(93, a, framed: false))));
        Assert.Null(Wo207Rules.ConvSpeakArgs(1, 1, ""));
        Assert.Null(Wo207Rules.ConvSpeakArgs(1, 1, new string('x', 201)));
        Assert.Null(Wo207Rules.ConvSpeakArgs(1, 1, "p", 0, new string('n', 61)));
    }

    [Fact]
    public void The_vote_mark_and_the_options_poll_read_their_own_arguments()
    {
        byte[] clear = Body(92, Wo207Rules.ConvArgs(92, [255, 0], framed: true));
        Assert.Equal(255, clear[1]);                                  // op_mark: index = body[1] (255 = clear), nameLen = body[2]
        Assert.Equal(3 + clear[2], clear.Length);
        byte[] mark = Body(92, Wo207Rules.ConvArgs(92, [1, 0], framed: true));
        Assert.Equal(1, mark[1]);
        byte[] oldClear = Body(92, Wo207Rules.ConvArgs(92, [255, 0], framed: false));
        Assert.NotEqual(3 + oldClear[2], oldClear.Length);            // the twin's clear: a bad request
        Assert.Equal(1, Body(91, Wo207Rules.ConvArgs(91, [1], framed: true))[1]);   // op_poll: the arm flag at body[1]
        Assert.Equal(0, Body(91, Wo207Rules.ConvArgs(91, [0], framed: true))[1]);
        Assert.Equal(91, Body(91, Wo207Rules.ConvArgs(91, [0], framed: false))[1]); // the twin: the op read as "arm"
    }
}

/// <summary>WO-207 B.3: a relay frame's pipe call goes before the background polls waiting at the gate (and the twin: in arrival order).</summary>
public class Wo207PipeTests
{
    private static async Task<int> FramePosition(bool framesFirst, int kind = 0x27)
    {
        string name = BenchInstance.PipeNameFor("t207" + Guid.NewGuid().ToString("N")[..8]);
        var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        var served = new List<byte>();
        try
        {
            var accept = server.WaitForConnectionAsync();
            await using var pipe = new CombatPipe(name) { FramesFirst = framesFirst };
            Assert.True(await pipe.EnsureConnectedAsync());
            await accept;
            var dll = Task.Run(async () =>
            {
                byte seq = 0;
                var head = new byte[3];
                for (int k = 0; k < 13; k++)
                {
                    int got = 0;
                    while (got < 3) got += await server.ReadAsync(head.AsMemory(got));
                    var body = new byte[head[1] | (head[2] << 8)];
                    got = 0;
                    while (got < body.Length) got += await server.ReadAsync(body.AsMemory(got));
                    lock (served) served.Add(body[0]);
                    await Task.Delay(15);   // one answer a frame
                    await server.WriteAsync(new byte[] { 0x9F, 4, 0, 1, seq++, body[0], 0 });
                }
            });
            var polls = Enumerable.Range(1, 12).Select(i => pipe.Wo138Async((byte)i, [])).ToList();   // the background polls
            await Task.Delay(30);
            await Task.Run(async () => { Wo207Trace.Kind.Value = kind; await pipe.Wo138Async(99, []); });   // a frame's call
            await Task.WhenAll(polls);
            await dll;
            lock (served) return served.IndexOf(99);
        }
        finally { server.Dispose(); }
    }

    [Fact]
    public async Task A_frames_call_goes_before_the_waiting_polls()
    {
        int at = await FramePosition(true);
        Assert.InRange(at, 0, 3);
    }

    [Fact]
    public async Task A_conversations_call_goes_before_the_waiting_polls_too()
    {
        // WO-207 A.1 (mp_conv_pipe_first): the camera pump and the options poll carry ConvKind -- a frame's priority at the gate
        Assert.Equal("conv", Wo207Trace.KindName(Wo207Trace.ConvKind));
        int at = await FramePosition(true, Wo207Trace.ConvKind);
        Assert.InRange(at, 0, 3);
    }

    [Fact]
    public async Task Twin_without_it_the_frames_call_waits_behind_every_poll()
    {
        int at = await FramePosition(false);
        Assert.True(at >= 10, $"served at {at}");
    }
}
