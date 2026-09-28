using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-138: no pausing, and the host's NPC stream in the DLL -- the agent's half
// (docs/WO-138-findings.md).
//
// HOST
//   * the mod's rescan set (npc_track) and the sender's settings (w138_cfg) go to
//     the DLL; the DLL samples and sends at its frame hook, whatever the Lua
//     timers do (a menu, the inventory, a cutscene stop them); each 0xA0 row goes
//     out as the same NpcState (0x26) the Lua line made. While the DLL's status
//     says it sends, the mod is told (KCD2MP_W138NativeSend) and keeps its own
//     line in; its lines that still arrive are dropped (a resync passes).
//     No DLL, or a stopped one: the Lua sender, exactly as before.
//   * the pause announcement: PauseUp's state byte carries the reasons (menu,
//     inventory, dialogue, cutscene, load, skip-time, frozen); 0 = running.
// JOINER
//   * while the host announced a pause and its link is alive, the host's copies
//     are held visible where the stream left them (the mod's silence rule and the
//     DLL's are suspended); the host resuming restarts their silence clocks; the
//     host's link lost (no packet for 6 s) ends the hold at once: hidden, as before.
// BOTH (a session with a partner here -- the levers)
//   * the ESC menu's and a rendered video's PauseGame are declined by the DLL's gate;
//   * the inventory's time-scale divide (wh_ui_ApsePauseRatio, 1000) is set to 1;
//   * outside a session (no relay, or no partner for 10 s) all of it goes back.
public partial class GameBridge
{
    private bool _w138Connected;
    private volatile bool _w138NativeSwitch = true;   // mp_w138_native (the mod's command)
    private volatile bool _w138LeversSwitch = true;   // mp_w138_levers
    private volatile bool _w138NativeActive;          // the DLL's status says it sends
    private Wo138Cfg _w138Cfg = Wo138Cfg.Default;
    private bool _w138HaveTrack;
    private ushort _w138TrackGen;
    private readonly Dictionary<int, List<(string Name, byte Flags)>> _w138TrackParts = new();
    private int _w138TrackPartsWanted;
    private List<(string Name, byte Flags)> _w138Track = [];
    private int _w138TrackDirty;                      // 1 = send the set again
    private volatile bool _w138Dialog;
    private Wo138WorldState _w138World;
    private byte _w138LastReasons;
    private Channel<Wo138Row>? _w138Rows;
    private long _w138RowsIn, _w138RowsSent, _w138LuaDropped, _w138HoldOns, _w138PauseEdges;
    private string _w138LastText = "";
    // joiner
    private int _w138HostId = -1;                     // the authority's ghost id (the NpcStateDown source)
    private readonly ConcurrentDictionary<byte, long> _w138LastFromMs = new();
    private volatile byte _w138HostReasons;
    private long _w138HostPausedSinceMs;
    private bool _w138HoldOn;
    // levers
    private bool _w138LeversApplied;

    private static long W138NowMs() => Environment.TickCount64;

    private bool W138Joiner => _combatRoleApplied && !_isDamageAuthority && _hostAuthority;
    private bool W138Host => _combatRoleApplied && _isDamageAuthority;

    private void Wo138OnConnect(CancellationToken ct)
    {
        _w138Connected = true;
        _w138NativeActive = false;
        _w138HostId = -1;
        _w138LastFromMs.Clear();
        _w138HostReasons = 0;
        _w138HoldOn = false;
        _w138LeversApplied = false;
        _w138TrackDirty = 1;
        _w138Rows = Channel.CreateBounded<Wo138Row>(new BoundedChannelOptions(4096) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        _combat.OnNpcStream = rows => { foreach (var r in rows) { Interlocked.Increment(ref _w138RowsIn); _w138Rows?.Writer.TryWrite(r); } };
        _combat.OnWorld = w =>
        {
            bool edge = w.Frozen != _w138World.Frozen;
            _w138World = w;
            if (edge) { Console.WriteLine($"MP-WO138 world {(w.Frozen ? "FROZEN" : "runs")} (scale {w.ScalePermille / 1000.0:F3}, held 0x{w.HeldMask:X})"); _ = _sendPauseIfChanged?.Invoke(); }
        };
        _ = Wo138SendLoopAsync(_w138Rows.Reader, ct);
        _ = Wo138LoopAsync(ct);
        // A lever an earlier agent left on (it crashed, or a pipe drop) is put back first.
        _ = ExecLuaAsync("if KCD2MP_W138ApseRatio then KCD2MP_W138ApseRatio(false) end");
    }

    private async Task Wo138OnDisconnectAsync()
    {
        _w138Connected = false;
        _combat.OnNpcStream = null;
        _combat.OnWorld = null;
        _w138Rows?.Writer.TryComplete();
        _w138NativeActive = false;
        try
        {
            await _combat.Wo138Async(Wo138Codec.OpConfig, Wo138Codec.ConfigBody(false, _w138Cfg));
            await _combat.Wo138Async(Wo138Codec.OpLevers, Wo138Codec.LeversBody(false, Wo138Codec.DefaultMask));
            await _combat.Wo138Async(Wo138Codec.OpHold, [0]);
        }
        catch { }
        try
        {
            await _transport.ExecuteNowAsync("if KCD2MP_W138NativeSend then KCD2MP_W138NativeSend(0) end; " +
                                             "if KCD2MP_W138Hold then KCD2MP_W138Hold(false, 0, true) end; " +
                                             "if KCD2MP_W138ApseRatio then KCD2MP_W138ApseRatio(false) end");
        }
        catch { }
        _w138HoldOn = false;
        _w138LeversApplied = false;
    }

    // ------------------------------------------------------------------ host: the stream

    /// <summary>Rows from the DLL, in its order, to the relay as NpcState (0x26).</summary>
    private async Task Wo138SendLoopAsync(ChannelReader<Wo138Row> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var r in reader.ReadAllAsync(ct))
            {
                var send = _sendNpcState;
                if (send is null || !W138Host) continue;
                lock (_requestsIn) _ownedNpcSeenUtc[r.Name] = DateTime.UtcNow;   // WO-102 Phase 5, as the Lua line did
                await send(r.Name, r.X, r.Y, r.Z, r.Rot, r.Hp, r.Flags);
                _w138RowsSent++;
            }
        }
        catch (OperationCanceledException) { }
        catch (ChannelClosedException) { }
        catch (Exception ex) { Console.WriteLine($"MP-WO138 send loop stopped: {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>The mod's npc_state line, while the DLL streams: dropped (a resync passes).</summary>
    private bool Wo138DropLuaLine(byte flags)
    {
        if (!Wo138Codec.DropLuaLine(_w138NativeActive, flags)) return false;
        _w138LuaDropped++;
        return true;
    }

    private void Wo138OnTrackLine(string arg)
    {
        if (!Wo138Codec.TryParseTrack(arg, out ushort gen, out int part, out int parts, out var names))
        {
            Console.WriteLine($"MP-WO138 malformed npc_track '{(arg.Length > 80 ? arg[..80] : arg)}'");
            return;
        }
        lock (_w138TrackParts)
        {
            if (gen != _w138TrackGen || parts != _w138TrackPartsWanted) { _w138TrackParts.Clear(); _w138TrackGen = gen; _w138TrackPartsWanted = parts; }
            _w138TrackParts[part] = names;
            if (_w138TrackParts.Count < parts) return;
            var all = new List<(string, byte)>();
            for (int i = 0; i < parts; i++) if (_w138TrackParts.TryGetValue(i, out var p)) all.AddRange(p);
            _w138Track = all;
            _w138HaveTrack = true;
            _w138TrackParts.Clear();
        }
        Interlocked.Exchange(ref _w138TrackDirty, 1);
    }

    private void Wo138OnCfgLine(string arg)
    {
        if (Wo138Codec.TryParseCfg(arg, out var c)) { if (c != _w138Cfg) { _w138Cfg = c; Interlocked.Exchange(ref _w138TrackDirty, 1); } }
        else Console.WriteLine($"MP-WO138 malformed w138_cfg '{arg}'");
    }

    private void Wo138OnModLine(string arg)
    {
        switch (arg.Trim())
        {
            case "native on": _w138NativeSwitch = true; Interlocked.Exchange(ref _w138TrackDirty, 1); Console.WriteLine("MP-WO138 native sender switched ON (mp_w138_native)"); break;
            case "native off": _w138NativeSwitch = false; Interlocked.Exchange(ref _w138TrackDirty, 1); Console.WriteLine("MP-WO138 native sender switched off: the Lua sender streams (mp_w138_native)"); break;
            case "levers on": _w138LeversSwitch = true; Console.WriteLine("MP-WO138 levers switched ON (mp_w138_levers)"); break;
            case "levers off": _w138LeversSwitch = false; Console.WriteLine("MP-WO138 levers switched off: menus pause as usual (mp_w138_levers)"); break;
            case "status": Console.WriteLine(Wo138StatsLine()); break;
            default:
                // Live checks: "pausetest <source> <1|0>" -- PauseGame through the DLL's gate (op 9).
                var f = arg.Trim().Split(' ');
                if (f.Length == 3 && f[0] == "pausetest" && ushort.TryParse(f[1], out ushort src))
                {
                    var body = new byte[4];
                    body[0] = (byte)(f[2] == "1" ? 1 : 0);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1), src);
                    _ = Task.Run(async () =>
                    {
                        var r = await _combat.Wo138Async(Wo138Codec.OpPause, body);
                        Console.WriteLine($"MP-WO138 pausetest source {src} pause={f[2]}: {(r is null ? "no answer" : r.Value.Ok ? $"ok, held 0x{(r.Value.Payload.Length >= 2 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(r.Value.Payload) : 0):X}" : $"refused (reason {r.Value.Reason})")}");
                    });
                }
                break;
        }
    }

    private void Wo138OnDialogLine(string arg)
    {
        bool on = arg.Trim() == "1";
        if (on == _w138Dialog) return;
        _w138Dialog = on;
        _ = _sendPauseIfChanged?.Invoke();
    }

    // ------------------------------------------------------------------ the pause announcement

    /// <summary>The PauseUp state byte: 0 = running, else this machine's reasons.</summary>
    private byte Wo138PauseState()
    {
        var tail = _transport as LogTailGameTransport;
        byte r = Wo138Codec.Reasons(
            menu: (tail?.MenuOpen ?? false) || _localManualPaused,
            inventory: tail?.InventoryOpen ?? false,
            dialogue: _w138Dialog,
            cutscene: tail?.CutsceneActive ?? false,
            loading: _where == GameWhere.Loading,
            skipTime: tail?.SkipTimeActive ?? false,
            frozen: _w138World.Frozen);
        // Without the log tail (the HTTP transport) the old aggregate is the menu bit.
        if (tail is null && _localAutoPaused) r |= Wo138Codec.ReasonMenu;
        if (r != _w138LastReasons)
        {
            _w138LastReasons = r;
            _w138PauseEdges++;
        }
        return r;
    }

    // ------------------------------------------------------------------ joiner: hold, don't hide

    /// <summary>Every relay packet, at the socket read: the link of its source is alive.</summary>
    private void Wo138NoteArrival(int type, byte[] payload)
    {
        if (payload.Length < 1) return;
        if (type != Protocol.Ghost && type != Protocol.NpcStateDown && type != Protocol.PauseDown && type != Protocol.PlayerStateDown
            && type != Protocol.ActionDown) return;
        byte src = payload[0];
        _w138LastFromMs[src] = W138NowMs();
        if (type == Protocol.NpcStateDown && _w138HostId != src && W138Joiner)
        {
            _w138HostId = src;
            Console.WriteLine($"MP-WO138 the host is ghost {src} (its NPC stream)");
        }
    }

    private void Wo138OnPeerPause(byte sourceId, byte state)
    {
        if (!W138Joiner) return;
        if (_w138HostId < 0) { _w138HostId = sourceId; Console.WriteLine($"MP-WO138 the host is ghost {sourceId} (its pause)"); }
        if (sourceId != _w138HostId) return;
        byte was = _w138HostReasons;
        _w138HostReasons = state;
        if (was == 0 && state != 0) _w138HostPausedSinceMs = W138NowMs();
        Console.WriteLine($"MP-WO138 the host {(state == 0 ? "resumed" : "is paused: " + Wo138Codec.Describe(state))}");
        _ = Wo138ApplyHoldAsync(CancellationToken.None, force: true);
    }

    private async Task Wo138ApplyHoldAsync(CancellationToken ct, bool force = false)
    {
        long now = W138NowMs();
        long last = _w138HostId >= 0 && _w138LastFromMs.TryGetValue((byte)_w138HostId, out long l) ? l : 0;
        byte reasons = _w138HostReasons;
        bool hold = W138Joiner && Wo138Codec.Hold(reasons, now, last, _w138HostPausedSinceMs);
        bool edge = hold != _w138HoldOn;
        if (!edge && !force && !hold) return;
        bool lost = !hold && reasons != 0;   // still "paused", but the link is gone (or the cap passed)
        _w138HoldOn = hold;
        if (edge)
        {
            if (hold) _w138HoldOns++;
            Console.WriteLine(hold
                ? $"MP-WO138 HOLD on: the host's copies stay visible (host paused: {Wo138Codec.Describe(reasons)})"
                : lost ? $"MP-WO138 HOLD off: the host's link is lost ({(now - last) / 1000.0:F1} s silent) -- the 3 s rule hides its copies"
                       : "MP-WO138 HOLD off: the host resumed -- the copies' silence clocks restart now");
            try { await _combat.Wo138Async(Wo138Codec.OpHold, [(byte)(hold ? 1 : 0)], ct); } catch { }
        }
        if (edge || hold)
            await ExecLuaAsync($"if KCD2MP_W138Hold then KCD2MP_W138Hold({(hold ? "true" : "false")}, {reasons}, {(lost ? "false" : "true")}) end");
    }

    // ------------------------------------------------------------------ the loop

    private async Task Wo138LoopAsync(CancellationToken ct)
    {
        long lastCfg = 0, lastAnchors = 0, lastStatus = 0, lastNativeTell = 0, lastLevers = 0, lastHold = 0, lastStats = W138NowMs();
        bool lastSentOn = false, toldNative = false;
        while (!ct.IsCancellationRequested && _w138Connected)
        {
            try { await Task.Delay(250, ct); } catch { return; }
            try
            {
                long now = W138NowMs();
                bool host = W138Host;

                // ---- host: the native sender
                bool wantOn = host && _w138NativeSwitch && _w138HaveTrack && _where != GameWhere.Menu && _where != GameWhere.Loading;
                if (Interlocked.Exchange(ref _w138TrackDirty, 0) == 1 || wantOn != lastSentOn || now - lastCfg >= 5000)
                {
                    lastCfg = now;
                    if (wantOn)
                    {
                        List<(string, byte)> set;
                        lock (_w138TrackParts) set = _w138Track;
                        foreach (var body in Wo138Codec.TrackBodies(_w138TrackGen, set))
                            await _combat.Wo138Async(Wo138Codec.OpTrack, body, ct);
                    }
                    var r = await _combat.Wo138Async(Wo138Codec.OpConfig, Wo138Codec.ConfigBody(wantOn, _w138Cfg), ct);
                    if (wantOn != lastSentOn)
                        Console.WriteLine($"MP-WO138 native sender {(wantOn ? "ON" : "off")} ({(r is null ? "the DLL did not answer: the Lua sender streams" : $"{_w138Track.Count} tracked")})");
                    lastSentOn = wantOn;
                }
                if (wantOn && now - lastAnchors >= 500)
                {
                    lastAnchors = now;
                    var anchors = new List<(float, float, float)>();
                    var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(5);
                    foreach (var kv in _ghostLastPos) if (kv.Value.AtUtc >= cutoff && anchors.Count < 8) anchors.Add((kv.Value.X, kv.Value.Y, kv.Value.Z));
                    await _combat.Wo138Async(Wo138Codec.OpAnchors, Wo138Codec.AnchorsBody(anchors), ct);
                }
                if (now - lastStatus >= 1000)
                {
                    lastStatus = now;
                    var st = await _combat.Wo138StatusAsync(ct);
                    bool active = wantOn && st is { Sending: true };
                    if (active != _w138NativeActive)
                    {
                        _w138NativeActive = active;
                        Console.WriteLine(active ? "MP-WO138 the DLL streams this machine's NPCs (the Lua sender is quiet)"
                                                 : "MP-WO138 the Lua sender streams (the DLL is not sending)");
                    }
                    if (active && (now - lastNativeTell >= 2000 || !toldNative)) { lastNativeTell = now; toldNative = true; await ExecLuaAsync("if KCD2MP_W138NativeSend then KCD2MP_W138NativeSend(6) end"); }
                    else if (!active && toldNative) { toldNative = false; await ExecLuaAsync("if KCD2MP_W138NativeSend then KCD2MP_W138NativeSend(0) end"); }
                }

                // ---- the announcement (both roles; SendPauseIfChangedAsync compares)
                _ = _sendPauseIfChanged?.Invoke();

                // ---- joiner: the hold (re-asserted every 2 s while on)
                if (W138Joiner && (now - lastHold >= 2000 || _w138HoldOn != Wo138Codec.Hold(_w138HostReasons, now,
                        _w138HostId >= 0 && _w138LastFromMs.TryGetValue((byte)_w138HostId, out long lf) ? lf : 0, _w138HostPausedSinceMs)))
                {
                    lastHold = now;
                    await Wo138ApplyHoldAsync(ct);
                }

                // ---- both: the levers, only in a session with a partner here
                bool partner = false;
                var fresh = DateTime.UtcNow - TimeSpan.FromSeconds(10);
                foreach (var kv in _ghostLastPos) if (kv.Value.AtUtc >= fresh) { partner = true; break; }
                bool levers = Wo138Codec.LeversOn(_w138Connected, _combat.IsConnected, partner, _w138LeversSwitch) && _where != GameWhere.Menu;
                if (levers != _w138LeversApplied || now - lastLevers >= 5000)
                {
                    lastLevers = now;
                    var r = await _combat.Wo138Async(Wo138Codec.OpLevers, Wo138Codec.LeversBody(levers, Wo138Codec.DefaultMask), ct);
                    if (levers != _w138LeversApplied)
                    {
                        Console.WriteLine(levers
                            ? $"MP-WO138 levers ON (a partner is here): the ESC menu and a video no longer pause this world{(r is { Ok: true } ? "" : " [the DLL's gate is not armed: they still do]")}; the inventory does not slow it"
                            : "MP-WO138 levers off (no partner here, or no session): menus pause as usual");
                        await ExecLuaAsync($"if KCD2MP_W138ApseRatio then KCD2MP_W138ApseRatio({(levers ? "true" : "false")}) end");
                    }
                    _w138LeversApplied = levers;
                }

                if (now - lastStats >= 60_000)
                {
                    lastStats = now;
                    _w138LastText = await _combat.Wo138TextAsync(ct) ?? "native: no answer";
                    Console.WriteLine(Wo138StatsLine());
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Console.WriteLine($"MP-WO138 tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private string Wo138StatsLine() => FormattableString.Invariant(
        $"MP-WO138-STATS role={(W138Host ? "host" : W138Joiner ? "joiner" : "-")} native={(_w138NativeActive ? 1 : 0)} switch={(_w138NativeSwitch ? 1 : 0)} tracked={_w138Track.Count} rows_in={_w138RowsIn} rows_sent={_w138RowsSent} lua_dropped={_w138LuaDropped} pause_state={Wo138Codec.Describe(_w138LastReasons)} edges={_w138PauseEdges} host_id={_w138HostId} host_state={Wo138Codec.Describe(_w138HostReasons)} hold={(_w138HoldOn ? 1 : 0)} holds={_w138HoldOns} levers={(_w138LeversApplied ? 1 : 0)} | {_w138LastText}");
}
