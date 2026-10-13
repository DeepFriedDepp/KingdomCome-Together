// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-179 -- 0.50.7: shared scenes. Only two kinds are shared: pre-rendered videos (both watch) and any scene that moves Henry
// (the partner is brought along). Every other cutscene and dialogue stays as it was; the scene guard (Part A) applies to all.
//
//   eyes      the DLL (wo179): every fade edge and every jump of the local player (op 32, polled at 10 Hz)
//   local     the log tail's scene stages (WO-151's feed) and the conversations with this player (WO-167's) say which scene runs
//   bus       W164 kind 15 (Wo179Rules): S start, R destination, E end, K skip key, L landed, T clock jump
//   partner   C1 the same video (its own quest copy, else the movie player), C3 the ladder, C4 the landing and the ignore window
//
// Log tags: WO179-FADE, WO179-SCENE, WO179-RELOCATE, WO179-FALLBACK, WO179-SKIP, WO179-BUS, WO179-VIDEO, WO179-BRING, WO179-QUEUE,
//           WO179-RELEASE, WO179-TIME, WO179-STATS.
public partial class GameBridge
{
    private volatile bool _w179Bus = Wo179Rules.DefaultBus, _w179Video = Wo179Rules.DefaultVideo, _w179Bring = Wo179Rules.DefaultBring,
                          _w179Ignored = Wo179Rules.DefaultIgnored, _w179FightClear = Wo179Rules.DefaultFightClear;

    private sealed class W179Scene
    {
        public int Seq; public string Name = "", Type = ""; public char Kind;
        public long StartMs, EndMs; public float SX, SY, SZ;
        public bool Sent, Relocated, Video, SkipKey, Skip, Ended;
    }

    private sealed class W179Remote
    {
        public byte Src; public int Seq; public char Kind; public string Name = ""; public long AtMs;
        public float X, Y, Z; public bool HaveDest, OwnCopy, CopyPlaying, SkipKey, Ended, Brought; public string How = "";
    }

    private readonly object _w179Lock = new();
    private W179Scene? _w179Cur, _w179LastEnded;
    private int _w179Seq;
    private readonly ConcurrentDictionary<byte, W179Remote> _w179Remote = new();
    private readonly ConcurrentQueue<(byte Src, int Seq)> _w179Held = new();
    private readonly ConcurrentDictionary<string, long> _w179LocalVideoAt = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _w179TimeApplied = new(StringComparer.Ordinal);
    private int _w179BringBusy;
    private uint _w179NatSeq;
    private volatile bool _w179Faded;
    private long _w179FadeOnAt;
    private string _w179FadeScene = "-";
    private float _w179X, _w179Y, _w179Z;
    private volatile bool _w179HavePos;
    private long _w179DudeStartMs, _w179TwinSeenMs, _w179TwinReleasedFor;
    private long _w179Fades, _w179FadesLong, _w179Relocations, _w179Fallbacks, _w179Skips, _w179Videos, _w179VideoCopies, _w179Brings,
                 _w179BringsPlaced, _w179Queued, _w179TwinReleases, _w179PosLifts, _w179BusIn, _w179BusOut, _w179Unscened;
    private readonly ConcurrentDictionary<string, bool> _w179PosLifted = new(StringComparer.Ordinal);

    private string W179Role => _isDamageAuthority ? "host" : "joiner";
    /// <summary>The main menu or a load (an agent started mid-game knows no load yet: Unknown counts as the world).</summary>
    private bool W179NotInWorld => _where is GameWhere.Menu or GameWhere.Loading;
    private bool _w179Primed;

    // ================================================================ switches

    private void Wo179OnCfg(string? arg)
    {
        foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            bool on = kv.EndsWith("=on", StringComparison.Ordinal);
            if (kv.StartsWith("bus=", StringComparison.Ordinal)) _w179Bus = on;
            else if (kv.StartsWith("video=", StringComparison.Ordinal)) _w179Video = on;
            else if (kv.StartsWith("bring=", StringComparison.Ordinal)) _w179Bring = on;
            else if (kv.StartsWith("ignored=", StringComparison.Ordinal)) _w179Ignored = on;
            else if (kv.StartsWith("fight_clear=", StringComparison.Ordinal)) _w179FightClear = on;
        }
        Console.WriteLine($"WO179-CFG scene_guard={On(_w151SceneGuard)} bus={On(_w179Bus)} video_shared={On(_w179Video)} bring_along={On(_w179Bring)} partner_ignored={On(_w179Ignored)} fight_clear={On(_w179FightClear)}");
    }

    private void Wo179OnEvent(string name, string? arg)
    {
        if (name == "w179_cfg") { Wo179OnCfg(arg); return; }
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length == 0) return;
        switch (f[0])
        {
            case "skip-key":
                Wo179OnSkipKey();
                break;
            case "video-end" when f.Length >= 3:
                Console.WriteLine($"WO179-VIDEO the partner's copy ended seq={f[1]} why={f[2]}");
                foreach (var r in _w179Remote.Values) if (r.Seq.ToString(CultureInfo.InvariantCulture) == f[1]) r.CopyPlaying = false;
                if (f[2] == "skipped") Wo179OnSkipKey();
                break;
            case "clock" when f.Length >= 3 && int.TryParse(f[1], out int cseq) && double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double d):
                Wo179OnClock(cseq, d);
                break;
            case "ignore-end":
                Console.WriteLine($"WO179-IGNORE ended {string.Join(' ', f.Skip(1))}");
                break;
        }
    }

    // ================================================================ connect, poll, reset

    private void Wo179OnConnect(CancellationToken ct)
    {
        _ = ExecLuaAsync("if KCD2MP_W179Cfg then KCD2MP_W179Cfg() end");
        _ = Wo179PollLoopAsync(ct);
    }

    private async Task Wo179PollLoopAsync(CancellationToken ct)
    {
        long lastTick = 0, lastStats = Environment.TickCount64;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(100, ct); } catch { break; }
            try
            {
                await Wo179PollAsync();
                long now = Environment.TickCount64;
                if (now - lastTick >= 500) { lastTick = now; await Wo179GuardTickAsync(now); }
                if (now - lastStats >= 60_000)
                {
                    lastStats = now;
                    if (_w179Fades + _w179Relocations + _w179Videos + _w179BusIn + _w179BusOut > 0) Console.WriteLine(Wo179StatsLine());
                }
            }
            catch (Exception ex) { Console.WriteLine($"WO179 tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
        Wo179Reset("disconnect");
    }

    private string Wo179StatsLine() =>
        $"WO179-STATS fades={_w179Fades} fades_over_1_5s_past={_w179FadesLong} relocations={_w179Relocations} fallbacks={_w179Fallbacks} skips={_w179Skips} videos={_w179Videos} video_copies={_w179VideoCopies} brings={_w179Brings} placed={_w179BringsPlaced} queued={_w179Queued} twin_releases={_w179TwinReleases} positioning_lifts={_w179PosLifts} bus_in={_w179BusIn} bus_out={_w179BusOut} unscened_jumps={_w179Unscened}";

    private async Task Wo179PollAsync()
    {
        var args = new byte[4];
        BitConverter.TryWriteBytes(args, _w179NatSeq);
        var r = await _combat.Wo163Async(Protocol.Wo179OpPoll, args);
        if (r is not { Ok: true } x || x.Payload.Length < 19) return;
        var p = x.Payload;
        uint last = BitConverter.ToUInt32(p, 0);
        _w179Faded = p[4] != 0;
        if (p[5] != 0) { _w179X = BitConverter.ToSingle(p, 6); _w179Y = BitConverter.ToSingle(p, 10); _w179Z = BitConverter.ToSingle(p, 14); _w179HavePos = true; }
        else _w179HavePos = false;
        int n = p[18], at = 19;
        if (last < _w179NatSeq) _w179NatSeq = 0;   // a new DLL: its sequence started again
        if (!_w179Primed) { _w179Primed = true; _w179NatSeq = last; return; }   // the ring's history is an earlier agent's
        for (int i = 0; i < n && at + 33 <= p.Length; i++, at += 33)
        {
            uint seq = BitConverter.ToUInt32(p, at);
            byte kind = p[at + 4];
            uint age = BitConverter.ToUInt32(p, at + 5);
            float fx = BitConverter.ToSingle(p, at + 9), fy = BitConverter.ToSingle(p, at + 13), fz = BitConverter.ToSingle(p, at + 17);
            float tx = BitConverter.ToSingle(p, at + 21), ty = BitConverter.ToSingle(p, at + 25), tz = BitConverter.ToSingle(p, at + 29);
            if (seq <= _w179NatSeq) continue;
            _w179NatSeq = seq;
            long when = Environment.TickCount64 - age;
            switch (kind)
            {
                case 1: Wo179OnFade(true, when); break;
                case 2: Wo179OnFade(false, when); break;
                case 3: await Wo179OnJumpAsync(fx, fy, fz, tx, ty, tz, when); break;
                case 4: break;   // this DLL's own teleport (a pull, a bring-along): the agent made it
            }
        }
        if (last > _w179NatSeq) _w179NatSeq = last;
    }

    /// <summary>A load or the session's end: no scene survives it.</summary>
    private void Wo179Reset(string why)
    {
        W179Scene? cur;
        lock (_w179Lock) { cur = _w179Cur; _w179Cur = null; _w179LastEnded = null; }
        if (cur is { Sent: true } && _w179Bus) _ = Wo179SendAsync(Wo179Rules.End(cur.Seq, "load"));
        if (cur is not null && why == "load")
        {
            // a scene that ends in a load: a move to the other map (a level change) or a story load -- not shared (out of scope)
            Interlocked.Increment(ref _w179Skips);
            Console.WriteLine($"WO179-SKIP scene={cur.Type}:{cur.Name} reason=level-change-or-load -- the partner is not moved; a join catches him up as today");
        }
        else if (cur is not null) Console.WriteLine($"WO179-SCENE reset ({why}): '{cur.Name}' forgotten");
        _w179Remote.Clear();
        while (_w179Held.TryDequeue(out _)) { }
        _w179PosLifted.Clear();
        _w179DudeStartMs = 0;
    }

    // ================================================================ Part A: the fade log, the bounds

    private string Wo179SceneLabel()
    {
        var c = _w179Cur;
        if (c is not null) return $"{c.Type}:{c.Name}";
        if (!_w151Scenes.IsEmpty) { var s = _w151Scenes.Values.First(); return $"{s.Type}:{s.Name}"; }
        if (_w179DudeStartMs > 0) return "Dialogue:with-this-player";
        if (Volatile.Read(ref _w179BringBusy) != 0) return "BringAlong:partner-fade";
        return "-";
    }

    private void Wo179OnFade(bool on, long whenMs)
    {
        if (on)
        {
            _w179FadeOnAt = whenMs;
            _w179FadeScene = Wo179SceneLabel();
            Interlocked.Increment(ref _w179Fades);
            Console.WriteLine($"WO179-FADE on scene={_w179FadeScene} role={W179Role}");
            return;
        }
        long held = _w179FadeOnAt > 0 ? whenMs - _w179FadeOnAt : -1;
        // the scene's own authored part ends with its content; what is past it is ours to answer for
        long contentEnd = 0;
        foreach (var s in _w151Scenes.Values) if (s.EndAtMs > contentEnd) contentEnd = s.EndAtMs;
        var last = _w179LastEnded;
        if (last is not null && last.EndMs > contentEnd) contentEnd = last.EndMs;
        long past = contentEnd > _w179FadeOnAt && contentEnd > 0 ? whenMs - contentEnd : -1;
        if (past > 1500) Interlocked.Increment(ref _w179FadesLong);
        Console.WriteLine(FormattableString.Invariant(
            $"WO179-FADE off scene={_w179FadeScene} held_ms={held} past_content_end_ms={past} role={W179Role}{(past > 1500 ? " OVER-LIMIT" : "")}"));
        _w179FadeOnAt = 0;
    }

    /// <summary>The WO-151 stage feed, for every scene type (called from Wo151OnSceneStage).</summary>
    private void Wo179OnSceneStage(string stage, string type, string name)
    {
        long now = Environment.TickCount64;
        switch (stage)
        {
            case "PlayCutscene":
                Wo179LocalStart(type, name, now);
                break;
            case "ReleaseScene":
            case "Interrupt":
                Wo179LocalEnd(name, stage == "Interrupt" ? "interrupt" : null, now);
                break;
        }
    }

    /// <summary>WO-167's conversation clock: a conversation with this player started / ended.</summary>
    private void Wo179OnDudeDialogue(bool ended)
    {
        long now = Environment.TickCount64;
        if (!ended)
        {
            _w179DudeStartMs = now;
            _w179TwinSeenMs = 0;
            lock (_w179Lock) if (_w179Cur is null) _w179Cur = Wo179NewScene("Dialogue", "conversation", now);
        }
        else
        {
            _w179DudeStartMs = 0;
            Wo179LocalEnd("conversation", null, now);
        }
    }

    /// <summary>The engine lines (Wo144OnEngineLine): a dialogue twin made = the conversation started properly (A3).</summary>
    private void Wo179OnEngineLine(string line)
    {
        if (_w179DudeStartMs > 0 && line.Contains("DialogTwin_", StringComparison.Ordinal)) _w179TwinSeenMs = Environment.TickCount64;
    }

    /// <summary>Part A, 2 Hz: the bounded end-positioning wait (A1) and the conversation that makes no twins (A3), joiner only.</summary>
    private async Task Wo179GuardTickAsync(long now)
    {
        if (!_w151SceneGuard || !W137Joiner) return;
        // A1: a scene's NPCs that never reach their end spots on this machine (they are the host's copies; the stream places them)
        foreach (var s in _w151Scenes.Values.ToArray())
        {
            if (s.EndAtMs == 0 || s.Positioned || _w179PosLifted.ContainsKey(s.Name)) continue;
            long hostEnd = 0;
            foreach (var r in _w179Remote.Values) if (r.Ended && r.Name == s.Name) hostEnd = Math.Max(hostEnd, r.AtMs);
            if (now < Wo179Rules.PositioningDeadline(s.EndAtMs, hostEnd)) continue;
            _w179PosLifted[s.Name] = true;
            Interlocked.Increment(ref _w179PosLifts);
            Console.WriteLine(FormattableString.Invariant(
                $"WO179-RELEASE scene {s.Type} '{s.Name}' end positioning not done {(now - s.EndAtMs) / 1000.0:F1} s after its content -- bounded at 2 s: the copies stay where the host's stream puts them, the black is lifted, the player stood up"));
            if (!s.Rescued) { s.Rescued = true; _ = Wo151SceneRescueSaveAsync(s.Name); }
            Volatile.Write(ref _w167FaderHeldMs, W167NowMs());
            await ExecLuaAsync("if KCD2MP_W167SceneReleased then KCD2MP_W167SceneReleased(false, \"wo179-positioning-2s\") end");
        }
        // A3: a conversation with this player that made no twin (the field: WAITING_FOR_TWINS for 20 s on a host copy, 6 of 6)
        long ds = _w179DudeStartMs;
        if (ds > 0 && _w179TwinSeenMs == 0 && now - ds >= Wo179Rules.TwinsBoundMs && _w179TwinReleasedFor != ds)
        {
            _w179TwinReleasedFor = ds;
            Interlocked.Increment(ref _w179TwinReleases);
            Console.WriteLine(FormattableString.Invariant(
                $"WO179-RELEASE conversation made no dialogue twin in {(now - ds) / 1000.0:F1} s (WAITING_FOR_TWINS on a host copy) -- ended the game's own way (InterruptDialogs) instead of a 20 s black"));
            await ExecLuaAsync("if KCD2MP_W179ReleaseDialog then KCD2MP_W179ReleaseDialog(\"no-twins\") end");
        }
    }

    // ================================================================ the local scene: start, jump, end

    private W179Scene Wo179NewScene(string type, string name, long now)
    {
        var s = new W179Scene { Type = type, Name = name, StartMs = now, Kind = Wo179Rules.KindOf(type), Video = type == "Rendered" };
        if (_w179HavePos) { s.SX = _w179X; s.SY = _w179Y; s.SZ = _w179Z; }
        else { s.SX = _lastX; s.SY = _lastY; s.SZ = _lastZ; }
        return s;
    }

    private void Wo179LocalStart(string type, string name, long now)
    {
        W179Scene s;
        lock (_w179Lock)
        {
            // a conversation that leads into a cutscene: the cutscene is the scene now (its start position is the conversation's)
            var prev = _w179Cur;
            s = Wo179NewScene(type, name, now);
            if (prev is not null && !prev.Ended && prev.Type == "Dialogue") { s.SX = prev.SX; s.SY = prev.SY; s.SZ = prev.SZ; s.StartMs = prev.StartMs; }
            _w179Cur = s;
        }
        if (s.Video) _w179LocalVideoAt[name] = now;
        if (Wo179Rules.IsCrimeScene(name))
        {
            Console.WriteLine($"WO179-SKIP scene={type}:{name} reason=crime-punishment -- an arrest, the pillory or jail is not shared (out of scope)");
            Interlocked.Increment(ref _w179Skips);
            return;
        }
        if (W137Joiner) _ = ExecLuaAsync($"if KCD2MP_W179ClockWatch then KCD2MP_W179ClockWatch(true, {s.Seq}) end");
        if (s.Video && _w179Bus && LivePartners().Count > 0)
        {
            s.Seq = Interlocked.Increment(ref _w179Seq);
            s.Sent = true;
            Interlocked.Increment(ref _w179Videos);
            Console.WriteLine(FormattableString.Invariant($"WO179-SCENE start video '{name}' seq={s.Seq} at ({s.SX:F1}, {s.SY:F1}, {s.SZ:F1}) -- the partner watches it too and is brought beside this Henry behind it"));
            _ = Wo179SendAsync(Wo179Rules.Start(s.Seq, 'V', name));
            _ = Wo179SendAsync(Wo179Rules.Dest(s.Seq, s.SX, s.SY, s.SZ));
        }
    }

    private void Wo179LocalEnd(string name, string? how, long now)
    {
        W179Scene? s;
        lock (_w179Lock)
        {
            s = _w179Cur;
            if (s is null || (s.Name != name && name != "conversation")) return;
            if (name == "conversation" && s.Type != "Dialogue") return;   // the cutscene it led into ends on its own release
            s.Ended = true; s.EndMs = now;
            _w179Cur = null; _w179LastEnded = s;
        }
        if (W137Joiner) _ = ExecLuaAsync("if KCD2MP_W179ClockWatch then KCD2MP_W179ClockWatch(false) end");
        if (Wo179Rules.IsCrimeScene(s.Name) || !_w179Bus) return;
        float ex = _w179HavePos ? _w179X : _lastX, ey = _w179HavePos ? _w179Y : _lastY, ez = _w179HavePos ? _w179Z : _lastZ;
        // C2 fallback: a scene that moved Henry without the jump being caught at the source
        if (!s.Relocated && !s.Video && LivePartners().Count > 0 && Wo179Rules.Dist2D(s.SX, s.SY, ex, ey) > Wo179Rules.FallbackMovedM)
        {
            Interlocked.Increment(ref _w179Fallbacks);
            Console.WriteLine(FormattableString.Invariant(
                $"WO179-FALLBACK scene={s.Type}:{s.Name} moved {Wo179Rules.Dist2D(s.SX, s.SY, ex, ey):F0} m and the move was not caught at the source -- the partner is pulled now"));
            Wo179SendRelocation(s, ex, ey, ez, "fallback");
        }
        if (s.Sent)
        {
            string h = how ?? (s.SkipKey ? "skip" : "end");
            Console.WriteLine(FormattableString.Invariant($"WO179-SCENE end '{s.Name}' seq={s.Seq} how={h} after={(now - s.StartMs) / 1000.0:F1} s"));
            _ = Wo179SendAsync(Wo179Rules.End(s.Seq, h));
        }
    }

    private void Wo179SendRelocation(W179Scene s, float x, float y, float z, string how)
    {
        if (!s.Sent)
        {
            s.Seq = Interlocked.Increment(ref _w179Seq);
            s.Sent = true;
            _ = Wo179SendAsync(Wo179Rules.Start(s.Seq, s.Video ? 'V' : 'R', s.Name.Length > 0 ? s.Name : "scene"));
        }
        s.Relocated = true;
        Interlocked.Increment(ref _w179Relocations);
        Console.WriteLine(FormattableString.Invariant($"WO179-RELOCATE scene={s.Type}:{s.Name} seq={s.Seq} to=({x:F1}, {y:F1}, {z:F1}) source={how} -- the partner is brought along"));
        _ = Wo179SendAsync(Wo179Rules.Dest(s.Seq, x, y, z));
    }

    /// <summary>C2 at the source: the game moved this player's Henry (the DLL's 20 Hz watch; our own teleports are kind 4).</summary>
    private async Task Wo179OnJumpAsync(float fx, float fy, float fz, float tx, float ty, float tz, long whenMs)
    {
        if (W179NotInWorld || Wo136Holding || _jj is not null) return;   // a load or a join moves him: caught up as today
        if (Environment.TickCount64 - _w179OwnMoveMs < 4000) return;               // a bring-along of ours
        W179Scene? s;
        long sinceEnd;
        lock (_w179Lock)
        {
            s = _w179Cur;
            var last = _w179LastEnded;
            sinceEnd = last is null ? -1 : whenMs - last.EndMs;
            if (s is null && last is not null && sinceEnd >= 0 && sinceEnd < Wo179Rules.JumpAfterSceneMs) s = last;
        }
        float d = Wo179Rules.Dist2D(fx, fy, tx, ty);
        if (!Wo179Rules.JumpInScene(_w179Cur is not null, sinceEnd, _w179Faded) || s is null)
        {
            long n = Interlocked.Increment(ref _w179Unscened);
            if (n <= 20) Console.WriteLine(FormattableString.Invariant($"WO179-JUMP {d:F0} m with no scene running (a console, mod or fast-travel move) -- not shared"));
            return;
        }
        if (Wo179Rules.IsCrimeScene(s.Name))
        {
            Interlocked.Increment(ref _w179Skips);
            Console.WriteLine($"WO179-SKIP scene={s.Type}:{s.Name} reason=crime-punishment jump={d:F0} m -- not shared (out of scope)");
            return;
        }
        if (!_w179Bus || LivePartners().Count == 0) { Console.WriteLine($"WO179-RELOCATE scene={s.Type}:{s.Name} jump={d:F0} m -- not sent ({(!_w179Bus ? "mp_scene_bus off" : "no partner")})"); return; }
        Wo179SendRelocation(s, tx, ty, tz, "jump");
        await Task.CompletedTask;
    }

    private long _w179OwnMoveMs;

    private void Wo179OnSkipKey()
    {
        var s = _w179Cur;
        if (s is { Video: true, Sent: true } && !s.SkipKey)
        {
            s.SkipKey = true;
            Console.WriteLine($"WO179-VIDEO skip pressed here seq={s.Seq} -- the partner is told");
            _ = Wo179SendAsync(Wo179Rules.SkipKey(s.Seq));
            return;
        }
        foreach (var r in _w179Remote.Values)
            if (r.Kind == 'V' && !r.Ended && !r.SkipKey)
            {
                r.SkipKey = true;
                Console.WriteLine($"WO179-VIDEO skip pressed here on the partner's video seq={r.Seq} -- told");
                _ = Wo179SendAsync(Wo179Rules.SkipKey(r.Seq), r.Src);
            }
    }

    private void Wo179OnClock(int seq, double delta)
    {
        var s = _w179LastEnded;
        if (s is null || !s.Relocated || delta < 60) return;
        Console.WriteLine(FormattableString.Invariant($"WO179-TIME this joiner's relocation scene '{s.Name}' skipped {delta:F0} game-s -- the host's world applies it once"));
        _ = Wo179SendAsync(Wo179Rules.Clock(s.Seq, delta));
    }

    // ================================================================ the bus

    private async Task Wo179SendAsync(string text, byte? to = null)
    {
        if (text.Length > Protocol.W164TextMax) { Console.WriteLine($"WO179-BUS not sent (too long): {text}"); return; }
        Interlocked.Increment(ref _w179BusOut);
        Console.WriteLine($"WO179-BUS out '{text}'");
        if (to is byte t) { await W164SendAsync(t, Protocol.W164SceneBus, text); return; }
        if (W137Host) foreach (byte j in Wo134Peers()) await W164SendAsync(j, Protocol.W164SceneBus, text);
        else await W164SendAsync(Protocol.JoinTargetHost, Protocol.W164SceneBus, text);
    }

    private async Task Wo179OnBusAsync(byte src, string text)
    {
        if (!Wo179Rules.TryParse(text, out var m)) { Interlocked.Increment(ref _w164Malformed); return; }
        Interlocked.Increment(ref _w179BusIn);
        Console.WriteLine($"WO179-BUS in from player {src}: '{text}'");
        if (!_w179Bus) return;
        long now = Environment.TickCount64;
        switch (m.Op)
        {
            case 'S':
            {
                var r = new W179Remote { Src = src, Seq = m.Seq, Kind = m.Kind, Name = m.Name, AtMs = now };
                _w179Remote[src] = r;
                if (m.Kind == 'V') _ = Wo179RemoteVideoAsync(r);
                break;
            }
            case 'R':
            {
                if (!_w179Remote.TryGetValue(src, out var r) || r.Seq != m.Seq) { r = new W179Remote { Src = src, Seq = m.Seq, Kind = 'R', Name = "scene", AtMs = now }; _w179Remote[src] = r; }
                r.X = m.X; r.Y = m.Y; r.Z = m.Z; r.HaveDest = true;
                _ = Wo179BringAlongAsync(r, "destination");
                break;
            }
            case 'E':
                if (_w179Remote.TryGetValue(src, out var re) && re.Seq == m.Seq)
                {
                    re.Ended = true; re.How = m.How; re.AtMs = now;
                    // C1: a video that ended there on its own (or both skipped) ends here on the same beat
                    if (re.Kind == 'V' && re.CopyPlaying && (m.How == "end" || re.SkipKey))
                    {
                        Console.WriteLine($"WO179-VIDEO the triggerer's video ended ({m.How}) -- this copy ends on the same beat");
                        await ExecLuaAsync("if KCD2MP_W179StopVideo then KCD2MP_W179StopVideo(\"partner-ended\") end");
                    }
                    else if (re.Kind == 'V' && re.CopyPlaying)
                        Console.WriteLine("WO179-VIDEO the triggerer skipped alone -- this copy plays on until this player skips or it ends");
                    Wo179RunHeld();
                }
                break;
            case 'K':
            {
                var own = _w179Cur;
                bool mine = own is { Video: true, Sent: true } && own.Seq == m.Seq;
                bool both = mine ? own!.SkipKey : _w179Remote.TryGetValue(src, out var rk) && rk.Seq == m.Seq && rk.SkipKey;
                if (_w179Remote.TryGetValue(src, out var rk2) && rk2.Seq == m.Seq) rk2.SkipKey = true;
                Console.WriteLine($"WO179-VIDEO the partner pressed skip on seq={m.Seq}{(both ? " -- both pressed" : " -- waiting for this player")}");
                if (both && _w179Remote.TryGetValue(src, out var rk3) && rk3.CopyPlaying)
                    await ExecLuaAsync("if KCD2MP_W179StopVideo then KCD2MP_W179StopVideo(\"both-skipped\") end");
                break;
            }
            case 'L':
                if (W137Host && _w179Ignored)
                {
                    Console.WriteLine(FormattableString.Invariant($"WO179-IGNORE player {src}'s figure landed at ({m.X:F1}, {m.Y:F1}, {m.Z:F1}) -- this world's people leave it alone for the window"));
                    await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W179Ignore then KCD2MP_W179Ignore(\"{src}\", {m.X:F2}, {m.Y:F2}, {m.Z:F2}, {Wo179Rules.IgnoreWindowS}) end"));
                }
                break;
            case 'T':
                if (W137Host)
                {
                    string key = $"{src}:{m.Seq}";
                    if (!_w179TimeApplied.TryAdd(key, true)) { Console.WriteLine($"WO179-TIME seq={m.Seq} from player {src} already applied -- once only"); break; }
                    Console.WriteLine(FormattableString.Invariant($"WO179-TIME player {src}'s relocation scene skipped {m.Value:F0} game-s -- the host's world applies it once (the bus-confirmed exception to the joiner time-skip drop)"));
                    await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W179ApplySkip then KCD2MP_W179ApplySkip({m.Value:F0}, \"player {src}\") end"));
                }
                break;
        }
    }

    // ================================================================ C1: the partner's video

    private async Task Wo179RemoteVideoAsync(W179Remote r)
    {
        if (!_w179Video) { Console.WriteLine($"WO179-VIDEO '{r.Name}' not played here: mp_scene_video_shared off"); return; }
        if (W179NotInWorld) { Console.WriteLine($"WO179-VIDEO '{r.Name}' not played here: not in the world (loading or joining)"); return; }
        long until = Environment.TickCount64 + Wo179Rules.VideoOwnCopyWaitMs;
        while (Environment.TickCount64 < until)
        {
            if (_w179LocalVideoAt.TryGetValue(r.Name, out long at) && Math.Abs(at - r.AtMs) < 10_000) break;
            await Task.Delay(50);
        }
        if (_w179LocalVideoAt.TryGetValue(r.Name, out long own) && Math.Abs(own - r.AtMs) < 10_000)
        {
            r.OwnCopy = true;
            Console.WriteLine(FormattableString.Invariant($"WO179-VIDEO '{r.Name}' seq={r.Seq}: this game's own quest copy plays it ({(own - r.AtMs) / 1000.0:+0.0;-0.0} s from the triggerer's)"));
            return;
        }
        if (!Wo179Rules.TryVideo(r.Name, out string path, out double secs))
        {
            Console.WriteLine($"WO179-VIDEO '{r.Name}' seq={r.Seq}: not in the game's video table -- not played here");
            return;
        }
        r.CopyPlaying = true;
        Interlocked.Increment(ref _w179VideoCopies);
        Console.WriteLine(FormattableString.Invariant($"WO179-VIDEO '{r.Name}' seq={r.Seq}: no own copy within 1 s -- played here ({path}, {secs:F1} s), {(Environment.TickCount64 - r.AtMs) / 1000.0:F2} s after the triggerer's start"));
        await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W179PlayVideo then KCD2MP_W179PlayVideo({r.Seq}, \"{path}\", {secs:F1}) end"));
    }

    // ================================================================ C3 + C4: bring the partner along

    private void Wo179RunHeld()
    {
        if (_w179Cur is not null) return;
        while (_w179Held.TryDequeue(out var h))
        {
            if (_w179Remote.TryGetValue(h.Src, out var r) && r.Seq == h.Seq && r.HaveDest && !r.Brought)
            {
                Console.WriteLine($"WO179-QUEUE the held scene seq={h.Seq} from player {h.Src} is offered again now");
                _ = Wo179BringAlongAsync(r, "held");
            }
        }
    }

    private async Task Wo179BringAlongAsync(W179Remote r, string why)
    {
        if (!_w179Bring) { Console.WriteLine($"WO179-BRING seq={r.Seq} not done: mp_scene_bring_along off"); return; }
        if (W179NotInWorld || Wo136Holding || _jj is not null)
        {
            Console.WriteLine($"WO179-BRING seq={r.Seq} not moved: this player is loading or joining (where={_where} holding={On(Wo136Holding)} join={On(_jj is not null)}) -- caught up on the next join as today");
            return;
        }
        var own = _w179Cur;
        if (own is not null && own.Type != "Dialogue")
        {
            // two shared scenes at once: this machine's own scene plays first; the partner's is held and offered again after it
            Interlocked.Increment(ref _w179Queued);
            _w179Held.Enqueue((r.Src, r.Seq));
            Console.WriteLine($"WO179-QUEUE seq={r.Seq} from player {r.Src} held -- this player's own scene '{own.Name}' runs first");
            return;
        }
        if (Interlocked.CompareExchange(ref _w179BringBusy, 1, 0) != 0)
        {
            // a bring-along is running: it reads the latest destination when it places (r is shared)
            Console.WriteLine($"WO179-BRING seq={r.Seq} destination updated while a bring-along runs");
            return;
        }
        try
        {
            Interlocked.Increment(ref _w179Brings);
            float d0 = Wo179Rules.Dist2D(_lastX, _lastY, r.X, r.Y);
            Console.WriteLine(FormattableString.Invariant($"WO179-BRING seq={r.Seq} ({why}) from player {r.Src}'s {(r.Kind == 'V' ? "video" : "scene")} '{r.Name}' -- {d0:F0} m away"));
            // dead, downed or respawning: wait until on his feet (the scene does not wait)
            long waitUntil = Environment.TickCount64 + Wo179Rules.DownWaitMaxMs;
            while (_localDowned && Environment.TickCount64 < waitUntil) await Task.Delay(1000);
            if (_localDowned) { Console.WriteLine($"WO179-BRING seq={r.Seq} not moved: still down after 180 s"); return; }
            if (d0 < 8f) { r.Brought = true; Console.WriteLine($"WO179-BRING seq={r.Seq} already beside ({d0:F1} m): nothing to do"); return; }
            bool video = r.Kind == 'V';
            if (video)
            {
                // C1: the move happens behind the video -- wait (at most 2 s) until this game's copy covers the screen
                long coverUntil = Environment.TickCount64 + 2000;
                while (!r.CopyPlaying && !r.OwnCopy && Environment.TickCount64 < coverUntil) await Task.Delay(50);
                if (r.CopyPlaying) await Task.Delay(300);   // the movie player's first frame
            }
            bool faded = false;
            if (!video)
            {
                var fr = await _combat.Wo163Async(Protocol.Wo179OpFade, [1, .. BitConverter.GetBytes(Wo179Rules.OwnFadeS)]);
                faded = fr is { Ok: true } f && f.Payload.Length > 0 && f.Payload[0] == 1;
                await Task.Delay((int)(Wo179Rules.OwnFadeS * 1000) + 50);
            }
            // C3: the quiet exit ladder
            string ladder = await AskModAsync("KCD2MP_W179Ladder", 3000);
            Console.WriteLine($"WO179-LADDER seq={r.Seq}: {ladder}");
            if (ladder.Contains("riding=1", StringComparison.Ordinal))
            {
                for (int i = 0; i < 6; i++)
                {
                    await Task.Delay(500);
                    string dm = await AskModAsync("KCD2MP_Wo114Dismount", 2000);
                    if (dm.Contains("mounted=no", StringComparison.Ordinal)) break;
                }
            }
            // C4: the landing -- a free ground spot 3, 6 or 10 m from the relocated Henry, on his floor (the DLL's navmesh snap within 3 m height)
            CombatPipe.JoinPlaceReport? pr = null;
            float ring = 0;
            foreach (float rm in Wo179Rules.LandingRingsM)
            {
                _w179OwnMoveMs = Environment.TickCount64;
                pr = await _combat.JoinPlaceAsync(r.X, r.Y, r.Z, rm);
                ring = rm;
                if (pr is { Ok: true }) break;
            }
            if (faded) { await Task.Delay(200); await _combat.Wo163Async(Protocol.Wo179OpFade, [0, .. BitConverter.GetBytes(Wo179Rules.OwnFadeS)]); }
            if (pr is not { Ok: true })
            {
                Console.WriteLine($"WO179-BRING seq={r.Seq} NOT placed (no free ground within 10 m{(pr is null ? "; no answer from the plugin" : "")})");
                return;
            }
            r.Brought = true;
            Interlocked.Increment(ref _w179BringsPlaced);
            float ax = pr.After[0], ay = pr.After[1], az = pr.After[2];
            Console.WriteLine(FormattableString.Invariant(
                $"WO179-BRING seq={r.Seq} placed {Wo179Rules.Dist2D(ax, ay, r.X, r.Y):F1} m from the relocated Henry (ring {ring:F0} m) at ({ax:F1}, {ay:F1}, {az:F1}) behind {(video ? "the video" : "the fade")}"));
            // C4: the ignore window runs in the host's world (the scene's people are the host's)
            if (_w179Ignored)
            {
                if (_isDamageAuthority) await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W179Ignore then KCD2MP_W179Ignore(\"player\", {ax:F2}, {ay:F2}, {az:F2}, {Wo179Rules.IgnoreWindowS}) end"));
                else await Wo179SendAsync(Wo179Rules.Landed(r.Seq, ax, ay, az));
            }
        }
        catch (Exception ex) { Console.WriteLine($"WO179-BRING seq={r.Seq} failed: {ex.GetType().Name}: {ex.Message}"); }
        finally { Volatile.Write(ref _w179BringBusy, 0); }
    }
}
