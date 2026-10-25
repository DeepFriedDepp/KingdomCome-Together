// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-207 B (docs/WO-207-findings.md): the queue that never drained. B.1 measures it (Wo207Trace): once a second with a partner,
// MP-W207-Q lag_ms=<how far the processor trails the reader> waiting=<frames in the queue> by=<the kinds waiting>; every 10 s
// MP-W207-QK with the busiest kinds' wait, handler time, Lua queue/answer and pipe gate/round trip.
public partial class GameBridge
{
    private readonly record struct W207Frame(int Type, long Arrival, long Start);

    private W207Frame Wo207Begin(InFrame f)
    {
        Wo207Trace.Dequeued(f.Type);
        Wo207Trace.Kind.Value = f.Type;
        return new W207Frame(f.Type, f.Arrival, Stopwatch.GetTimestamp());
    }

    private static void Wo207End(W207Frame w)
    {
        long end = Stopwatch.GetTimestamp();
        long wait = w.Arrival > 0 ? Math.Max(0, (w.Start - w.Arrival) * 1000 / Stopwatch.Frequency) : 0;
        Wo207Trace.Frame(Wo207Trace.Kind.Value is int k && k != 0 ? k : w.Type, wait, (end - w.Start) * 1000 / Stopwatch.Frequency);
        Wo207Trace.Kind.Value = 0;
    }

    /// <summary>The 4 Hz session tick: the queue's line every second, the kinds' line every 10 s (with a partner only); C.2's word to
    /// the mod every 5 s (the session runs).</summary>
    private void Wo207Tick(long n)
    {
        if (n % 20 == 1) _ = ExecLuaAsync(Wo207KeepAwakeLua(true));
        if (n % 4 == 2) Wo207NpcTrigTick();   // D.2
        if (n % 4 != 0 || LivePartners().Count == 0) return;
        // B.5: the processor's lag, and downstream of it the console's and the pipe's oldest frame-driven waits (bg = the agent's own loops)
        var (luaF, luaB) = _httpForMenu is { } h ? h.OldestWaitMs() : (0, 0);
        var (pipeF, pipeB) = _combat.OldestGateWaitMs();
        Console.WriteLine($"MP-W207-Q lag_ms={Wo202QueueLagMs()} waiting={Wo207Trace.WaitingTotal} by={Wo207Trace.WaitingLine()} lua_ms={luaF} pipe_ms={pipeF} lua_bg_ms={luaB} pipe_bg_ms={pipeB}");
        if (n % 40 == 0) Console.WriteLine("MP-W207-QK" + Wo207Trace.TakeLine());
        if (n % 240 == 60) Console.WriteLine($"MP-W207-STATS {Wo207LaneStats()} keep_awake={(config.KeepAwake ? "on" : "off")} {Wo207TimerStats()} stale_dropped={Interlocked.Read(ref _w207StaleDropped)} w121_stale={_w121EvStale} decouple={(W207("queue_decouple") ? "on" : "off")}");
    }

    // ---------------------------------------------------------------- C.2 keep the game running behind another window

    /// <summary>The mod's mp_keep_awake word: the session runs (or not), with the launcher's choice (KeepAwakeSetting).</summary>
    private string Wo207KeepAwakeLua(bool live) =>
        $"if KCD2MP_W207KeepAwake then KCD2MP_W207KeepAwake({(live ? 1 : 0)}, {(config.KeepAwake ? 1 : 0)}) end";

    /// <summary>"CSystem::Quit invoked": the player's own value back now (the engine may still run one console call).</summary>
    private async Task Wo207OnQuitAsync()
    {
        try { await _transport.ExecuteNowAsync(Wo207KeepAwakeLua(false)); }
        catch (Exception ex) { Console.WriteLine($"WO207-AWAKE the release at quit did not send: {ex.GetType().Name}"); }
    }
}

// WO-207 A.3: the decision timer. The starter's DLL notes his HUD's SetDialogTimeLimit / UpdateDialogTimeLimit / ClearDialogTimeLimit
// (op 91's tail); while someone listens, a start and a clear go out at once and a running timer every 500 ms (ConvTimer, kind 13), as the
// fraction left and how fast it falls. The listener draws the same timer on his own HUD and runs it on between messages. It is only a
// display: the starter's engine decides when it expires, and a vote never holds it.   WO207-TIMER ...
public partial class GameBridge
{
    private long _w207TimersOut, _w207TimersIn;

    private async Task Wo207TimerOutAsync(W204ConvState c, Wo207Rules.TimerPoll? t, byte[] to)
    {
        if (t is not { } tp || to.Length == 0) return;
        string? text = null;
        long now = Environment.TickCount64;
        lock (c)
        {
            if (!Wo207Rules.TimerDue(tp.Seq, c.TimerSeq, tp.On, now, c.TimerSentMs)) return;
            if (tp.Seq != c.TimerSeq)
                Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"WO207-TIMER conversation {c.Cid} decision {c.DecId}: the timer {(tp.On ? "started" : "cleared")} on this player's HUD -> {to.Length} listener(s)"));
            c.TimerSeq = tp.Seq; c.TimerSentMs = now;
            text = Wo207Rules.TimerText(c.Cid, c.DecId, tp.On, tp.LeftNow, tp.RatePerS);
        }
        Interlocked.Increment(ref _w207TimersOut);
        foreach (byte p in to) await Wo203SendConvAsync(p, Protocol.ConvTimer, text);
    }

    private string Wo207TimerStats() => $"timers_out={Interlocked.Read(ref _w207TimersOut)} timers_in={Interlocked.Read(ref _w207TimersIn)}";
}

// WO-207: the mod's events.
public partial class GameBridge
{
    private void Wo207OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w207_icon":   // A.4: the partner's vote showed in this player's own rows (the agent's clock: the bench compares it with the vote's)
                Console.WriteLine($"WO207-VOTE icon shown in this player's options: {(arg == "clear" ? "cleared" : "row " + arg)}");
                return;
            case "w207_cfg":    // mp_keep_awake (C.2), and the B/D switches
                Wo207OnCfg(arg);
                return;
            case "w207_status":
                Console.WriteLine($"MP-W207-STATS {Wo207LaneStats()} keep_awake={(config.KeepAwake ? "on" : "off")} {Wo207TimerStats()} stale_dropped={Interlocked.Read(ref _w207StaleDropped)} w121_stale={_w121EvStale} decouple={(W207("queue_decouple") ? "on" : "off")}");
                return;
        }
    }
}

// WO-207: the switches the agent reads (the mod's mp_* commands send "name=on|off" as w207_cfg). New mechanisms ship on.
public partial class GameBridge
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _w207On = new(StringComparer.Ordinal);

    /// <summary>A WO-207 switch: on unless the mod said off (a test twin's KCDMP_TEST_W207_OFF=name,name turns it off from the start).</summary>
    private bool W207(string name) =>
        _w207On.TryGetValue(name, out bool v) ? v : !Wo207Rules.TwinOff(Environment.GetEnvironmentVariable("KCDMP_TEST_W207_OFF"), name);

    private void Wo207OnCfg(string? arg)
    {
        int eq = (arg ?? "").IndexOf('=');
        if (eq <= 0) { Console.WriteLine($"MP-W207 cfg refused: '{arg}'"); return; }
        string name = arg![..eq], val = arg[(eq + 1)..];
        if (val is not ("on" or "off")) { Console.WriteLine($"MP-W207 cfg refused: '{arg}'"); return; }
        _w207On[name] = val == "on";
        Console.WriteLine($"MP-W207 cfg {name}={val}");
        if (name == "queue_decouple" && _httpForMenu is { } http) http.Decoupled = val == "on";
        if (name == "queue_decouple") _combat.FramesFirst = val == "on";
        if (name == "joiner_npc_triggers") _w207NpcTrigDue = true;   // D.2: on sends the list again, off forgets it
    }
}

// WO-207 A.1: the conversation lane (mp_conv_lane, on). The pair's joined conversation failed where the synthetic partner never could:
// the listener's agent queue fell 3-5 s behind its NPC traffic, the camera samples behind it, and the listener's DLL ended the view for
// silence ("WO203B-VIEW the DLL ended the view ... by itself (silence)", talk4). Every ConvDown frame now goes from the reader to this
// lane, in order, never behind another kind. Off: through the frame processor as before (the twin).
public partial class GameBridge
{
    private long _w207ConvLaneFrames, _w207ConvLaneMaxWaitMs;

    private async Task Wo207ConvLaneAsync(ChannelReader<InFrame> lane, CancellationToken ct)
    {
        try
        {
            await foreach (var f in lane.ReadAllAsync(ct))
            {
                if (Wo140DropSeparate(f.Type)) continue;   // WO-140: a joiner in its own world
                Interlocked.Increment(ref _w207ConvLaneFrames);
                long wait = (Stopwatch.GetTimestamp() - f.Arrival) * 1000 / Stopwatch.Frequency;
                if (wait > Interlocked.Read(ref _w207ConvLaneMaxWaitMs)) Interlocked.Exchange(ref _w207ConvLaneMaxWaitMs, wait);
                // the listener's camera samples and lines go into its DLL first at the pipe's gate (mp_conv_pipe_first)
                Wo207Trace.Kind.Value = W207("conv_pipe_first") ? Wo207Trace.ConvKind : 0;
                try { await Wo123OnFrameAsync(f.Type, f.Payload, ct); }
                catch (Exception ex) { Console.WriteLine($"MP-W207 conv lane: a frame failed: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private string Wo207LaneStats() => $"conv_lane={(W207("conv_lane") ? "on" : "off")} conv_lane_frames={Interlocked.Read(ref _w207ConvLaneFrames)} conv_lane_max_wait_ms={Interlocked.Exchange(ref _w207ConvLaneMaxWaitMs, 0)}"
        + $" activity_lane_frames={Interlocked.Read(ref _w207ActLaneFrames)} activity_lane_max_wait_ms={Interlocked.Exchange(ref _w207ActLaneMaxWaitMs, 0)}";

    // B.3: the activity lane -- WO-141's activity rows and WO-143's extras (each row a native write: ~4.6 pipe calls a frame, 140-160 ms a
    // frame on the bench's joiner), in order, in their own lane. Charged to their kind in the trace.
    private long _w207ActLaneFrames, _w207ActLaneMaxWaitMs;

    private async Task Wo207ActivityLaneAsync(ChannelReader<InFrame> lane, CancellationToken ct)
    {
        try
        {
            await foreach (var f in lane.ReadAllAsync(ct))
            {
                if (Wo140DropSeparate(f.Type)) continue;   // WO-140: a joiner in its own world
                Interlocked.Increment(ref _w207ActLaneFrames);
                long wait = (Stopwatch.GetTimestamp() - f.Arrival) * 1000 / Stopwatch.Frequency;
                if (wait > Interlocked.Read(ref _w207ActLaneMaxWaitMs)) Interlocked.Exchange(ref _w207ActLaneMaxWaitMs, wait);
                Wo207Trace.Kind.Value = f.Type;
                long t0 = Stopwatch.GetTimestamp();
                try { await Wo123OnFrameAsync(f.Type, f.Payload, ct); }
                catch (Exception ex) { Console.WriteLine($"MP-W207 activity lane: a frame failed: {ex.GetType().Name}: {ex.Message}"); }
                Wo207Trace.Frame(f.Type, wait, (Stopwatch.GetTimestamp() - t0) * 1000 / Stopwatch.Frequency);
                Wo207Trace.Kind.Value = 0;
            }
        }
        catch (OperationCanceledException) { }
    }
}

// WO-207 B.2: nothing stale reaches the game (mp_queue_drop_stale, on). A state already waits once per body (WO-202 III.1's coalescing,
// WO-147's superseded skip) and a Lua state once per key (the batch queue). A combat action whose own moment is already further behind
// its sender's newest position than Protocol.EventStaleMs is dropped here, at dequeue, before any handler -- counted per kind
// (MP-W207-QK drop=), never handed to the game to be judged there.
public partial class GameBridge
{
    private long _w207StaleDropped;

    private bool Wo207DropStale(in InFrame f)
    {
        if (f.Type != Protocol.ActionDown || !W207("queue_drop_stale")) return false;
        var p = f.Payload;
        if (Wo207Rules.ActionBehindMs(p, g => _ghostLastSenderMs.TryGetValue(g, out uint ms) ? ms : null) is not int behind || behind <= Protocol.EventStaleMs) return false;
        long n = Interlocked.Increment(ref _w207StaleDropped);
        Wo207Trace.Dropped(Wo207Trace.ActionKindBase + p[1]);
        if (n <= 20 || n % 100 == 0)
            Console.WriteLine($"MP-W207 stale dropped at dequeue: ghost {p[0]} {(ActionKind)p[1]} {behind} ms behind its sender's newest position [n={n}]");
        return true;
    }
}

// WO-207 D.2: the joiner's NPC triggers (mp_joiner_npc_triggers, on). WO-206 C1 named two States the joiner's own logic set
// (trosecko.kovar.KovarJeVKovarne at each change, zaby.henik_hlida_majku.state27): each is written by an AreaTrigger watching a named
// NPC (the blacksmith in his smithy, Henik at the maypole). On the joiner that NPC is a copy moving on the host's stream, so the trigger
// fired at its own moments -- receive-only kept the change from being asked of the host, but it still landed here. The game's own quest
// files name every such trigger (QuestValueIndex.NpcTriggerPaths: 185 in the 1.5.5 files); the joiner's DLL drops their out-port fires
// like its quiet clocks, and the host's value arrives as a mirror. Sent after every load and every 5 minutes (a module woken since).
public partial class GameBridge
{
    private volatile bool _w207NpcTrigDue = true;
    private long _w207NpcTrigAtMs;
    private int _w207NpcTrigBusy;

    private void Wo207OnGameplayStarted() => _w207NpcTrigDue = true;

    private void Wo207NpcTrigTick()
    {
        if (!W137Joiner || _where != GameWhere.World) return;
        if (!_w207NpcTrigDue && Environment.TickCount64 - Interlocked.Read(ref _w207NpcTrigAtMs) < 300_000) return;
        if (Interlocked.CompareExchange(ref _w207NpcTrigBusy, 1, 0) != 0) return;
        _ = Task.Run(async () => { try { await Wo207SendNpcTriggersAsync(); } finally { Interlocked.Exchange(ref _w207NpcTrigBusy, 0); } });
    }

    private async Task Wo207SendNpcTriggersAsync()
    {
        _w207NpcTrigDue = false;
        Interlocked.Exchange(ref _w207NpcTrigAtMs, Environment.TickCount64);
        var clear = await _combat.Wo163Async(Protocol.Wo200OpQuietNodes, [0]);
        if (clear is not { Ok: true }) { Console.WriteLine("WO207-QUIET the DLL did not take the NPC triggers (an older DLL?) -- the joiner's own triggers still write its States"); return; }
        if (!W207("joiner_npc_triggers")) return;
        var idx = await Wo147QuestIndexAsync();
        if (idx is null) { Console.WriteLine("WO207-QUIET Scripts.pak not read -- the joiner's NPC triggers are not quiet"); return; }
        var paths = idx.NpcTriggerPaths().Where(p => Encoding.ASCII.GetByteCount(p) <= 400).ToList();
        int resolved = 0, missing = 0, i = 0;
        while (i < paths.Count)
        {
            var batch = new List<byte[]>();
            int size = 2;
            while (i < paths.Count && batch.Count < 255)
            {
                var pb = Encoding.ASCII.GetBytes(paths[i]);
                if (size + 2 + pb.Length > 900) break;
                batch.Add(pb); size += 2 + pb.Length; i++;
            }
            var a = new byte[size];
            a[0] = 1; a[1] = (byte)batch.Count;
            int o = 2;
            foreach (var pb in batch) { BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(o), (ushort)pb.Length); pb.CopyTo(a, o + 2); o += 2 + pb.Length; }
            var r = await _combat.Wo163Async(Protocol.Wo200OpQuietNodes, a);
            if (r is not { Ok: true } x || x.Payload.Length < 2) { Console.WriteLine($"WO207-QUIET the DLL refused NPC triggers at {i}/{paths.Count}"); return; }
            resolved += x.Payload[0]; missing += x.Payload[1];
        }
        Console.WriteLine($"WO207-QUIET joiner: {resolved} NPC trigger(s) of the game's quest files are quiet here ({missing} of {paths.Count} not loaded now) -- the host's world decides their States");
    }
}
