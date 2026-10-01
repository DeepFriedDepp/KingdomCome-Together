// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-147: the joiner can fight, and the leash pulls -- the agent's half
// (docs/WO-147-findings.md; the rules are Wo147Rules in Wo147.cs).
//
// THE LEASH (both roles)
//   * leash frames have their own lane from the socket reader: they never wait
//     behind NPC traffic in the processor (the field: 40-63 s behind);
//   * every partner's position is recorded the moment it is read, on the
//     sender's own clock: the host's leash uses a joiner's position only while
//     it is current (a backlog's burst of old samples is not a reading), and a
//     partner moving faster than any horse is FLYING (the Modding Tools'
//     developer fly mode) -- its distance counts, but the host's NPC stream
//     does not follow it across the map;
//   * the joiner tells a jump, a flight and a fast travel apart (the field
//     called a flight "a fast travel the block missed");
//   * a pull with no ground beside the host (the area not loaded on the joiner
//     yet) lands on a spot the host just stood on, then on the ground beside
//     him once it has loaded.
public partial class GameBridge
{
    // ---- partners' positions, as read (reader thread writes, leash loop reads) ----
    private readonly ConcurrentDictionary<byte, Wo147Rules.PeerPositions> _w147PeerPos = new();
    private readonly ConcurrentDictionary<byte, List<(float X, float Y, float Z, long AtMs)>> _w147Trail = new();
    private readonly ConcurrentDictionary<byte, (bool Stale, long SinceMs)> _w147StaleNoted = new();
    private readonly ConcurrentDictionary<byte, bool> _w147PeerFlyingNoted = new();

    // ---- this machine's own motion (the leash loop) ----
    private readonly Wo147Rules.OwnMotion _w147Own = new();
    private bool _w147OwnFlyingNoted;
    private long _w147LeashLaneFrames;

    private static long W147NowMs() => Environment.TickCount64;
    private static long W147StampMs(long stopwatchTicks) => stopwatchTicks * 1000 / Stopwatch.Frequency;

    private void Wo147LeashOnConnect()
    {
        foreach (var p in _w147PeerPos.Values) lock (p) p.Reset();
        _w147PeerPos.Clear();
        _w147Trail.Clear();
        _w147StaleNoted.Clear();
        _w147PeerFlyingNoted.Clear();
        _w147Own.Reset();
        _w147OwnFlyingNoted = false;
    }

    // ---------------------------------------------------------------- the reader

    private static bool Wo147IsLeashFrame(int type) => type == Protocol.LeashDown || type == Protocol.LeashStateDown;

    /// <summary>The reader: a partner's position, stamped the moment its bytes are in (Stopwatch ms).</summary>
    private void Wo147NotePositionAtRead(int type, byte[] payload, long arrival)
    {
        if (type != Protocol.Ghost || !PositionCodec.TryDecodeGhost(payload, out var gs)) return;
        long at = W147StampMs(arrival);
        var pos = _w147PeerPos.GetOrAdd(gs.GhostId, _ => new Wo147Rules.PeerPositions());
        lock (pos) pos.Feed(gs.X, gs.Y, gs.SenderMs, at);
        var trail = _w147Trail.GetOrAdd(gs.GhostId, _ => new List<(float, float, float, long)>());
        lock (trail)
        {
            if (trail.Count == 0 || at - trail[^1].AtMs >= 250) trail.Add((gs.X, gs.Y, gs.Z, at));
            if (trail.Count > 160) trail.RemoveRange(0, trail.Count - 160);   // ~40 s at 4 Hz
        }
    }

    /// <summary>The leash's own lane: its frames are handled the moment they are read.</summary>
    private async Task Wo147LeashLaneAsync(ChannelReader<InFrame> lane, CancellationToken ct)
    {
        try
        {
            await foreach (var f in lane.ReadAllAsync(ct))
            {
                if (Wo140DropSeparate(f.Type)) continue;   // WO-140: a joiner in its own world
                Interlocked.Increment(ref _w147LeashLaneFrames);
                try { await Wo123OnFrameAsync(f.Type, f.Payload, ct); }
                catch (Exception ex) { Console.WriteLine($"MP-LEASH lane: a frame failed: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    // ---------------------------------------------------------------- host: distances

    /// <summary>
    /// The host's leash distance to joiner <paramref name="id"/>, or null when there is no current reading
    /// (none yet, the partner's samples are a backlog behind, or none arrived for 5 s). One line when a
    /// partner's position goes stale and one when it is current again.
    /// </summary>
    private double? Wo147LeashDistance(byte id, float hostX, float hostY)
    {
        if (!_w147PeerPos.TryGetValue(id, out var pos)) return null;
        long now = W147StampMs(Stopwatch.GetTimestamp());
        bool fresh; long lag; float x, y; bool any; var motion = Wo147Rules.Motion.Normal; float speed;
        lock (pos) { any = pos.Any; fresh = pos.Fresh(now); lag = pos.LagMs; x = pos.X; y = pos.Y; motion = pos.LastMotion; speed = pos.LastSpeedMps; }
        if (!any) return null;
        (bool Stale, long SinceMs) noted = _w147StaleNoted.TryGetValue(id, out var n) ? n : (false, 0L);
        if (!fresh && !noted.Stale)
        {
            _w147StaleNoted[id] = (true, now);
            Console.WriteLine(FormattableString.Invariant(
                $"MP-LEASH host: joiner {id}'s position is not current (its samples run {lag / 1000.0:F1} s behind, or none for 5 s) -- no leash decision on old positions until they are"));
        }
        else if (fresh && noted.Stale)
        {
            _w147StaleNoted[id] = (false, now);
            Console.WriteLine(FormattableString.Invariant($"MP-LEASH host: joiner {id}'s position is current again (after {(now - noted.SinceMs) / 1000.0:F0} s)"));
        }
        bool flying = motion == Wo147Rules.Motion.Flying;
        if (flying != (_w147PeerFlyingNoted.TryGetValue(id, out bool wasF) && wasF))
        {
            _w147PeerFlyingNoted[id] = flying;
            if (flying) Console.WriteLine(FormattableString.Invariant(
                $"MP-LEASH host: joiner {id} moves {speed:F0} m/s by its own clock -- flying (no horse is that fast); real positions: the leash counts them, the NPC stream stops following it until it lands"));
        }
        return fresh ? LeashLogic.Dist2D(hostX, hostY, x, y) : null;
    }

    /// <summary>A pull's answer may take twice the partner's measured lag plus 4 s (0 when there is none).</summary>
    private int Wo147PeerLagTimeoutMs(byte id)
    {
        if (!_w147PeerPos.TryGetValue(id, out var pos)) return 0;
        long lag; lock (pos) lag = pos.LagMs;
        return (int)Math.Clamp(2 * lag + 4000, 0, 90_000);
    }

    /// <summary>WO-138's anchors: a flying partner's position is not a place to stream NPCs around.</summary>
    private bool Wo147PeerFlying(byte id)
    {
        if (!_w147PeerPos.TryGetValue(id, out var pos)) return false;
        lock (pos) return pos.LastMotion == Wo147Rules.Motion.Flying;
    }

    // ---------------------------------------------------------------- joiner: own motion

    private bool Wo147OwnFlying => _w147Own.Flying;

    /// <summary>
    /// Every leash tick on a joiner: this player's own motion. A jump (a respawn, a quest or cutscene
    /// teleport, our own pull) and a flight are told apart from a fast travel, which the engine announces
    /// itself ("FastTravel: started", Wo114OnLocalFastTravel). Nothing here is reported as a fast travel.
    /// </summary>
    private void Wo147JoinerMotionCheck()
    {
        if (!_hasPushed || !_joinedWorld || _localDowned || _where is GameWhere.Loading or GameWhere.Menu || _jj is not null || _rewinding
            || (DateTime.UtcNow - _leashPulledUtc).TotalSeconds < 5 || Volatile.Read(ref _leashPulling) != 0)
        { _w147Own.Reset(); return; }
        var m = _w147Own.Feed(_lastX, _lastY, W147NowMs(), _lastRiding);
        if (m == Wo147Rules.Motion.Jump)
            Console.WriteLine(FormattableString.Invariant(
                $"MP-LEASH joiner: this player moved {_w147Own.StepM:F0} m in one step -- a teleport (a quest, a cutscene, a wake), not a fast travel (none started)"));
        bool flying = _w147Own.Flying;
        if (flying != _w147OwnFlyingNoted)
        {
            _w147OwnFlyingNoted = flying;
            Console.WriteLine(flying
                ? FormattableString.Invariant($"MP-LEASH joiner: this player is flying ({_w147Own.SpeedMps:F0} m/s, no horse, no fast travel -- the developer fly mode?): the host's leash counts every real position; a pull places a flier too")
                : "MP-LEASH joiner: this player is on its feet again");
        }
    }

    // ---------------------------------------------------------------- joiner: the pull's target

    /// <summary>
    /// Where the host is, for a pull: the command's own position, or the host's avatar's newest sample when
    /// that is newer (a command that waited in a backlog carries an old position).
    /// </summary>
    private (float X, float Y, float Z) Wo147PullTargetHost(LeashCommand c)
    {
        byte hid = _leashHostId != 0xFF ? _leashHostId : _hostModeFrom;
        if (hid != 0xFF && _w147Trail.TryGetValue(hid, out var trail))
        {
            lock (trail)
            {
                if (trail.Count > 0 && W147StampMs(Stopwatch.GetTimestamp()) - trail[^1].AtMs < 2000
                    && LeashLogic.Dist2D(trail[^1].X, trail[^1].Y, c.HostX, c.HostY) < 200)
                    return (trail[^1].X, trail[^1].Y, trail[^1].Z);
            }
        }
        return (c.HostX, c.HostY, c.HostZ);
    }

    private (float X, float Y, float Z) Wo147PullFallbackSpot(float hx, float hy, float hz)
    {
        byte hid = _leashHostId != 0xFF ? _leashHostId : _hostModeFrom;
        List<(float X, float Y, float Z, long AtMs)> copy = new();
        if (hid != 0xFF && _w147Trail.TryGetValue(hid, out var trail)) lock (trail) copy.AddRange(trail);
        return Wo147Rules.PullFallbackSpot(hx, hy, hz, _lastX, _lastY, copy, W147StampMs(Stopwatch.GetTimestamp()));
    }

    // ================================================================ the frame backlog (mp_npc_catchup)
    //
    // The field (0.42.2): the joiner's agent handled the host's frames up to 833 s late (the host's own, on
    // loopback, 139 s). The reader kept up; the one processor did not: every Lua batch that fills waits on the
    // game's ExecuteString, and the game slowed under a churn this lag itself caused -- the mod released every
    // puppet whose pushes had not come for 3 s ("stream silent"), though its samples sat in this queue, and the
    // next late push started it again (up to ~31 console commands a second: pause, resume, stance resets). Two
    // changes break the loop: behind, a sample a newer one of the same NPC already supersedes is skipped; and the
    // agent, which sees when samples really arrive, says when a stream fell silent -- the mod's own clock rule is
    // the fallback for an agent that is gone.

    private volatile bool _w147Catchup = true;   // mp_npc_catchup (the mod's w147_cfg)
    private readonly ConcurrentDictionary<string, (ushort Seq, byte Flags, long AtMs)> _w147NpcNewest = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w147NpcProcessedMs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w147NpcPushedAtMs = new(StringComparer.Ordinal);   // when a sample last went through (processor clock)
    private readonly ConcurrentDictionary<string, bool> _w147SilentSent = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<byte, long> _w147GhostNewestMs = new();
    private long _w147ReadAtMs, _w147ProcAtMs, _w147LagMaxMs, _w147LagWinMs;
    private long _w147Superseded, _w147GhostSuperseded, _w147SilentNotes;
    private bool _w147CatchupToldOff;

    /// <summary>The reader: the newest sample of every NPC and avatar, as read (before any queue).</summary>
    private void Wo147NoteNpcAtRead(int type, byte[] payload, long arrival)
    {
        long at = W147StampMs(arrival);
        Volatile.Write(ref _w147ReadAtMs, at);
        if (type == Protocol.NpcStateDown && payload.Length >= 2 && payload.Length == 2 + payload[1] + Protocol.NpcStateFixedTail)
        {
            int o = 2 + payload[1];
            string name = Encoding.UTF8.GetString(payload, 2, payload[1]);
            _w147NpcNewest[name] = (BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(o + Protocol.NpcStateSeqOffset)),
                                    payload[o + Protocol.NpcStateFlagsOffset], at);
            _w147SilentSent.TryRemove(name, out _);
        }
        else if (type == Protocol.Ghost && payload.Length >= 1) _w147GhostNewestMs[payload[0]] = at;
    }

    /// <summary>The processor takes a frame read at <paramref name="arrival"/>: its lag, noted for the stats and the mod.</summary>
    private void Wo147NoteProcessing(long arrival)
    {
        long at = W147StampMs(arrival);
        Volatile.Write(ref _w147ProcAtMs, at);
        long lag = W147StampMs(Stopwatch.GetTimestamp()) - at;
        long max;
        while (lag > (max = Interlocked.Read(ref _w147LagMaxMs)) && Interlocked.CompareExchange(ref _w147LagMaxMs, lag, max) != max) { }
        while (lag > (max = Interlocked.Read(ref _w147LagWinMs)) && Interlocked.CompareExchange(ref _w147LagWinMs, lag, max) != max) { }
    }

    /// <summary>True: this NPC sample is superseded and the processor is behind -- skip it whole.</summary>
    private bool Wo147NpcSkip(string name, ushort seq, byte rawFlags, long arrival)
    {
        if (!_w147Catchup || !_w147NpcNewest.TryGetValue(name, out var nw)) return false;
        long nowMs = W147StampMs(Stopwatch.GetTimestamp());
        double lag = nowMs - W147StampMs(arrival);
        double since = _w147NpcPushedAtMs.TryGetValue(name, out long last) ? nowMs - last : double.MaxValue;
        if (!Wo147Rules.SupersededUnderLag(true, lag, seq, rawFlags, nw.Seq, nw.Flags, since)) return false;
        Interlocked.Increment(ref _w147Superseded);
        return true;
    }

    /// <summary>The processor handed the mod this NPC's sample read at <paramref name="arrival"/>.</summary>
    private void Wo147NpcProcessed(string name, long arrival)
    {
        _w147NpcProcessedMs[name] = W147StampMs(arrival);
        _w147NpcPushedAtMs[name] = W147StampMs(Stopwatch.GetTimestamp());
    }

    /// <summary>True: this avatar sample is superseded (no state block of its own) and the processor is behind -- no Lua push.</summary>
    private bool Wo147GhostSkip(byte ghostId, long arrival, bool hasStateBlock)
    {
        if (!_w147Catchup || hasStateBlock || !_w147GhostNewestMs.TryGetValue(ghostId, out long newest)) return false;
        long at = W147StampMs(arrival);
        double lag = (Stopwatch.GetTimestamp() - arrival) * 1000.0 / Stopwatch.Frequency;
        if (lag < Wo147Rules.CatchupLagMs || newest <= at) return false;
        Interlocked.Increment(ref _w147GhostSuperseded);
        return true;
    }

    /// <summary>
    /// Once a second: the agent's word to the mod (how far behind it is; the mod releases a puppet only on this
    /// agent's say-so while the word is fresh), and the streams that fell silent at the reader once the mod has
    /// their last sample. Off (mp_npc_catchup off): the mod is told once, and its own 3 s rule decides as before.
    /// </summary>
    private async Task Wo147SilenceTickAsync()
    {
        if (!_w147Catchup)
        {
            if (!_w147CatchupToldOff) { _w147CatchupToldOff = true; await ExecLuaAsync("if KCD2MP_NpcSilenceAgent then KCD2MP_NpcSilenceAgent(-1) end"); }
            return;
        }
        _w147CatchupToldOff = false;
        long now = W147StampMs(Stopwatch.GetTimestamp());
        // how far behind: now, or the worst of the last second (what the mod's renderer has to cover)
        long behind = Math.Max(Math.Max(0, Volatile.Read(ref _w147ReadAtMs) - Volatile.Read(ref _w147ProcAtMs)),
                               Interlocked.Exchange(ref _w147LagWinMs, 0));
        await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_NpcSilenceAgent then KCD2MP_NpcSilenceAgent({behind}) end"));
        foreach (var (name, nw) in _w147NpcNewest)
        {
            if (now - nw.AtMs > 120_000) { _w147NpcNewest.TryRemove(name, out _); _w147NpcProcessedMs.TryRemove(name, out _); _w147NpcPushedAtMs.TryRemove(name, out _); _w147SilentSent.TryRemove(name, out _); continue; }
            long done = _w147NpcProcessedMs.TryGetValue(name, out long d) ? d : -1;
            if (!Wo147Rules.SilenceDue(now, nw.AtMs, done, _w147SilentSent.ContainsKey(name))) continue;
            _w147SilentSent[name] = true;
            Interlocked.Increment(ref _w147SilentNotes);
            await ExecLuaAsync("if KCD2MP_NpcStreamSilent then KCD2MP_NpcStreamSilent(\"" + name + "\") end");
        }
    }

    private string Wo147CatchupStatsText()
    {
        long lagMax = Interlocked.Exchange(ref _w147LagMaxMs, 0);
        return FormattableString.Invariant(
            $"catchup={(_w147Catchup ? "on" : "off")} lag_max_ms={lagMax} superseded={Interlocked.Read(ref _w147Superseded)} ghost_superseded={Interlocked.Read(ref _w147GhostSuperseded)} silent_notes={Interlocked.Read(ref _w147SilentNotes)}");
    }

    // ================================================================ settings, the 1 s loop, stats

    private volatile bool _w147HostileEngage = true;   // mp_hostile_engage (joiner)
    private volatile int _w147LeashCapMs = LeashLogic.HoldCapMs;   // mp_leash_cap_s (host)
    private float _w147RelMax = -0.1f;                  // a copy at or under this relationship is an enemy
    private long _w147EngageOn, _w147EngageOff, _w147EngageFail, _w147EngageFriendly, _w147Ticks;

    /// <summary>w147_cfg from the mod: hostile_engage=on quest_safety=on leash_cap_s=60 range_m=12 rel_max=-0.10</summary>
    private void Wo147OnCfg(string? arg)
    {
        foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq <= 0) continue;
            string k = kv[..eq], v = kv[(eq + 1)..];
            switch (k)
            {
                case "hostile_engage": _w147HostileEngage = v == "on"; break;
                case "quest_safety": _w147QuestSafety = v == "on"; break;
                case "leash_cap_s": if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)) _w147LeashCapMs = Math.Clamp(s, 0, 3600) * 1000; break;
                case "rel_max": if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r)) _w147RelMax = r; break;
                case "npc_catchup": _w147Catchup = v == "on"; break;
            }
        }
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W147 cfg hostile_engage={(_w147HostileEngage ? "on" : "off")} quest_safety={(_w147QuestSafety ? "on" : "off")} leash_cap_s={_w147LeashCapMs / 1000} rel_max={_w147RelMax:F2} npc_catchup={(_w147Catchup ? "on" : "off")}"));
        if (!_w147HostileEngage) _ = Wo147ReleaseAllAsync("mp_hostile_engage off");
    }

    private void Wo147OnConnect(CancellationToken ct)
    {
        _w147Local.Clear();
        _ = ExecLuaAsync("if KCD2MP_W147CfgEmit then KCD2MP_W147CfgEmit() end");
        _ = Wo147QuestIndexAsync();   // read once, early (about a second)
        _ = Wo147LoopAsync(ct);
    }

    private async Task Wo147LoopAsync(CancellationToken ct)
    {
        long lastStats = Environment.TickCount64;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); } catch { return; }
            try
            {
                Interlocked.Increment(ref _w147Ticks);
                if (_where != GameWhere.Menu && !Wo136Holding) await Wo147HostileTickAsync(ct);
                if (_where != GameWhere.Menu) await Wo147SilenceTickAsync();   // the backlog fix: a stream's silence is the agent's word
                if (Environment.TickCount64 - lastStats >= 60_000)
                {
                    lastStats = Environment.TickCount64;
                    string? nat = null;
                    try { nat = await _combat.Wo147StatusAsync(ct); } catch { }
                    Console.WriteLine(FormattableString.Invariant(
                        $"MP-W147-STATS hostile_engage={(_w147HostileEngage ? "on" : "off")} engaged={_w147Local.Count} engage_on={_w147EngageOn} engage_off={_w147EngageOff} engage_fail={_w147EngageFail} friendly_skipped={_w147EngageFriendly} leash_lane={_w147LeashLaneFrames} {Wo147QuestStatsText()} {Wo147CatchupStatsText()} | {nat ?? "native: no answer"}"));
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Console.WriteLine($"MP-W147 tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    // ================================================================ Phase 1: the joiner fights

    // Copies this player engaged on its own (name -> eid, last time the conditions held).
    private readonly ConcurrentDictionary<string, (uint Eid, long SeenMs, float Rel)> _w147Local = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _w147FriendlyNoted = new(StringComparer.Ordinal);
    // Copies the host's stream calls dead (the live run: 0.5 s after the host's death landed, the mod still read the
    // copy alive and the tick engaged it again -- combat mode put back on a dying body).
    private readonly ConcurrentDictionary<string, bool> _w147StreamDead = new(StringComparer.Ordinal);

    private void Wo147NoteStreamDead(string name, bool dead)
    {
        if (dead) _w147StreamDead[name] = true;
        else _w147StreamDead.TryRemove(name, out _);
    }
    private long _w147DrawnSinceMs = -1, _w147SheathedSinceMs = -1;

    /// <summary>
    /// WO-147 1.4 (the field: "NPCs ignore him; he can only lock on to and attack an NPC the host is already
    /// fighting"): every copy of a host NPC is suspended -- its brain never perceives this player, never
    /// joins a skirmish with him -- and only WO-132's engagement, driven by the HOST NPC's combat state, ever
    /// put one into this player's combat mode. Now an ENEMY's copy (the game's own relationship to this
    /// player, soul:GetRelationship, at or under rel_max) within 12 m is engaged the same way while this
    /// player's weapon is out: a skirmish with this player and combat mode held on the copy -- lock on, block,
    /// swing. Its hits then go to the host as this player's (the gate, the avatar the attacker there), the
    /// host's NPC turns on the avatar, and WO-132's engagement follows the host's fight. A friend's copy is
    /// never engaged. Released 5 s after the weapon goes away, past 15 m, when the copy dies or goes down,
    /// or with mp_hostile_engage off.
    /// </summary>
    private async Task Wo147HostileTickAsync(CancellationToken ct)
    {
        if (!Wo131JoinerActive || !_w147HostileEngage)
        {
            if (!_w147Local.IsEmpty) await Wo147ReleaseAllAsync(!_w147HostileEngage ? "mp_hostile_engage off" : "not a joiner in the host's world");
            return;
        }
        string reply = await AskModAsync("KCD2MP_W147Hostiles", 1500);
        if (reply is "timeout" or "missing") return;
        var parsed = Wo147Rules.ParseHostiles(reply);
        if (parsed is not { } h) return;
        long now = Environment.TickCount64;
        if (h.Drawn) { if (_w147DrawnSinceMs < 0) _w147DrawnSinceMs = now; _w147SheathedSinceMs = -1; }
        else { _w147DrawnSinceMs = -1; if (_w147SheathedSinceMs < 0) _w147SheathedSinceMs = now; }
        bool sheathedLong = !h.Drawn && _w147SheathedSinceMs >= 0 && now - _w147SheathedSinceMs >= 5000;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in h.Copies)
        {
            seen.Add(c.Name);
            bool alive = c.Alive && !_w147StreamDead.ContainsKey(c.Name);   // the host's word on a death comes first
            var want = Wo147Rules.JudgeHostileEngage(c.Rel, _w147RelMax, alive, c.DistM, h.Drawn, _w147Local.ContainsKey(c.Name), sheathedLong);
            if (want == Wo147Rules.EngageWant.Friendly)
            {
                if (h.Drawn && _w147FriendlyNoted.TryAdd(c.Name, true))
                {
                    Interlocked.Increment(ref _w147EngageFriendly);
                    Console.WriteLine(FormattableString.Invariant($"MP-W147 {c.Name} is no enemy of this player (relationship {c.Rel:F2}) -- never engaged; a hit on it is still the host's to judge"));
                }
                if (_w147Local.ContainsKey(c.Name)) await Wo147ReleaseAsync(c.Name, "no longer an enemy");
                continue;
            }
            if (want == Wo147Rules.EngageWant.Release) { if (_w147Local.ContainsKey(c.Name)) await Wo147ReleaseAsync(c.Name, !alive ? "down or dead" : sheathedLong ? "the weapon is away" : "out of reach"); continue; }
            if (want != Wo147Rules.EngageWant.Engage) continue;
            if (!_w131Guarded.TryGetValue(c.Name, out uint eid) || !_nativeBound.ContainsKey(c.Name)) continue;   // only the host's bound copy (WO-131/132)
            if (!Wo136Rules.MayEngage(Wo136HostNpcDown(c.Name))) continue;   // WO-136: never while the host's NPC is down
            var r = await _combat.Wo132EngageAsync(true, eid, Wo147Rules.LocalEngageState(), ct);
            if (r.Reason == 6) { if (_w147Local.ContainsKey(c.Name)) await Wo147ReleaseAsync(c.Name, "past 15 m"); continue; }
            if (!r.Ok) { if (Interlocked.Increment(ref _w147EngageFail) <= 10) Console.WriteLine($"MP-W147 engage {c.Name} FAILED (reason {r.Reason})"); continue; }
            bool fresh = !_w147Local.ContainsKey(c.Name);
            _w147Local[c.Name] = (eid, now, c.Rel);
            if (fresh)
            {
                Interlocked.Increment(ref _w147EngageOn);
                Console.WriteLine(FormattableString.Invariant(
                    $"MP-W147 engage on {c.Name} (eid 0x{eid:X}, {c.DistM:F1} m, relationship {c.Rel:F2}): an enemy of this player, the weapon is out -- the game's own combat mode against the host's copy (skirmish {(r.Skirmish ? "added" : "NOT added")}), whether or not the host fights it; its hits go to the host"));
            }
        }
        // A copy no longer listed (the stream stopped, it left the 12 m list): let it go.
        foreach (var name in _w147Local.Keys.ToArray())
            if (!seen.Contains(name)) await Wo147ReleaseAsync(name, "no longer near");
    }

    // ---------------------------------------------------------------- the hit's source and the host's watch

    private long _w147NotOwnBlow, _w147FightWatches, _w147GuardsForgotten;
    private readonly ConcurrentDictionary<string, long> _w147FightWatch = new(StringComparer.Ordinal);

    private bool Wo147HitHookArmed => _dllWo121Status.Contains("hit_slot=armed", StringComparison.Ordinal);

    /// <summary>
    /// WO-147: on a joiner only this player's own blows go to the host. The DLL's sampler reports any drop of an
    /// NPC's health near the player; the field's host avatar hit a hidden local copy and the gate took it for the
    /// joiner's (dropped only because that copy was not bound). The "by the player" bit comes from the hit hook,
    /// so without an armed hook the old rule stands.
    /// </summary>
    private bool Wo147DropNotOwnBlow(bool byPlayer, float health, float stamina)
    {
        if (!Wo147Rules.DropNotOwnBlow(Wo131JoinerActive, byPlayer, Wo147HitHookArmed)) return false;
        long n = Interlocked.Increment(ref _w147NotOwnBlow);
        if (n <= 10 || n % 100 == 0)
            Console.WriteLine(FormattableString.Invariant(
                $"MP-W147 a drop of {health:F1} hp / {stamina:F1} st near this player that was not its own blow -- not sent (#{n}; the host's world has its own)"));
        return true;
    }

    /// <summary>Host: an NPC fights an avatar -- watch its combat state for 30 s (WO-132's watch keys on a drawn weapon only).</summary>
    private void Wo147WatchFight(string npcName, string why)
    {
        if (!_isDamageAuthority || !_sharedWorld || npcName.Length == 0 || npcName.StartsWith("kcd2mp_", StringComparison.Ordinal)) return;
        long now = Environment.TickCount64;
        bool fresh = !_w147FightWatch.TryGetValue(npcName, out long until) || until < now;
        _w147FightWatch[npcName] = now + 30_000;
        if (!fresh || _w132Watched.ContainsKey(npcName)) return;
        _w132Watched[npcName] = now;
        Interlocked.Increment(ref _w147FightWatches);
        _ = Task.Run(async () =>
        {
            uint? eid = await _combat.Wo132WatchAsync(true, npcName);
            if (eid is null) { _w132Watched.TryRemove(npcName, out _); return; }
            Console.WriteLine($"MP-W147 watching {npcName} (eid 0x{eid:X}): {why} -- its fight with the avatar goes to the joiner (combat mode, lock-on)");
        });
    }

    /// <summary>mp_test_hit: "&lt;npc&gt; &lt;eidHex&gt; &lt;hp&gt; &lt;st&gt;" -- the DLL lands this player's blow (a live test's stand-in for a key).</summary>
    private void Wo147OnTestHit(string arg)
    {
        var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 4 || !NpcNamePattern.IsMatch(p[0])
            || !uint.TryParse(p[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint eid) || eid == 0
            || !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float hp) || !(hp is >= 0 and <= 500)
            || !float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float st) || !(st is >= 0 and <= 500))
        { Console.WriteLine($"MP-W147 test hit: malformed '{arg}'"); return; }
        _ = Task.Run(async () =>
        {
            var r = await _combat.Wo147TestPlayerHitAsync(eid, hp, st);
            Console.WriteLine(r is { } x
                ? FormattableString.Invariant($"MP-W147 test hit on {p[0]} (eid 0x{eid:X}) hp={hp:F1} st={st:F1}: {(x.Ok ? "landed" : $"refused (reason {x.Reason})")} health {x.Before:F1} -> {x.After:F1} -- the sampler forwards it like a swing")
                : $"MP-W147 test hit on {p[0]}: no answer from the DLL");
        });
    }

    private bool Wo147FightWatched(string npcName) =>
        _w147FightWatch.TryGetValue(npcName, out long until) && until >= Environment.TickCount64;

    /// <summary>
    /// A load re-creates every copy (the field: the joiner's rejoin kept tpod_malik's entity id, the agent saw "the
    /// same eid" and never guarded the new body, and the host's next blow killed it). Forget the guards; the next
    /// puppet start guards the new bodies.
    /// </summary>
    private void Wo147ForgetGuardsOnLoad(string what)
    {
        // The souls read from the copies' bodies go too: a load reuses entity ids (the live run's stand-ins came
        // back with their old ids), so a cached (id, soul) pair named the dead old soul.
        _copySoulGuid.Clear();
        int n = _w131Guarded.Count;
        if (n == 0) return;
        _w131Guarded.Clear();
        _w131HpWritten.Clear();
        Interlocked.Add(ref _w147GuardsForgotten, n);
        Console.WriteLine($"MP-W147 a load ({what}): {n} copy guard(s) forgotten -- the re-created copies are guarded again as their streams start");
    }

    private async Task Wo147ReleaseAsync(string name, string why)
    {
        if (!_w147Local.TryRemove(name, out var e)) return;
        // WO-132's own engagement (the host's NPC in combat) keeps the copy: nothing to undo then.
        if (_w132Engaged.ContainsKey(name)) { Console.WriteLine($"MP-W147 {name}: {why} -- the host's NPC is fighting, WO-132 keeps it engaged"); return; }
        Interlocked.Increment(ref _w147EngageOff);
        try { await _combat.Wo132EngageAsync(false, e.Eid, default); } catch { }
        Console.WriteLine($"MP-W147 engage off {name}: {why} -- still bound and paused");
    }

    private async Task Wo147ReleaseAllAsync(string why)
    {
        foreach (var name in _w147Local.Keys.ToArray()) await Wo147ReleaseAsync(name, why);
    }

    // ================================================================ quest safety (Phase 2b)

    private volatile bool _w147QuestSafety = true;   // mp_quest_safety on|off (Lua KCD2MP.w147.questSafety; event w147_cfg)
    private Task<QuestValueIndex?>? _w147QuestIndex;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _w147PortValues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _w147PathType = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _w147NoPortNoted = new(StringComparer.Ordinal);
    private DateTime _w147LastDialogueUtc = DateTime.MinValue;
    private long _w147DestructiveIn, _w147DestructiveAlready, _w147DestructiveConv, _w147DestructiveRefused;

    /// <summary>The quest value index, read once from the installed game's Scripts.pak (null when it is not found).</summary>
    private Task<QuestValueIndex?> Wo147QuestIndexAsync() => _w147QuestIndex ??= Task.Run(() =>
    {
        try
        {
            string? pak = QuestValueIndex.FindScriptsPak();
            if (pak is null) { Console.WriteLine("MP-W147 quest values: Scripts.pak not found -- destructive steps are told by their names only, corrections by what was seen"); return (QuestValueIndex?)null; }
            var sw = Stopwatch.StartNew();
            var idx = QuestValueIndex.LoadFrom(pak);
            Console.WriteLine($"MP-W147 quest values: {idx.TypeCount} State types from {idx.FileCount} quest files read in {sw.ElapsedMilliseconds} ms -- destructive steps and correction ports come from the game's own data");
            return idx;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MP-W147 quest values failed ({ex.GetType().Name}: {ex.Message}) -- names only");
            return null;
        }
    });

    /// <summary>What <paramref name="port"/> produced on <paramref name="path"/> here (a change, an apply): a correction may fire it for that value.</summary>
    private void Wo147LearnPortValue(string path, string? port, int value, string? type)
    {
        if (!string.IsNullOrEmpty(type)) _w147PathType[path] = type;
        // Only a port that sets a value is learnable (Increment, Exec and the like are relative or data-driven).
        if (!Wo137Text.IsPort(port) || !port!.StartsWith("Set", StringComparison.Ordinal)) return;
        var m = _w147PortValues.GetOrAdd(path, _ => new ConcurrentDictionary<string, int>(StringComparer.Ordinal));
        m[port!] = value;
        if (_w147PortValues.Count > 4000) _w147PortValues.Clear();   // a long session: start over rather than grow
    }

    /// <summary>This player is in a conversation, or left one within 3 s (its outcome lands as the dialogue ends).</summary>
    private bool Wo147InOwnConversation()
    {
        if (LuaBusyFresh(out bool d, out _) && d) { _w147LastDialogueUtc = DateTime.UtcNow; return true; }
        return (DateTime.UtcNow - _w147LastDialogueUtc).TotalSeconds < 3;
    }

    /// <summary>
    /// The port a correction of <paramref name="path"/> toward <paramref name="hostVal"/> may fire (Wo147Rules.CorrectionPort):
    /// the type's own value name, else a port seen producing that value here; null = none (said once per State and value).
    /// </summary>
    private async Task<string?> Wo147CorrectionPortAsync(string path, int hostVal, string? knownType)
    {
        var idx = await Wo147QuestIndexAsync();
        string? type = !string.IsNullOrEmpty(knownType) ? knownType : _w147PathType.TryGetValue(path, out var t) ? t : null;
        if (string.IsNullOrEmpty(type))
        {
            try
            {
                var r = await _combat.Wo137ReadStateTypesAsync([path]);
                if (r is { Count: 1 } && r[0].Found && r[0].Type.Length > 0) { type = r[0].Type; _w147PathType[path] = type; }
            }
            catch { }
        }
        _w147PortValues.TryGetValue(path, out var learned);
        string? port = Wo147Rules.CorrectionPort(idx, type, hostVal, learned);
        if (port is null && _w147NoPortNoted.TryAdd(path + "=" + hostVal.ToString(CultureInfo.InvariantCulture), true))
            Console.WriteLine($"MP-W147 no port is known to produce {path} = {hostVal} ({type ?? "type unknown"}) -- not corrected (the next join loads it exactly)");
        return port;
    }

    /// <summary>
    /// Host: a joiner's request that the host would apply. A DESTRUCTIVE one (Wo147Rules.Destructive) is not
    /// taken on the joiner's word: out of the joiner's own conversation it is that conversation's outcome
    /// and applies; otherwise this world gets <see cref="Wo147Rules.DestructiveWaitMs"/> to reach the value by
    /// itself (the NPC really died or went down here, this world's own graph led there) = already; else it
    /// is refused and the joiner's copy goes back. True = handled here (the caller does nothing more).
    /// </summary>
    private async Task<bool> Wo147DestructiveGateAsync(byte src, uint tok, QuestChange req, int hostVal, string hostPort, string head)
    {
        if (!_w147QuestSafety) return false;
        var idx = await Wo147QuestIndexAsync();
        string? type = null;
        try
        {
            var r = await _combat.Wo137ReadStateTypesAsync([req.Path]);
            if (r is { Count: 1 } && r[0].Found) type = r[0].Type;
        }
        catch { }
        var (kind, why) = Wo147Rules.Destructive(idx, type, req.Port, req.Old, req.New);
        if (kind == Wo147Rules.Destruction.None) return false;
        Interlocked.Increment(ref _w147DestructiveIn);
        bool conv = (req.Flags & Wo147Rules.FlagConversation) != 0;
        if (conv)
        {
            Interlocked.Increment(ref _w147DestructiveConv);
            Console.WriteLine($"{head}: DESTRUCTIVE ({Wo147Rules.DestructionName(kind)}: {why}) but out of the joiner's own conversation -- its outcome, applied");
            return false;
        }
        Console.WriteLine($"{head}: DESTRUCTIVE ({Wo147Rules.DestructionName(kind)}: {why}) -- held until this world agrees (up to {Wo147Rules.DestructiveWaitMs / 1000} s)");
        // Off the request queue: the other requests go on meanwhile; the verdict comes back through it.
        _ = Task.Run(async () =>
        {
            bool reached = false;
            int last = hostVal;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < Wo147Rules.DestructiveWaitMs)
            {
                await Task.Delay(500);
                try
                {
                    var reads = await _combat.Wo137ReadStatesAsync([req.Path]);
                    if (reads is { Count: 1 } && reads[0].Found && reads[0].Ok)
                    {
                        last = reads[0].Val;
                        if (last == req.New) { reached = true; break; }
                        if (last != req.Old) break;   // this world went somewhere else
                    }
                }
                catch { }
            }
            var verdict = Wo147Rules.JudgeDestructive(reached, false);
            Wo137Post(async () =>
            {
                if (verdict == Wo147Rules.DestructiveVerdict.Already)
                {
                    Interlocked.Increment(ref _w147DestructiveAlready); Interlocked.Increment(ref _w137VerdictAlready);
                    await Wo137ReplyAsync(src, tok, "already", last, hostPort, req.Path);
                    Console.WriteLine(FormattableString.Invariant($"{head}: this world reached {last} by itself in {sw.ElapsedMilliseconds / 1000.0:F1} s -- already (counted once)"));
                    return;
                }
                Interlocked.Increment(ref _w147DestructiveRefused); Interlocked.Increment(ref _w137VerdictRefused);
                await Wo137ReplyAsync(src, tok, "refused", last, hostPort, req.Path);
                Console.WriteLine(FormattableString.Invariant(
                    $"{head}: REFUSED -- this world is still at {last} after {sw.ElapsedMilliseconds / 1000.0:F1} s ({Wo147Rules.DestructionName(kind)} happened only on the joiner's copy); the joiner's copy goes back"));
            });
        });
        return true;
    }

    private long _w147CorrectionsSkipped;

    /// <summary>A queued correction toward <paramref name="c"/>.New: false = this copy is on the host's value already (skipped).</summary>
    private async Task<bool?> Wo147CorrectionStillNeededAsync(QuestChange c)
    {
        try
        {
            var r = await _combat.Wo137ReadStatesAsync([c.Path]);
            if (r is not { Count: 1 } || !r[0].Found || !r[0].Ok) return null;
            if (r[0].Val != c.New) return true;
            Interlocked.Increment(ref _w147CorrectionsSkipped);
            Console.WriteLine(FormattableString.Invariant($"MP-W147 correction of {c.Path} ({c.Port}) skipped -- this copy is on the host's value {c.New} already"));
            return false;
        }
        catch { return null; }
    }

    private string Wo147QuestStatsText() => FormattableString.Invariant(
        $"quest_safety={(_w147QuestSafety ? "on" : "off")} destructive_in={_w147DestructiveIn} already={_w147DestructiveAlready} conversation={_w147DestructiveConv} refused={_w147DestructiveRefused} corrections_skipped={_w147CorrectionsSkipped} not_own_blow={_w147NotOwnBlow} fight_watches={_w147FightWatches} guards_forgotten={_w147GuardsForgotten}");

    /// <summary>After a fallback placement: the ground beside the host, once this area has loaded (two tries).</summary>
    private async Task Wo147SettleBesideHostAsync(byte seq)
    {
        foreach (int waitMs in new[] { 1500, 3000, 4000, 6000, 10000 })
        {
            await Task.Delay(waitMs);
            byte hid = _leashHostId != 0xFF ? _leashHostId : _hostModeFrom;
            if (hid == 0xFF || !_ghostLastPos.TryGetValue(hid, out var hp)) return;
            var pr = await _combat.JoinPlaceAsync(hp.X, hp.Y, hp.Z, LeashPlaceDistM);
            Console.WriteLine(FormattableString.Invariant(
                $"MP-LEASH joiner: pull #{seq}: the ground beside the host {(pr is { Ok: true } ? "has loaded -- placed there" : "is not loaded yet")} ({(pr is null ? "no answer" : $"residual {pr.Residual:F2} m")})"));
            if (pr is { Ok: true }) return;
        }
    }
}
