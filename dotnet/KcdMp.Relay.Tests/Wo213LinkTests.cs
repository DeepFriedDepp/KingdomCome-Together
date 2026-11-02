// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using KcdMp.Server.Features.ClientHandling;
using KcdMp.Server.Features.Faults;

namespace KcdMp.Relay.Tests;

// =============================================================================
// WO-213 F.1: the fault mode's "link" rule (LinkShaper) and A's measures. Every check has its twin (the same measurement with the
// rule or its switch off) and is one predicate true on the check and false on the twin, as WO-206's tests.
// =============================================================================

[Collection(Wo206FaultCollection.Name)]
public class Wo213LinkTests
{
    private static string Plan(string rulesJson) => $"{{ \"seed\": 213, \"rules\": [ {rulesJson} ] }}";

    private static byte[] Beat(string text)
    {
        var t = Encoding.ASCII.GetBytes(text);
        var p = new byte[3 + 2 + t.Length];
        p[0] = Protocol.StoryBeatUp;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(1), (ushort)(2 + t.Length));
        p[3] = 1;
        p[4] = (byte)t.Length;
        t.CopyTo(p, 5);
        return p;
    }

    private static async Task<(Peer Host, Peer Joiner)> PairAsync(FaultRelay relay)
    {
        var host = await Peer.ConnectAsync(relay.TcpPort, "w213-host");
        var joiner = await Peer.ConnectAsync(relay.TcpPort, "w213-joiner");
        await Task.Delay(150);
        return (host, joiner);
    }

    private static async Task<List<double>> ReadBeatTimesAsync(Peer p, int n, Stopwatch clock, TimeSpan timeout)
    {
        var got = new List<double>();
        var until = DateTime.UtcNow + timeout;
        while (got.Count < n)
        {
            var left = until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) break;
            try
            {
                var (type, _) = await p.ReadPacketAsync(left);
                if (type == Protocol.StoryBeatDown) got.Add(clock.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) { break; }
        }
        return got;
    }

    // ---------------------------------------------------------------- the rate

    private const int Rate = 6000, Sends = 60, BeatLen = 120;
    private static readonly string RatePlan = Plan($"{{ \"name\": \"slow\", \"kind\": \"link\", \"role\": \"joiner\", \"dir\": \"both\", \"bytes_per_s\": {Rate} }}");
    private static bool NearRate(double bps) => bps >= Rate * 0.8 && bps <= Rate * 1.25;

    private static async Task<double> MeasureRateAsync(FaultRelay relay)
    {
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        var clock = Stopwatch.StartNew();
        var reader = ReadBeatTimesAsync(joiner, Sends, clock, TimeSpan.FromSeconds(15));
        for (int i = 0; i < Sends; i++) await host.SendRawAsync(Beat(new string('x', BeatLen - 2) + $"{i:00}"));
        var got = await reader;
        Assert.Equal(Sends, got.Count);
        return (Sends - 1) * (3 + 3 + BeatLen) / Math.Max((got[^1] - got[0]) / 1000.0, 1e-3);
    }

    [Fact]
    public async Task Wo213Link_caps_the_joiners_link_near_its_rate()
    {
        await using var relay = await FaultRelay.StartAsync(RatePlan);
        double bps = await MeasureRateAsync(relay);
        Assert.True(NearRate(bps), $"throughput {bps:0} B/s, cap {Rate}");
        Assert.True(relay.Rule("slow").FramesHeld >= Sends / 2);
    }

    [Fact]
    public async Task Wo213Link_rate_twin_without_the_switch_is_not_capped()
    {
        await using var relay = await FaultRelay.StartAsync(RatePlan, env: false);
        Assert.False(NearRate(await MeasureRateAsync(relay)));
    }

    // ---------------------------------------------------------------- the latency

    private const int LatencyMs = 250;
    private static readonly string LatencyPlan = Plan($"{{ \"name\": \"far\", \"kind\": \"link\", \"role\": \"joiner\", \"dir\": \"down\", \"bytes_per_s\": 10000000, \"ms\": {LatencyMs} }}");

    private static async Task<double> MeasureOneWayAsync(FaultRelay relay)
    {
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        var clock = Stopwatch.StartNew();
        double sent = clock.Elapsed.TotalMilliseconds;
        var reader = ReadBeatTimesAsync(joiner, 1, clock, TimeSpan.FromSeconds(5));
        await host.SendRawAsync(Beat("hello"));
        var got = await reader;
        Assert.Single(got);
        return got[0] - sent;
    }

    [Fact]
    public async Task Wo213Link_delays_the_joiners_down_link_by_its_latency()
    {
        await using var relay = await FaultRelay.StartAsync(LatencyPlan);
        Assert.True(await MeasureOneWayAsync(relay) >= LatencyMs);
    }

    [Fact]
    public async Task Wo213Link_latency_twin_without_the_switch_arrives_sooner()
    {
        await using var relay = await FaultRelay.StartAsync(LatencyPlan, env: false);
        Assert.False(await MeasureOneWayAsync(relay) >= LatencyMs);
    }

    // ---------------------------------------------------------------- the send buffer: backpressure into the relay's own queue

    private static string BufferPlan(int buffer) => Plan($"{{ \"name\": \"buf\", \"kind\": \"link\", \"role\": \"joiner\", \"dir\": \"down\", \"bytes_per_s\": 3000, \"buffer_bytes\": {buffer} }}");

    /// <summary>The joiner's session queue (the 0.63.9 relay's) while the shaper drains: its depth right after a burst.</summary>
    private static async Task<int> QueueDepthAfterBurstAsync(FaultRelay relay)
    {
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        for (int i = 0; i < 40; i++) await host.SendRawAsync(Beat(new string('y', 100)));
        await Task.Delay(400);
        var handler = relay.App.Services.GetService(typeof(ClientHandler)) as ClientHandler;
        var js = handler!.GetClients().Single(c => c.IsReady && c.Name == "w213-joiner");
        var text = js.TakeQueueText();
        return int.Parse(System.Text.RegularExpressions.Regex.Match(text, @"depth=(\d+)").Groups[1].Value);
    }

    [Fact]
    public async Task Wo213Link_a_full_send_buffer_holds_the_writer_and_the_relays_queue_grows()
    {
        await using var relay = await FaultRelay.StartAsync(BufferPlan(1024));
        Assert.True(await QueueDepthAfterBurstAsync(relay) > 5);
    }

    [Fact]
    public async Task Wo213Link_buffer_twin_a_big_send_buffer_takes_the_burst_at_once()
    {
        await using var relay = await FaultRelay.StartAsync(BufferPlan(1 << 20));
        Assert.False(await QueueDepthAfterBurstAsync(relay) > 5);
    }

    // ---------------------------------------------------------------- C on the relay: latest wins and unreliable state under a shaped link

    private static byte[] NpcUp(string name, float x, ushort seq)
    {
        var nb = Encoding.UTF8.GetBytes(name);
        var p = new byte[1 + nb.Length + Protocol.NpcStateFixedTail];
        p[0] = (byte)nb.Length; nb.CopyTo(p, 1);
        BitConverter.TryWriteBytes(p.AsSpan(1 + nb.Length), x);
        BitConverter.TryWriteBytes(p.AsSpan(1 + nb.Length + 4), 2500f);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(1 + nb.Length + Protocol.NpcStateSeqOffset), seq);
        var f = new byte[3 + p.Length]; f[0] = Protocol.NpcStateUp; BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(1), (ushort)p.Length); p.CopyTo(f, 3);
        return f;
    }

    /// <summary>(NPC states the joiner got, beats it got): the host sends <paramref name="npcs"/> NPCs' states and 20 beats.</summary>
    private static async Task<(int States, int Beats)> CountAsync(FaultRelay relay, bool hello, int npcs, int perNpc)
    {
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        if (hello) { await joiner.SendRawAsync(Protocol.BuildLinkHelloUp(Protocol.LinkCapLatestWins | Protocol.LinkCapUnreliableState)); await Task.Delay(200); }
        for (int r = 0; r < perNpc; r++)
            for (int i = 0; i < npcs; i++) await host.SendRawAsync(NpcUp($"npc_w213_{i}", 2000 + r * 0.5f + i, (ushort)(r + 1)));
        for (int i = 0; i < 20; i++) await host.SendRawAsync(Beat($"beat{i:00}"));
        int states = 0, beats = 0;
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTime.UtcNow < until && beats < 20)
        {
            try
            {
                var (t, _) = await joiner.ReadPacketAsync(until - DateTime.UtcNow);
                if (t is Protocol.NpcStateDown or Protocol.NpcStateQDown) states++;
                if (t == Protocol.StoryBeatDown) beats++;
            }
            catch (OperationCanceledException) { break; }
        }
        await Task.Delay(300);
        while (true)
        {
            try { var (t, _) = await joiner.ReadPacketAsync(TimeSpan.FromMilliseconds(200)); if (t is Protocol.NpcStateDown or Protocol.NpcStateQDown) states++; }
            catch (OperationCanceledException) { break; }
        }
        return (states, beats);
    }

    private static readonly string LossyPlan = Plan("{ \"name\": \"lossy\", \"kind\": \"link\", \"role\": \"joiner\", \"dir\": \"down\", \"bytes_per_s\": 10000000, \"loss_pct\": 40 }");

    [Fact]
    public async Task Wo213Link_state_goes_unreliable_after_the_hello_and_every_event_still_arrives()
    {
        await using var relay = await FaultRelay.StartAsync(LossyPlan);
        var (states, beats) = await CountAsync(relay, hello: true, npcs: 60, perNpc: 1);
        Assert.Equal(20, beats);                    // events are reliable: the lost ones came again
        Assert.InRange(states, 1, 50);              // ~40 % of 60 state frames lost for good
    }

    [Fact]
    public async Task Wo213Link_unreliable_twin_without_the_hello_every_state_arrives()
    {
        await using var relay = await FaultRelay.StartAsync(LossyPlan);
        var (states, beats) = await CountAsync(relay, hello: false, npcs: 60, perNpc: 1);
        Assert.Equal(20, beats);
        Assert.Equal(60, states);                   // reliable: every one, late or not
    }

    private static readonly string SlowPlan = Plan("{ \"name\": \"slow2\", \"kind\": \"link\", \"role\": \"joiner\", \"dir\": \"down\", \"bytes_per_s\": 2000, \"buffer_bytes\": 1024 }");

    [Fact]
    public async Task Wo213Link_latest_wins_one_npcs_flood_reaches_the_slow_link_as_its_newest_states()
    {
        await using var relay = await FaultRelay.StartAsync(SlowPlan);
        var (states, _) = await CountAsync(relay, hello: false, npcs: 1, perNpc: 60);
        Assert.InRange(states, 1, 20);              // 60 samples of one NPC: the slots kept the newest while the link was busy
    }

    [Fact]
    public async Task Wo213Link_latest_wins_twin_link_sched_off_queues_every_sample()
    {
        string? was = Environment.GetEnvironmentVariable(W213Switch.EnvVar);
        Environment.SetEnvironmentVariable(W213Switch.EnvVar, "link_sched");
        try
        {
            await using var relay = await FaultRelay.StartAsync(SlowPlan);
            var (states, _) = await CountAsync(relay, hello: false, npcs: 1, perNpc: 60);
            Assert.True(states > 20, $"{states} states");   // the 0.63.9 queue: every sample, late
        }
        finally { Environment.SetEnvironmentVariable(W213Switch.EnvVar, was); }
    }

    // ---------------------------------------------------------------- the plan

    [Fact]
    public void Wo213Link_plan_takes_a_link_and_refuses_a_link_with_a_window()
    {
        var p = RelayFaultPlan.Parse(Plan("{ \"kind\": \"link\", \"bytes_per_s\": 131072, \"ms\": 35, \"jitter_ms\": 15, \"loss_pct\": 1.0, \"stall_per_min\": 3, \"stall_min_ms\": 300, \"stall_max_ms\": 1000 }"));
        Assert.Equal(FaultKind.Link, p.Rules[0].Kind);
        Assert.Equal(131072, p.Rules[0].Link!.BytesPerS);
        Assert.Equal(2 * 35 + 100, p.Rules[0].Link!.RtoMs);
        Assert.Throws<FormatException>(() => RelayFaultPlan.Parse(Plan("{ \"kind\": \"link\", \"bytes_per_s\": 1000, \"for_s\": 5 }")));
        Assert.Throws<FormatException>(() => RelayFaultPlan.Parse(Plan("{ \"kind\": \"link\", \"bytes_per_s\": 1000, \"loss_pct\": 80 }")));
        Assert.Throws<FormatException>(() => RelayFaultPlan.Parse(Plan("{ \"kind\": \"lag\", \"ms\": 10, \"loss_pct\": 1 }")));
    }

    // ---------------------------------------------------------------- the stalls (one direction's view; shared by both)

    [Fact]
    public void Wo213Link_stalls_hold_the_wire_about_as_often_as_asked()
    {
        var shape = new LinkShape(100000, 1 << 16, 0, 0, 0, 6, 200, 400, 100);   // 6 a minute, 200..400 ms
        var st = new LinkShaper.Stalls(shape, 1);
        int held = 0;
        for (double t = 0; t < 600_000; t += 10) if (st.Clear(t) > t) held++;
        double heldS = held * 10 / 1000.0;   // ~60 stalls x ~0.3 s = ~18 s of 600
        Assert.InRange(heldS, 9, 30);
        var none = new LinkShaper.Stalls(shape with { StallPerMin = 0 }, 1);
        Assert.Equal(123.0, none.Clear(123));
    }
}

/// <summary>WO-213 A.1/A.5: the kinds, the meter and the receiver's arrival numbers (pure).</summary>
public class Wo213MeasureTests
{
    [Fact]
    public void Kinds_name_frames_by_the_relay_tables_rows_and_their_side_channel_kinds()
    {
        Assert.Equal("pos", LinkKinds.Of(Protocol.Ghost, new byte[18]));
        Assert.Equal("npc-state", LinkKinds.Of(Protocol.NpcStateDown, new byte[40]));
        Assert.Equal("npc-anim", LinkKinds.Of(Protocol.NpcAnimDown, new byte[20]));
        Assert.Equal("action:NpcCombat", LinkKinds.Of(Protocol.ActionUp, [(byte)ActionKind.NpcCombat, 0, 0]));
        Assert.Equal("action:NpcCombat", LinkKinds.Of(Protocol.ActionDown, [3, (byte)ActionKind.NpcCombat, 0]));
        var w203up = new byte[Protocol.JoinHeaderLen + 2]; w203up[Protocol.JoinHeaderLen] = 12;
        Assert.Equal("w203:12", LinkKinds.Of(Protocol.W203Up, w203up));
        var w203down = new byte[1 + Protocol.JoinHeaderLen + 2]; w203down[1 + Protocol.JoinHeaderLen] = 12;
        Assert.Equal("w203:12", LinkKinds.Of(Protocol.W203Down, w203down));
        Assert.Equal("ping", LinkKinds.Of(Protocol.Pong, new byte[8]));
        Assert.True(LinkState.IsStateType(Protocol.NpcStateDown));
        Assert.False(LinkState.IsStateType(Protocol.StoryBeatDown));
    }

    [Fact]
    public void Meter_reports_bytes_per_second_per_kind_each_way()
    {
        var m = new LinkMeter();
        for (int i = 0; i < 10; i++) m.Count(true, "npc-state", 44);
        m.Count(false, "pos", 30);
        string line = m.TakeLine("test");
        Assert.Matches(@"out=npc-state:\d+/", line);
        Assert.Matches(@"in=pos:\d+/", line);
        Assert.Contains("out=-", m.TakeLine("test"));   // a new window
        Assert.Contains("npc-state", m.TotalLine("test", 10));
    }

    [Fact]
    public void Arrival_measures_per_body_spacing_and_the_age_past_the_fastest_arrival()
    {
        var a = new ArrivalStats();
        long f = Stopwatch.Frequency, t0 = 1_000 * f;
        // one body every 100 ms, sender stamps 100 ms apart, the third one 300 ms late (queued)
        uint s = 50_000;
        a.Arrive("npc-state", "a", t0, s, 1);
        a.Arrive("npc-state", "a", t0 + f / 10, s + 100, 1);
        a.Arrive("npc-state", "a", t0 + f / 10 * 2 + f * 3 / 10, s + 200, 1);
        for (int i = 0; i < 5; i++) a.Arrive("npc-state", "b", t0 + f / 10 * i, s + (uint)(100 * i), 1);
        var line = Assert.Single(a.TakeLines("t"));
        Assert.Matches(@"ia_p50=100 ", line);
        Assert.Matches(@"age_p99=30\d ", line);
        Assert.Empty(a.TakeLines("t"));
    }
}

/// <summary>WO-213 C/D: the scheduler (pure). Each check with its twin (the switch off).</summary>
public class Wo213SchedulerTests
{
    private static byte[] NpcDown(byte src, string name, float x, ushort seq = 1)
    {
        var nb = Encoding.UTF8.GetBytes(name);
        var p = new byte[2 + nb.Length + Protocol.NpcStateFixedTail];
        p[0] = src; p[1] = (byte)nb.Length; nb.CopyTo(p, 2);
        BitConverter.TryWriteBytes(p.AsSpan(2 + nb.Length), x);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2 + nb.Length + Protocol.NpcStateSeqOffset), seq);
        return Frame(Protocol.NpcStateDown, p);
    }

    private static byte[] Frame(byte type, byte[] p)
    {
        var f = new byte[3 + p.Length];
        f[0] = type; BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(1), (ushort)p.Length); p.CopyTo(f, 3);
        return f;
    }

    private static byte[] Event(int i) => Frame(Protocol.StoryBeatDown, [0, 1, 1, (byte)i]);

    /// <summary>An NpcAnim down row: [src][target][joinId:4][count] bodies [nl][name][cm][nsc=0].</summary>
    private static byte[] Anim(params string[] names)
    {
        var ms = new MemoryStream();
        ms.WriteByte(0); ms.WriteByte(1); ms.Write(new byte[4]); ms.WriteByte((byte)names.Length);
        foreach (var n in names) { var b = Encoding.UTF8.GetBytes(n); ms.WriteByte((byte)b.Length); ms.Write(b); ms.WriteByte(0); ms.WriteByte(0); }
        return Frame(Protocol.NpcAnimDown, ms.ToArray());
    }

    private static List<byte[]> Drain(LinkScheduler s, int budget = int.MaxValue, int pending = -1)
    {
        var o = new List<byte[]>();
        while (s.TryTake(budget, pending, out var f, out _)) o.Add(f);
        return o;
    }

    [Fact]
    public void Latest_wins_one_slot_per_body_and_twin_queues_every_sample()
    {
        var s = new LinkScheduler();
        for (int i = 0; i < 10; i++) s.Enqueue(NpcDown(0, "npc_a", i));
        var got = Drain(s);
        Assert.Single(got);
        Assert.Equal(9f, BitConverter.ToSingle(got[0], 3 + 2 + 5));
        Assert.Equal(9, s.Coalesced);
        var twin = new LinkScheduler(new LinkSchedOptions { LatestWins = false });
        for (int i = 0; i < 10; i++) twin.Enqueue(NpcDown(0, "npc_a", i));
        Assert.Equal(10, Drain(twin).Count);
    }

    [Fact]
    public void Events_go_first_in_order_and_are_never_merged_or_dropped()
    {
        var s = new LinkScheduler(new LinkSchedOptions { AgeLimitMs = 1 });
        s.Enqueue(NpcDown(0, "npc_a", 1));
        for (int i = 0; i < 5; i++) s.Enqueue(Event(i));
        Thread.Sleep(20);
        var got = Drain(s);
        Assert.Equal(5, got.Count);   // the state aged out (C.3), every event went
        for (int i = 0; i < 5; i++) Assert.Equal(i, got[i][6]);
        Assert.Equal(1, s.StaleDropped);
    }

    [Fact]
    public void Age_limit_drops_old_state_and_twin_sends_it_late()
    {
        var s = new LinkScheduler(new LinkSchedOptions { AgeLimitMs = 30 });
        s.Enqueue(NpcDown(0, "npc_a", 1));
        Thread.Sleep(80);
        Assert.Empty(Drain(s));
        var twin = new LinkScheduler(new LinkSchedOptions { AgeLimitMs = 0 });
        twin.Enqueue(NpcDown(0, "npc_a", 1));
        Thread.Sleep(80);
        Assert.Single(Drain(twin));
    }

    [Fact]
    public void Batched_rows_split_per_body_and_merge_back_with_the_newest_of_each()
    {
        var s = new LinkScheduler();
        s.Enqueue(Anim("a", "b"));
        s.Enqueue(Anim("b", "c"));
        var got = Assert.Single(Drain(s));
        Assert.Equal(Protocol.NpcAnimDown, got[0]);
        Assert.Equal(3, got[3 + 1 + Protocol.JoinHeaderLen]);   // a, b (the newer), c in one row
        var pieces = new List<StatePiece>();
        Assert.True(LinkStateCodec.Split(got, pieces));
        Assert.Equal(new[] { "a", "b", "c" }, pieces.Select(p => p.Body).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Activity_extras_are_events_reliable_in_order_never_repeated()
    {
        var pieces = new List<StatePiece>();
        var oneShot = Frame(Protocol.ActivityExtraDown, [0, 1, 0, 0, 0, 0, Protocol.ExtraKindOneShot, 1, 1, (byte)'a', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.False(LinkStateCodec.Split(oneShot, pieces));
        var hands = new byte[1 + Protocol.JoinHeaderLen + 2 + 1 + 1 + 32];
        hands[1 + Protocol.JoinHeaderLen] = Protocol.ExtraKindHands; hands[2 + Protocol.JoinHeaderLen] = 1; hands[3 + Protocol.JoinHeaderLen] = 1; hands[4 + Protocol.JoinHeaderLen] = (byte)'a';
        Assert.False(LinkStateCodec.Split(Frame(Protocol.ActivityExtraDown, hands), pieces));   // the first build: a slot, repeated every 3 s
        Assert.False(LinkState.IsStateType(Protocol.ActivityExtraDown));
        var s = new LinkScheduler(new LinkSchedOptions { Unreliable = true, RepeatMs = 30, RepeatExtraMs = 30 });
        s.Enqueue(Frame(Protocol.ActivityExtraDown, hands));
        Assert.Single(Drain(s));
        Thread.Sleep(80);
        Assert.Empty(Drain(s));   // never repeated
        Assert.Equal(1, s.EventsSent);
    }

    [Fact]
    public void The_receiver_leaves_out_a_posture_repeat_it_applied_and_takes_the_hosts_resend()
    {
        var f = new LinkRepeatFilter { WindowMs = 1500 };
        static byte[] P(params string[] n) => Anim(n)[3..];
        var original = P("a", "b");
        Assert.Same(original, f.Filter(Protocol.NpcAnimDown, original, 0));                 // the original: applied as it came
        Assert.Null(f.Filter(Protocol.NpcAnimDown, P("a", "b"), 1000));                       // the relay's repeat 1 s later: nothing left
        var mixed = f.Filter(Protocol.NpcAnimDown, P("a", "c"), 1100);                         // a repeat beside a new body: only the new one
        Assert.NotNull(mixed);
        var pieces = new List<StatePiece>();
        Assert.True(LinkStateCodec.Split([Protocol.NpcAnimDown, (byte)mixed!.Length, (byte)(mixed.Length >> 8), .. mixed], pieces));
        Assert.Equal(new[] { "c" }, pieces.Select(x => x.Body).ToArray());
        Assert.NotNull(f.Filter(Protocol.NpcAnimDown, P("a", "b"), 2000));                    // the host's own resend 2 s after: applied
        Assert.Equal(3, f.Dropped);
        // a repeat whose original was lost differs from the last applied: it passes
        var g = new LinkRepeatFilter();
        Assert.NotNull(g.Filter(Protocol.NpcAnimDown, P("a"), 0));
        Assert.NotNull(g.Filter(Protocol.NpcAnimDown, Anim2("a")[3..], 500));
        // another type passes untouched (NPC states are not repeated; each has its own sequence)
        var state = NpcDown(0, "a", 1)[3..];
        Assert.Same(state, g.Filter(Protocol.NpcStateDown, state, 600));
        // the twin (no filter on the joiner): each of those repeats was one more owner stream-in, a main-thread pipe call each
    }

    /// <summary>An NpcAnim down row whose bodies carry one scope (a different posture than <see cref="Anim"/>'s).</summary>
    private static byte[] Anim2(params string[] names)
    {
        var ms = new MemoryStream();
        ms.WriteByte(0); ms.WriteByte(1); ms.Write(new byte[4]); ms.WriteByte((byte)names.Length);
        foreach (var n in names) { var b = Encoding.UTF8.GetBytes(n); ms.WriteByte((byte)b.Length); ms.Write(b); ms.WriteByte(1); ms.WriteByte(1); ms.Write(new byte[Protocol.NpcAnimScopeLen]); }
        return Frame(Protocol.NpcAnimDown, ms.ToArray());
    }

    [Fact]
    public void Repeats_restate_unchanged_owner_state_only_while_unreliable()
    {
        var s = new LinkScheduler(new LinkSchedOptions { Unreliable = true, RepeatMs = 30 });
        s.Enqueue(NpcDown(0, "a", 1));
        s.Enqueue(Anim("a"));
        Assert.Equal(2, Drain(s).Count);
        Thread.Sleep(60);
        var again = Drain(s);
        Assert.Single(again);
        Assert.Equal(Protocol.NpcAnimDown, again[0][0]);
        Assert.Equal(1, s.Repeats);
        var twin = new LinkScheduler(new LinkSchedOptions { Unreliable = false, RepeatMs = 30 });
        twin.Enqueue(NpcDown(0, "a", 1));
        twin.Enqueue(Anim("a"));
        Drain(twin);
        Thread.Sleep(60);
        Assert.Empty(Drain(twin));
    }

    [Fact]
    public void Bands_send_the_fighter_before_the_far_body()
    {
        var s = new LinkScheduler { BandOf = p => p.Body == "fighter" ? 0 : 3 };
        s.Enqueue(NpcDown(0, "far", 1));
        s.Enqueue(NpcDown(0, "fighter", 1));
        Drain(s);
        Thread.Sleep(40);
        s.Enqueue(NpcDown(0, "far", 2));
        s.Enqueue(NpcDown(0, "fighter", 2));
        var got = Drain(s);
        Assert.Contains("fighter", Encoding.UTF8.GetString(got[0]));
    }

    [Fact]
    public void Budget_and_a_full_path_hold_state_in_its_slot_but_never_an_event()
    {
        var s = new LinkScheduler();
        s.Enqueue(NpcDown(0, "a", 1));
        Assert.Empty(Drain(s, budget: 10));                 // the frame is bigger than the budget
        Assert.Empty(Drain(s, pending: 64 * 1024));          // the path already holds too much
        s.Enqueue(Event(1));
        Assert.Single(Drain(s, budget: 0, pending: 1 << 20)); // the event goes anyway
        Assert.Single(Drain(s));                              // and the state once there is room
        Assert.True(s.BudgetWaits >= 1 && s.PendingWaits >= 1);
    }

    /// <summary>A path the test steers: its own state lane or not, and how full the events' lane is.</summary>
    private sealed class LanePath(bool ownLane) : ILinkPath
    {
        public readonly List<(byte Type, bool State)> Sent = new();
        public int EventPending;
        public string Transport => "test";
        public bool CanSendUnreliable => ownLane;
        public bool StateOwnLane => ownLane;
        public ValueTask SendAsync(ReadOnlyMemory<byte> frame, bool state, CancellationToken ct) { Sent.Add((frame.Span[0], state)); return ValueTask.CompletedTask; }
        public LinkPathStatus Status() => new(true, 256 * 1024, 0, 0) { EventPendingBytes = EventPending };
    }

    [Fact]
    public async Task With_a_state_lane_the_state_goes_beside_an_event_burst_and_twin_one_lane_after_it()
    {
        // Steam's two lanes: the events' lane holds a world transfer's worth -- the events wait here, the state goes now (never dropped)
        var lane = new LanePath(ownLane: true) { EventPending = 200 * 1024 };
        var w = new LinkWriter(new LinkScheduler(new LinkSchedOptions { Unreliable = true }), lane);
        for (int i = 0; i < 5; i++) w.Enqueue(Event(i));
        w.Enqueue(NpcDown(0, "a", 1));
        await w.PumpOnceAsync(CancellationToken.None);
        Assert.Equal([(Protocol.NpcStateDown, true)], lane.Sent);
        lane.EventPending = 0;                                // the lane drained: the events, in order
        await w.PumpOnceAsync(CancellationToken.None);
        Assert.Equal(6, lane.Sent.Count);
        Assert.All(lane.Sent.Skip(1), x => Assert.Equal(Protocol.StoryBeatDown, x.Type));
        // the twin (Steam's defaults: one lane) -- the events first, the state behind the burst
        var one = new LanePath(ownLane: false) { EventPending = 200 * 1024 };
        var w1 = new LinkWriter(new LinkScheduler(), one);
        for (int i = 0; i < 5; i++) w1.Enqueue(Event(i));
        w1.Enqueue(NpcDown(0, "a", 1));
        await w1.PumpOnceAsync(CancellationToken.None);
        Assert.Equal(Protocol.StoryBeatDown, one.Sent[0].Type);
    }
}

/// <summary>WO-213 D.3: the compact NPC state, relay to client and back (pure).</summary>
public class Wo213CompactTests
{
    private static byte[] Down(string name, float x, float y, float z, float rot, float hp, byte flags, ushort seq)
    {
        var p = NpcCompact.BuildDown(2, name, new NpcCompact.Fields(x, y, z, rot, hp, flags, seq, 1000u + seq));
        var f = new byte[3 + p.Length]; f[0] = Protocol.NpcStateDown; BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(1), (ushort)p.Length); p.CopyTo(f, 3);
        return f;
    }

    [Fact]
    public void Round_trip_within_a_centimetre_and_deltas_are_smaller()
    {
        var enc = new NpcCompact.Encoder(); var dec = new NpcCompact.Decoder();
        var sizes = new List<int>();
        for (ushort i = 0; i < 8; i++)
        {
            var full = Down("ttkc_man_3", 2003.217f + i * 0.07f, 2497.361f, 114.92f, 1.2345f, 87.5f, 0x04, i);
            var c = enc.Encode(full);
            Assert.Equal(Protocol.NpcStateQDown, c[0]);
            sizes.Add(c.Length);
            var back = dec.Decode(c.AsSpan(3));
            Assert.NotNull(back);
            Assert.True(NpcCompact.TryReadDown(back, out byte src, out string name, out var f));
            Assert.Equal(2, src); Assert.Equal("ttkc_man_3", name);
            Assert.InRange(f.X, 2003.207f + i * 0.07f, 2003.227f + i * 0.07f);
            Assert.InRange(f.Z, 114.90f, 114.94f);
            Assert.InRange(f.Rot, 1.232f, 1.237f);
            Assert.Equal(87.5f, f.Hp); Assert.Equal(0x04, f.Flags); Assert.Equal(i, f.Seq); Assert.Equal(1000u + i, f.SenderMs);
        }
        Assert.True(sizes[^1] < Down("ttkc_man_3", 0, 0, 0, 0, 0, 0, 0).Length / 2, $"a delta is {sizes[^1]} bytes");
    }

    [Fact]
    public void A_lost_first_message_is_healed_by_a_later_full_one()
    {
        var enc = new NpcCompact.Encoder(); var dec = new NpcCompact.Decoder();
        enc.Encode(Down("a", 10, 10, 10, 0, 50, 0, 1));   // lost on the way (the name, the first full message)
        enc.Encode(Down("a", 10, 10, 10, 0, 50, 0, 2));
        enc.Encode(Down("a", 10, 10, 10, 0, 50, 0, 3));
        var delta = enc.Encode(Down("a", 11, 10, 10, 0, 50, 0, 4));
        Assert.Null(dec.Decode(delta.AsSpan(3)));          // no name for the id yet: dropped, counted
        Assert.Equal(1, dec.UnknownId);
        Thread.Sleep(NpcCompact.FullEveryMs + 50);
        var full = enc.Encode(Down("a", 12, 10, 10, 0, 50, 0, 5));
        Assert.NotNull(dec.Decode(full.AsSpan(3)));
    }

    [Fact]
    public void A_body_outside_the_format_goes_full_size()
    {
        var enc = new NpcCompact.Encoder();
        var f = Down("a", -5, 10, 10, 0, 50, 0, 1);
        Assert.Same(f, enc.Encode(f));
    }
}
