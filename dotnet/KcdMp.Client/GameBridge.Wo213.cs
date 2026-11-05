// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using KcdMp.Steam;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-213 Part A (docs/WO-213-findings.md): the link, measured on both machines. Every 10 s
//   MP-LINK kinds     messages and bytes per kind each way (A.1)
//   MP-LINK out       the write path: how long a frame waited for the link's lock and how long its write took (A.2; the scheduler of
//                     Part C adds its own queue numbers to the same line)
//   MP-LINK arrival   per kind, the time between arrivals (per body for state) and, for a sender-stamped kind, the age past the
//                     fastest arrival seen (A.5)
//   MP-LINK npc-bands the host's NPCs as this machine reads them, the update rate per distance band to the nearer player (A.5)
// and every 2 s on a Steam link
//   MP-LINK steam     Steam's own numbers for the connection (A.3), with its config read back once at the start.
// The 0.63.9 field logs had none of this: their "176 ms" was the frame processor's view (baseline.md).
public partial class GameBridge
{
    private readonly LinkMeter _w213Meter = new();
    private long _w213PostureAsks;
    private readonly ArrivalStats _w213Arrival = new();
    private RelayLink? _w213Link;
    private long _w213LinkSinceTs;
    private readonly ConcurrentDictionary<string, (int N, int Band)> _w213NpcWin = new(StringComparer.Ordinal);
    private long _w213NpcWinStartTs = Stopwatch.GetTimestamp();
    private readonly object _w213OutLock = new();
    private readonly List<double> _w213LockWaitMs = new(512), _w213WriteMs = new(512);
    private long _w213SlowWrites;

    /// <summary>The bands of A.5 / D.2 (metres to the nearer player): fighting or within 15 m of a player while drawn, 40, 150, beyond.</summary>
    internal static readonly string[] W213BandNames = ["fight", "40m", "150m", "far"];

    private void Wo213OnConnect(RelayLink link)
    {
        _w213Link = link;
        _w213Npc = new NpcCompact.Decoder();   // ids are per link
        _w213LinkSinceTs = Stopwatch.GetTimestamp();
        _w213NpcWin.Clear();
        if (link.Steam is { } sc)
        {
            try { Console.WriteLine($"MP-LINK steam-config at connect (Steam's own, before WO-213 B): {sc.ReadLinkConfig().Log}"); }
            catch (Exception ex) { Console.WriteLine($"MP-LINK steam-config not read: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private void Wo213OnDisconnect()
    {
        if (_w213Link is null) return;
        double s = Stopwatch.GetElapsedTime(_w213LinkSinceTs).TotalSeconds;
        Console.WriteLine(_w213Meter.TotalLine(W213Who, s));
        _w213Link = null;
    }

    private string W213Who => _isDamageAuthority ? "host" : "joiner";

    // ---------------------------------------------------------------- the switches (mp_w213 <name> on|off; the bench's twin: KCDMP_TEST_W213_OFF)

    private readonly ConcurrentDictionary<string, bool> _w213On = new(StringComparer.Ordinal);

    /// <summary>A WO-213 switch: on unless the mod said off (the link's own switches take effect at the next connection).</summary>
    private bool W213On(string name) => _w213On.TryGetValue(name, out bool v) ? v : !W213Switch.TwinOff(name);

    /// <summary>The native word (op 146): D.2's bands, D.4, E.1-E.3, E.5; and the bands themselves (the defaults, KCDMP_W213_BANDS overrides).</summary>
    private async Task Wo213PushNativeAsync()
    {
        try
        {
            uint bits = (W213On("npc_bands") ? 1u : 0) | (W213On("playout_adaptive") ? 2u : 0) | (W213On("posture_hold_late") ? 4u : 0)
                      | (W213On("posture_no_pos") ? 8u : 0) | (W213On("figure_playout") ? 16u : 0) | (W213On("figure_band") ? 32u : 0);
            var body = new byte[4 + 14];
            BitConverter.TryWriteBytes(body.AsSpan(0), bits);
            var b = W213Rules.Bands(Environment.GetEnvironmentVariable("KCDMP_W213_BANDS"));
            for (int i = 0; i < 7; i++) BitConverter.TryWriteBytes(body.AsSpan(4 + 2 * i), (ushort)b[i]);
            var r = await _combat.Wo163Async(Protocol.Wo213OpConfig, body);
            if (r is not { Ok: true } && !_w213NativeToldOld) { _w213NativeToldOld = true; Console.WriteLine("MP-W213 the DLL did not take op 146 (an older KCDMP.dll?)"); }
        }
        catch (Exception ex) { Console.WriteLine($"MP-W213 the native word was not sent: {ex.Message}"); }
    }
    private bool _w213NativeToldOld;

    private async Task Wo213NativeStatusAsync()
    {
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo213OpStatus, []);
            if (r is { Ok: true } rr) Console.WriteLine($"MP-W213-NATIVE {Encoding.ASCII.GetString(rr.Payload)}");
        }
        catch { }
    }

    private void Wo213OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w213_cfg":
            {
                int eq = (arg ?? "").IndexOf('=');
                if (eq <= 0 || arg![(eq + 1)..] is not ("on" or "off")) { Console.WriteLine($"MP-W213 cfg refused: '{arg}'"); return; }
                _w213On[arg[..eq]] = arg[(eq + 1)..] == "on";
                Console.WriteLine($"MP-W213 cfg {arg}");
                _ = Wo213PushNativeAsync();
                return;
            }
            case "w213_status":
                _ = Wo213NativeStatusAsync();
                Console.WriteLine($"MP-W213-STATS {string.Join(' ', W213Rules.SwitchNames.Select(n => $"{n}={(W213On(n) ? "on" : "off")}"))} caps=0x{_w213Caps:X4} {Wo213LineStats()} posture_asks={Interlocked.Read(ref _w213PostureAsks)}");
                return;
        }
    }

    /// <summary>A.1/A.2: one frame written to the relay, with how long it waited for the lock and how long the write itself took.</summary>
    private void Wo213NoteOut(byte[] packet, double lockWaitMs, double writeMs)
    {
        if (packet.Length >= 3) _w213Meter.CountFrame(true, packet[0], packet.AsSpan(3));
        lock (_w213OutLock)
        {
            if (_w213LockWaitMs.Count < 20_000) { _w213LockWaitMs.Add(lockWaitMs); _w213WriteMs.Add(writeMs); }
            if (writeMs > 100) _w213SlowWrites++;
        }
    }

    /// <summary>A.1/A.5: one frame read from the relay, at the reader (before any queue of this agent).</summary>
    private void Wo213NoteIn(int type, byte[] payload, long arrival, int wireBytes)
    {
        string kind = LinkKinds.Of(type, payload);
        _w213Meter.Count(false, kind, wireBytes);
        if (type == Protocol.NpcStateQDown) return;   // one the decoder could not complete yet: counted, nothing to time
        if (type == Protocol.Ghost && PositionCodec.TryDecodeGhost(payload, out var gs))
        {
            string body = "kcd2mp_" + gs.GhostId.ToString(CultureInfo.InvariantCulture);
            _w213Arrival.Arrive(kind, body, arrival, gs.SenderMs, gs.GhostId, Wo213Moved(body, gs.X, gs.Y, arrival));
        }
        else if (type == Protocol.NpcStateDown && payload.Length >= 2 + Protocol.NpcStateFixedTail && payload.Length == 2 + payload[1] + Protocol.NpcStateFixedTail)
        {
            int nl = payload[1], o = 2 + nl;
            string name = Encoding.UTF8.GetString(payload, 2, nl);
            uint senderMs = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(o + Protocol.NpcStateSenderMsOffset));
            float x = BitConverter.ToSingle(payload, o), y = BitConverter.ToSingle(payload, o + 4);
            _w213Arrival.Arrive(kind, name, arrival, senderMs, payload[0], Wo213Moved(name, x, y, arrival));
            byte flags = payload[o + Protocol.NpcStateFlagsOffset];
            byte oppKind = 0; long oppAgeMs = long.MaxValue;
            if (_w209OppLast.TryGetValue(name, out var opp)) { oppKind = opp.Kind; oppAgeMs = Environment.TickCount64 - opp.At; }
            int band = Wo213BandOf(x, y, W213Rules.Fighting((flags & (Protocol.NpcStateFlagEngaged | 0x04)) != 0, oppKind, oppAgeMs));
            _w213NpcWin.AddOrUpdate(name, (1, band), (_, v) => (v.N + 1, band));
            Wo214NoteBandSample(name, x, y);   // WO-214 A.0/G: the walking bodies' own rates
        }
        else _w213Arrival.Arrive(kind, null, arrival);
    }

    private readonly Dictionary<string, (float X, float Y, long Ts)> _w213LastXY = new(StringComparer.Ordinal);

    /// <summary>
    /// A.5: did this body keep moving through the time since its last sample -- 0.3 to 15 m/s over it, and that time under 10 s
    /// (longer: a body that left the stream, the sender's cull or a load; faster: a warp)? Only a gap over a second asks: a body that
    /// stopped and then walked on, or stood with its 2 s heartbeat, is no silence of its stream. Reader thread only.
    /// </summary>
    private bool Wo213Moved(string body, float x, float y, long ts)
    {
        bool moved = true;
        if (_w213LastXY.TryGetValue(body, out var p))
        {
            double dtS = (ts - p.Ts) / (double)Stopwatch.Frequency;
            double d = Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
            moved = W213Rules.MovedThrough(d, dtS);
        }
        _w213LastXY[body] = (x, y, ts);
        return moved;
    }

    /// <summary>D.2's band of a body at (x, y): 0 fight (drawn within 15 m of a player), 1 within 40 m, 2 within 150 m, 3 beyond.</summary>
    private int Wo213BandOf(float x, float y, bool drawn)
    {
        double d = double.MaxValue;
        var own = _w211LastOwnPos;
        if (own.X != 0 || own.Y != 0) d = Math.Sqrt((x - own.X) * (x - own.X) + (y - own.Y) * (y - own.Y));
        foreach (var g in _ghostLastPos.Values)
            d = Math.Min(d, Math.Sqrt((x - g.X) * (x - g.X) + (y - g.Y) * (y - g.Y)));
        return W213Rules.Band(d, drawn);
    }

    /// <summary>The 4 Hz session tick: the 10 s lines, and Steam's numbers every 2 s.</summary>
    private void Wo213Tick(long n)
    {
        if (n % 8 == 5) _ = Wo213PushNativeAsync();   // E/D.2's native word every 2 s (a reloaded DLL takes it again)
        if (n % 120 == 13 && (W137Host || W137Joiner)) _ = Wo213NativeStatusAsync();
        if (_w213Link is null) return;
        if (n % 8 == 3 && _w213Link.Steam is { } sc) Wo213SteamLine(sc);
        if (n % 8 == 1) Wo213ToastTick();
        if (n % 40 != 7) return;
        string who = W213Who;
        Console.WriteLine(_w213Meter.TakeLine(who));
        foreach (var l in _w213Arrival.TakeLines(who)) Console.WriteLine(l);
        Console.WriteLine(Wo213OutLine(who));
        if (_w213Npc.Decoded + _w213Npc.UnknownId + _w213Npc.Malformed > 0)
            Console.WriteLine($"MP-LINK compact who={who} decoded={_w213Npc.Decoded} unknown_id={_w213Npc.UnknownId} malformed={_w213Npc.Malformed} repeats_left_out={_w213Repeats?.Dropped ?? 0} figure_lua_skipped={_w213GhostLuaSkipped}");
        if (!_isDamageAuthority) Console.WriteLine(Wo213BandsLine(who));
    }

    private string Wo213OutLine(string who)
    {
        List<double> w, d; long slow;
        lock (_w213OutLock)
        {
            w = new List<double>(_w213LockWaitMs); d = new List<double>(_w213WriteMs); slow = _w213SlowWrites;
            _w213LockWaitMs.Clear(); _w213WriteMs.Clear(); _w213SlowWrites = 0;
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"MP-LINK out who={who} via={_w213Link?.Via ?? "-"} frames={w.Count} lock_wait_p50={Q(w, 0.5):F1} lock_wait_p99={Q(w, 0.99):F1} lock_wait_max={(w.Count > 0 ? w.Max() : 0):F1} write_p99={Q(d, 0.99):F1} write_max={(d.Count > 0 ? d.Max() : 0):F1} writes_over_100ms={slow}{Wo213SchedOutText()}");
        static double Q(List<double> v, double q) => v.Count == 0 ? 0 : ArrivalStats.Quantile(v, q);
    }

    private string Wo213BandsLine(string who)
    {
        double s = Math.Max(0.001, Stopwatch.GetElapsedTime(_w213NpcWinStartTs).TotalSeconds);
        var snap = _w213NpcWin.ToArray();
        _w213NpcWin.Clear();
        _w213NpcWinStartTs = Stopwatch.GetTimestamp();
        Console.WriteLine(Wo214MovingLine(who, s, snap));   // WO-214 A.0/G: the same window, the bodies that walked
        var sb = new StringBuilder($"MP-LINK npc-bands who={who} window_s={s.ToString("F1", CultureInfo.InvariantCulture)}");
        for (int b = 0; b < W213BandNames.Length; b++)
        {
            var hz = snap.Where(kv => kv.Value.Band == b).Select(kv => kv.Value.N / s).ToList();
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $" {W213BandNames[b]}={hz.Count}:p50={(hz.Count > 0 ? ArrivalStats.Quantile(hz, 0.5) : 0):F1}Hz:p10={(hz.Count > 0 ? ArrivalStats.Quantile(hz, 0.1) : 0):F1}Hz"));
        }
        return sb.ToString();
    }

    /// <summary>Part C's scheduler adds its queue numbers to the out line (" sched=off" while its twin turns it off).</summary>
    private string Wo213SchedOutText() => _w213Writer is { } w ? w.Scheduler.TakeText() + $" rate={w.LastRate}" : " sched=off";

    // ---------------------------------------------------------------- Part B/C: this agent's way out

    private LinkWriter? _w213Writer;
    private Stream? _w213Stream;
    private CancellationTokenSource? _w213WriterCts;
    private ushort _w213Caps;

    /// <summary>
    /// After the Ack: B's settings on a Steam link (the send rate, no Nagle, two lanes: events and state), the scheduler (C) as this
    /// agent's one writer, and the hello -- the features this agent speaks; the relay answers with the ones both use.
    /// </summary>
    private void Wo213StartWriter(RelayLink link, Stream stream)
    {
        _w213Caps = 0;
        if (link.Steam is { } sc && !W213Switch.TwinOff("steam_cfg"))
        {
            try
            {
                var cfg = sc.ApplyLinkConfig(W213Switch.SteamSendRateMin, W213Switch.SteamSendRateMax, W213Switch.SteamNagleUs, null, lanes: true,
                                             W213Switch.SteamStatePriority, W213Switch.SteamEventWeight, W213Switch.SteamStateWeight);
                Console.WriteLine($"MP-LINK steam-config applied (WO-213 B), read back: {cfg.Log}");
            }
            catch (Exception ex) { Console.WriteLine($"MP-LINK steam-config not applied: {ex.GetType().Name}: {ex.Message}"); }
        }
        else if (link.Steam is not null) Console.WriteLine("MP-LINK steam-config left at Steam's own (the twin: steam_cfg off)");
        if (W213Switch.TwinOff("link_sched")) { Console.WriteLine("MP-LINK scheduler OFF (the twin: link_sched) -- every frame written in order as before"); return; }
        ILinkPath path = link.Steam is { } s2 ? new SteamLinkPath(s2) : new StreamLinkPath(stream);
        var sched = new LinkScheduler(new LinkSchedOptions { Budget = !W213Switch.TwinOff("link_budget") });
        // D.4: this player's own position is the top band; everything else this agent sends is its own few bodies
        if (!W213Switch.TwinOff("figure_band")) sched.BandOf = p => p.Kind == "pos" ? 0 : null;
        var w = new LinkWriter(sched, path);
        var cts = new CancellationTokenSource();
        _w213WriterCts = cts; _w213Stream = stream; _w213Writer = w;
        _ = Task.Run(async () =>
        {
            try { await w.RunAsync(cts.Token, (f, _) => { if (f.Length >= 3) _w213Meter.CountFrame(true, f[0], f.AsSpan(3)); }); }
            catch (Exception ex) { Console.WriteLine($"MP-LINK the writer stopped: {ex.GetType().Name}: {ex.Message}"); try { link.Dispose(); } catch { } }
        });
        ushort caps = (ushort)(Protocol.LinkCapLatestWins
                     | (W213Switch.TwinOff("state_unreliable") ? 0 : Protocol.LinkCapUnreliableState)
                     | (W213Switch.TwinOff("npc_compact") ? 0 : Protocol.LinkCapCompactNpc));
        w.Enqueue(Protocol.BuildLinkHelloUp(caps));
        Console.WriteLine($"MP-LINK scheduler on (via={path.Transport}, unreliable possible={(path.CanSendUnreliable ? 1 : 0)}); hello sent caps=0x{caps:X4}");
    }

    private void Wo213StopWriter()
    {
        try { _w213WriterCts?.Cancel(); } catch { }
        _w213Writer = null; _w213Stream = null; _w213WriterCts = null;
    }

    private NpcCompact.Decoder _w213Npc = new();

    // D.4: the figure's Lua update at most every FigureLuaEveryMs (a state block or a riding change at once); frame processor only
    private readonly Dictionary<byte, (long AtMs, bool Riding)> _w213GhostLua = new();
    private long _w213GhostLuaSkipped;
    private bool Wo213GhostLuaDue(byte ghost, bool riding, bool hasState)
    {
        if (!W213On("figure_band")) return true;
        long now = Environment.TickCount64;
        bool due = !_w213GhostLua.TryGetValue(ghost, out var l) || W213Rules.FigureLuaDue(now - l.AtMs, l.Riding != riding, hasState);
        if (due) _w213GhostLua[ghost] = (now, riding);
        else _w213GhostLuaSkipped++;
        return due;
    }
    // C.2's receiving side (null: its twin, link_sched off -- no repeats come then either)
    private readonly LinkRepeatFilter? _w213Repeats = W213Switch.TwinOff("link_sched") ? null : new LinkRepeatFilter();

    /// <summary>D.3: the relay's compact NPC state as the 0x27 payload (null: its id's name has not come yet, or malformed).</summary>
    private byte[]? Wo213Expand(byte[] payload) => _w213Npc.Decode(payload);

    /// <summary>The relay's answer: the features both use (unreliable state only on a path that has it).</summary>
    private bool Wo213TakeAtRead(int type, byte[] payload)
    {
        if (type != Protocol.LinkHelloDown) return false;
        if (!Protocol.TryParseLinkHello(payload, out byte v, out ushort caps, out byte transport, out int rateKBps)) return true;
        _w213Caps = caps;
        if (_w213Writer is { } w) w.Scheduler.Options.Unreliable = (caps & Protocol.LinkCapUnreliableState) != 0 && w.Path.CanSendUnreliable;
        Console.WriteLine($"MP-LINK hello answered: version={v} caps=0x{caps:X4} transport={Protocol.LinkTransportName(transport)} relay_rate={rateKBps} KB/s -- latest wins={((caps & Protocol.LinkCapLatestWins) != 0 ? 1 : 0)} unreliable state={(_w213Writer?.Scheduler.Options.Unreliable == true ? 1 : 0)} compact npc={((caps & Protocol.LinkCapCompactNpc) != 0 ? 1 : 0)}");
        return true;
    }

    /// <summary>A frame through the scheduler when it runs on this stream; false = write it the old way.</summary>
    private bool Wo213TryQueue(Stream stream, byte[] packet)
    {
        if (_w213Writer is not { } w || !ReferenceEquals(stream, _w213Stream)) return false;
        w.Enqueue(packet);   // throws when the writer died: the caller's write failed, as before
        return true;
    }

    private bool _w213SteamConfigLogged;

    private void Wo213SteamLine(SteamP2PConnection sc)
    {
        try
        {
            if (sc.RealTimeStatus() is not { } s) return;
            string lanes = s.Lanes.Length == 0 ? "-" : string.Join('/', s.Lanes.Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.PendingReliable}r{l.PendingUnreliable}u{l.QueueTimeUsec / 1000}ms")));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"MP-LINK steam who={W213Who} ping={s.PingMs} q_local={s.QualityLocal:F2} q_remote={s.QualityRemote:F2} out_pps={s.OutPacketsPerSec:F0} out_Bps={s.OutBytesPerSec:F0} in_pps={s.InPacketsPerSec:F0} in_Bps={s.InBytesPerSec:F0} send_rate={s.SendRateBytesPerSec} pend_unrel={s.PendingUnreliable} pend_rel={s.PendingReliable} unacked={s.SentUnackedReliable} queue_ms={SteamLinkStatus.QueueMsText(s.QueueTimeUsec)} lanes={lanes} unrel_sent={Interlocked.Read(ref sc.UnreliableSent)} unrel_refused={Interlocked.Read(ref sc.UnreliableRefused)}"));
            if (!_w213SteamConfigLogged) { _w213SteamConfigLogged = true; Console.WriteLine($"MP-LINK steam-config now: {sc.ReadLinkConfig().Log}"); }
        }
        catch (Exception ex) { Console.WriteLine($"MP-LINK steam status failed: {ex.GetType().Name}: {ex.Message}"); }
    }
}

// WO-213 E.4: the joined conversation's lines on the listener. The field's "first line skips after a pick": WO-210's pick hold showed the
// chosen row for its second and held the line behind it (600-770 ms), the starter's next line arrived on time and cut it -- subtitle and
// voice. Now each line's age is measured where it arrives (the relay's clock, the hold excluded); a line inside its window (the measured
// line latency plus its own length) is shown for what is left of it with its voice, and waits for the line before it to have had its
// time; only a line past its own end is a subtitle alone. The twin (line_latency off): every line at once, as before.
public partial class GameBridge
{
    private readonly object _w213LineLock = new();
    private readonly List<double> _w213LineAges = new(64);
    private long _w213PrevLineEndMs;
    private long _w213LinesOnTime, _w213LinesWaited, _w213LinesOver, _w213LinesUnstamped;

    /// <summary>The line's age where it arrived (ms on the relay's clock), or -1 when the starter's stamp is unknown.</summary>
    private long Wo213LineAge(Wo204Rules.ConvLineMsg line)
    {
        if (line.StartHostMs <= 0) return -1;
        long nowHost = UnixMs() + (long)Math.Round(ClockOffsetMs is double o && double.IsFinite(o) ? o : 0.0);
        return nowHost - line.StartHostMs;
    }

    /// <summary>How long to show the line (ms) and whether to voice it; waits for the line before it first.</summary>
    private async Task<(int ShowMs, bool Voice)> Wo213LinePlanAsync(Wo204Rules.ConvLineMsg line, long ageAtArrival, bool firstAfterPick = false)
    {
        if (line.Earlier || !W213On("line_latency")) return (line.DurMs, true);
        if (ageAtArrival < 0) { Interlocked.Increment(ref _w213LinesUnstamped); return (line.DurMs, true); }
        double latency;
        lock (_w213LineLock)
        {
            latency = W213Rules.LineLatencyMs(_w213LineAges);
            if (ageAtArrival <= line.DurMs) { _w213LineAges.Add(ageAtArrival); if (_w213LineAges.Count > 40) _w213LineAges.RemoveAt(0); }
        }
        long now = Environment.TickCount64;
        long prevEnd;
        lock (_w213LineLock) prevEnd = _w213PrevLineEndMs;
        var plan = W213Rules.LinePlan(ageAtArrival, line.DurMs, latency, Math.Max(0, prevEnd - now), firstAfterPick);
        if (plan.WaitMs > 0) { Interlocked.Increment(ref _w213LinesWaited); await Task.Delay((int)plan.WaitMs); }
        if (plan.Over) Interlocked.Increment(ref _w213LinesOver); else Interlocked.Increment(ref _w213LinesOnTime);
        lock (_w213LineLock) _w213PrevLineEndMs = Environment.TickCount64 + plan.ShowMs;
        long k = _w213LinesOver + _w213LinesOnTime;
        if (k <= 30 || plan.Over)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"WO213-LINE cid={line.Cid} line={line.LineId} age_ms={ageAtArrival} dur_ms={line.DurMs} latency_ms={latency:F0} waited_ms={plan.WaitMs} show_ms={plan.ShowMs}{(firstAfterPick ? " first-after-pick" : "")} -> {(plan.Over ? "past its end: subtitle only" : "voiced")}"));
        return (plan.ShowMs, !plan.Over);
    }

    // ---------------------------------------------------------------- G: the joiner's plain word when the link is genuinely poor

    private int _w213PoorRun;
    private long _w213ToastAtMs = -1_000_000, _w213Toasts;

    /// <summary>
    /// Every 2 s (the joiner): the link is poor when Steam's own numbers say so (quality under 0.75 either way, or 64 KB waiting, or a
    /// 300 ms ping), or -- on a link without them -- the ping over 400 ms. Five checks in a row (10 s) say it once, never more often than
    /// every two minutes; one spike never does.
    /// </summary>
    private void Wo213ToastTick()
    {
        if (_isDamageAuthority || !W213On("link_toast")) { _w213PoorRun = 0; return; }
        bool poor;
        if (_w213Link?.Steam is { } sc && sc.RealTimeStatus() is { } st)
            poor = W213Rules.SteamPoor(st.QualityLocal, st.QualityRemote, st.PendingReliable + st.PendingUnreliable, st.PingMs);
        else poor = _w202LastPingMs > 400;
        _w213PoorRun = poor ? _w213PoorRun + 1 : 0;
        long now = Environment.TickCount64;
        if (_w213PoorRun < 5 || now - _w213ToastAtMs < 120_000) return;
        _w213ToastAtMs = now;
        long k = Interlocked.Increment(ref _w213Toasts);
        Console.WriteLine($"MP-LINK toast: the link to the host has been poor for 10 s -- the player is told [n={k}]");
        _ = ExecLuaAsync("if KCD2MP_ShowNativeToast then KCD2MP_ShowNativeToast(\"Your connection to the host is struggling: NPCs may look jumpy\") end");
    }

    private string Wo213LineStats() =>
        $"lines_on_time={Interlocked.Read(ref _w213LinesOnTime)} lines_waited={Interlocked.Read(ref _w213LinesWaited)} lines_over={Interlocked.Read(ref _w213LinesOver)} lines_unstamped={Interlocked.Read(ref _w213LinesUnstamped)}";
}

/// <summary>WO-213: pure rules (tested in KcdMp.Client.Tests).</summary>
public static class W213Rules
{
    /// <summary>D.2 / A.5: an NPC fights when its state says drawn or engaged, or the host's opponent stream named an opponent for it in
    /// the last 5 s (a bandit fighting the partner's figure holds no weapon the game reports).</summary>
    public static bool Fighting(bool drawnOrEngaged, byte oppKind, long oppAgeMs) => drawnOrEngaged || (oppKind != 0 && oppAgeMs <= 5000);

    /// <summary>A.5: a body kept moving through a silence of <paramref name="dtS"/> s when it covered 0.3 m/s of it -- and no more than 15 m/s
    /// (faster is a warp: the game placing a body, a clock change's schedules) -- over under 10 s (longer: it left the stream).</summary>
    public static bool MovedThrough(double distM, double dtS) => dtS <= 10.0 && distM >= Math.Max(0.01, 0.3 * dtS) && distM <= 15.0 * dtS;

    /// <summary>D.4: the figure's change gate in the top band (the NPC sender's 1.5 cm, 0.01 rad); <paramref name="fine"/> off: the old one.</summary>
    public const float FigurePosEps = 0.015f, FigureRotEps = 0.01f;
    /// <summary>D.4: the position loop reads the DLL at most this often (the top band is 33 ms; unbounded it read ~64 times a second and
    /// with the read first at the pipe's gate the joiner's other pipe calls waited seconds).</summary>
    public const int FigureReadEveryMs = 25;
    /// <summary>D.4: the joiner's Lua figure update at most this often (the old stream's pace); the DLL takes every sample.</summary>
    public const int FigureLuaEveryMs = 50;
    public static bool FigureLuaDue(long sinceMs, bool ridingChanged, bool hasState) => hasState || ridingChanged || sinceMs >= FigureLuaEveryMs;
    public static bool FigureChanged(float dx, float dy, float dz, float drot, bool fine, float posEps, float rotEps)
    {
        float p = fine ? FigurePosEps : posEps, r = fine ? FigureRotEps : rotEps;
        return Math.Abs(dx) > p || Math.Abs(dy) > p || Math.Abs(dz) > p || Math.Abs(drot) > r;
    }

    /// <summary>G: Steam's own numbers say the link is poor right now (one check; the toast needs five in a row).</summary>
    public static bool SteamPoor(float qLocal, float qRemote, int pendingBytes, int pingMs) =>
        (qLocal >= 0 && qLocal < 0.75f) || (qRemote >= 0 && qRemote < 0.75f) || pendingBytes > 64 * 1024 || pingMs > 300;

    /// <summary>E.4: the line latency the window allows for -- the p90 of the recent lines' ages at arrival (300 ms until 5 are known).</summary>
    public static double LineLatencyMs(List<double> ages)
    {
        if (ages.Count < 5) return 300;
        return Math.Clamp(ArrivalStats.Quantile([.. ages], 0.9), 50, 2000);
    }

    public readonly record struct LinePlanResult(bool Over, long WaitMs, int ShowMs);

    /// <summary>
    /// E.4: a line <paramref name="ageMs"/> old where it arrived, <paramref name="durMs"/> long, with the measured latency; the line before
    /// it shown for <paramref name="prevLeftMs"/> more. Over = past its window (latency + its own length): a subtitle only, at once. Else it
    /// waits for the previous line while that still leaves it a second of its own, and is shown for what is left of it (1 s at least).
    /// </summary>
    public static LinePlanResult LinePlan(long ageMs, int durMs, double latencyMs, long prevLeftMs, bool firstAfterPick = false)
    {
        if (firstAfterPick) ageMs = Math.Min(ageMs, (long)latencyMs);   // the answer to a pick is never a subtitle alone: voiced in full
        double window = latencyMs + durMs;
        if (ageMs > window) return new LinePlanResult(true, 0, 1000);
        long wait = 0;
        if (prevLeftMs > 0)
        {
            double room = durMs - ageMs - 1000;   // what may be spent waiting and still leave a second of the line
            wait = (long)Math.Max(0, Math.Min(prevLeftMs, room));
        }
        int show = (int)Math.Max(1000, durMs - ageMs - wait + latencyMs);
        return new LinePlanResult(false, wait, show);
    }

    /// <summary>Every WO-213 switch the agent knows (the native ones go to the DLL in its word).</summary>
    public static readonly string[] SwitchNames = ["link_sched", "state_unreliable", "steam_cfg", "link_budget", "npc_bands", "npc_compact", "figure_band",
        "playout_adaptive", "posture_hold_late", "posture_no_pos", "figure_playout", "line_latency", "link_toast"];

    /// <summary>D.2's bands: fight / near / mid radii (m), then the four periods (ms); "15:33,40:50,150:100,0:500" overrides (the bench).</summary>
    public static int[] Bands(string? env)
    {
        int[] d = [15, 40, 150, 33, 50, 100, 500];
        if (string.IsNullOrWhiteSpace(env)) return d;
        var parts = env.Split(',');
        if (parts.Length != 4) return d;
        try
        {
            var r = new int[7];
            for (int i = 0; i < 4; i++)
            {
                var kv = parts[i].Split(':');
                if (i < 3) r[i] = int.Parse(kv[0], CultureInfo.InvariantCulture);
                r[3 + i] = int.Parse(kv[1], CultureInfo.InvariantCulture);
            }
            return r[3..].All(x => x >= 10) ? r : d;
        }
        catch (FormatException) { return d; }
    }

    /// <summary>D.2: the band of a body <paramref name="distM"/> from the nearer player (fight = drawn within 15 m).</summary>
    public static int Band(double distM, bool drawn, double fightM = 15, double nearM = 40, double midM = 150) =>
        drawn && distM <= fightM ? 0 : distM <= nearM ? 1 : distM <= midM ? 2 : 3;
}
