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
//   bus       W164 kind 15 (Wo179Rules): S start, R destination, E end, K skip key, L landed, T clock jump, W watching (WO-179B)
//   partner   C1 the same video (its own quest copy, else the movie player), C3 the ladder, C4 the landing and the ignore window
//
// WO-179B: a video's partner is protected at its START (the ladder at once, the ignore window at his own spot, the host's world doing it
// for a joiner's figure: W), stays where he is while he watches, and is moved AFTER it: the triggerer reports where its Henry settles
// (after its own post-video fade, else 2 s after the end; a relocation inside that window is part of it -- one move per scene) and the
// partner, black from the video's end (at most 3 s, at most 1 s past the report), lands beside it. Only the skip KEY is a skip on the
// wire (a second press within 4 s, the game's own rule); the triggerer's skip ends the partner's copy on the same beat through its end
// message, whatever the end (mp_scene_skip_both); the partner's skip ends his own copy (the game's own
// skip of the triggerer's story video from outside is not proven). The copy's skip check is held around the ladder and any mod move.
//
// Log tags: WO179-FADE, WO179-SCENE, WO179-RELOCATE, WO179-FALLBACK, WO179-SKIP, WO179-BUS, WO179-VIDEO, WO179-BRING, WO179-QUEUE,
//           WO179-RELEASE, WO179-TIME, WO179-STATS, WO179-WATCH, WO179-SETTLE, WO179-LAND.
public partial class GameBridge
{
    private volatile bool _w179Bus = Wo179Rules.DefaultBus, _w179Video = Wo179Rules.DefaultVideo, _w179Bring = Wo179Rules.DefaultBring,
                          _w179Ignored = Wo179Rules.DefaultIgnored, _w179FightClear = Wo179Rules.DefaultFightClear,
                          _w179SkipBoth = Wo179Rules.DefaultSkipBoth, _w179WatchSafe = Wo179Rules.DefaultWatchSafe;

    private sealed class W179Scene
    {
        public int Seq; public string Name = "", Type = ""; public char Kind;
        public long StartMs, EndMs; public float SX, SY, SZ;
        public bool Sent, Relocated, Video, SkipKey, Skip, Ended, Settled, PartnerSkipKey;
    }

    private sealed class W179Remote
    {
        public byte Src; public int Seq; public char Kind; public string Name = ""; public long AtMs;
        public float X, Y, Z; public bool HaveDest, OwnCopy, CopyPlaying, SkipKey, Ended, Brought; public string How = "";
        // WO-179B: SkipKey = this player pressed skip on it; TriggererSkipKey = the triggerer did. Landing: 0 not yet, 1 black, 2 done.
        public bool TriggererSkipKey, LateDest; public long DestAtMs, FadeAtMs; public int Landing;
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
                 _w179BringsPlaced, _w179Queued, _w179TwinReleases, _w179PosLifts, _w179BusIn, _w179BusOut, _w179Unscened,
                 _w179Watches, _w179Settles, _w179Lands, _w179LateDests, _w179Holds, _w179LastFadeOnMs;
    private int _w179HoldCount;
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
            else if (kv.StartsWith("skip_both=", StringComparison.Ordinal)) _w179SkipBoth = on;
            else if (kv.StartsWith("watch_safe=", StringComparison.Ordinal)) _w179WatchSafe = on;
        }
        Console.WriteLine($"WO179-CFG scene_guard={On(_w151SceneGuard)} bus={On(_w179Bus)} video_shared={On(_w179Video)} bring_along={On(_w179Bring)} partner_ignored={On(_w179Ignored)} fight_clear={On(_w179FightClear)} skip_both={On(_w179SkipBoth)} watch_safe={On(_w179WatchSafe)}");
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
                // WO-179B item 4: movement inside the copy ends it HERE only -- never sent as a skip (only the skip key is)
                Console.WriteLine($"WO179-VIDEO the partner's copy ended seq={f[1]} why={f[2]}{(f[2] == "skipped" ? " -- this player moved: his copy only, not sent as a skip" : "")}");
                foreach (var r in _w179Remote.Values)
                {
                    if (r.Seq.ToString(CultureInfo.InvariantCulture) != f[1]) continue;
                    lock (_w179Lock) r.CopyPlaying = false;
                    // the triggerer's video already ended (the copy played on past it): the black and the move follow now
                    if (r.Ended && Volatile.Read(ref r.Landing) == 0 && !r.OwnCopy && _w179Bring) _ = Wo179VideoLandAsync(r, "copy-ended-after-the-triggerer");
                }
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
        $"WO179-STATS fades={_w179Fades} fades_over_1_5s_past={_w179FadesLong} relocations={_w179Relocations} fallbacks={_w179Fallbacks} skips={_w179Skips} videos={_w179Videos} video_copies={_w179VideoCopies} brings={_w179Brings} placed={_w179BringsPlaced} queued={_w179Queued} twin_releases={_w179TwinReleases} positioning_lifts={_w179PosLifts} bus_in={_w179BusIn} bus_out={_w179BusOut} unscened_jumps={_w179Unscened} watches={_w179Watches} settles={_w179Settles} lands={_w179Lands} late_dests={_w179LateDests} holds={_w179Holds}";

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
                case 4:   // this DLL's own teleport (a pull, a bring-along): the agent made it -- WO-179B: never a skip of a copy
                    if (_w179Remote.Values.Any(r => r.CopyPlaying)) _ = Wo179HoldBriefAsync("a mod move of this player (the DLL's own teleport)");
                    break;
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
            Volatile.Write(ref _w179LastFadeOnMs, whenMs);
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
            Console.WriteLine(FormattableString.Invariant($"WO179-SCENE start video '{name}' seq={s.Seq} at ({s.SX:F1}, {s.SY:F1}, {s.SZ:F1}) -- the partner watches it too where he is; after it he is brought to where this Henry settles"));
            _ = Wo179SendAsync(Wo179Rules.Start(s.Seq, 'V', name));
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
            if (s.Video) _ = Wo179SettleAsync(s);   // WO-179B item 3: where Henry settles after it
        }
    }

    /// <summary>
    /// WO-179B item 3 (the triggerer): where this Henry settles after a shared video -- once its own post-video fade has lifted, or 2 s
    /// after the end when no fade came (SettleDue). A relocation inside that window (the kidnapping fading into being tied up) is part of
    /// it: the report carries where he is then. One report per scene.
    /// </summary>
    private async Task Wo179SettleAsync(W179Scene s)
    {
        long end = s.EndMs;
        bool fadeSeen = _w179Faded;
        try
        {
            while (true)
            {
                await Task.Delay(50);
                if (W179NotInWorld) { Console.WriteLine($"WO179-SETTLE seq={s.Seq} abandoned: a load -- the partner is not moved (a join catches him up as today)"); return; }
                long now = Environment.TickCount64;
                bool faded = _w179Faded;
                if (!fadeSeen && (faded || Volatile.Read(ref _w179LastFadeOnMs) >= end - 200)) fadeSeen = true;
                if (Wo179Rules.SettleDue(now - end, fadeSeen, faded)) break;
            }
            if (fadeSeen) await Task.Delay(200);   // a move made on the last black frame lands before the read
            s.Settled = true;
            float x = _w179HavePos ? _w179X : _lastX, y = _w179HavePos ? _w179Y : _lastY, z = _w179HavePos ? _w179Z : _lastZ;
            float moved = Wo179Rules.Dist2D(s.SX, s.SY, x, y);
            Interlocked.Increment(ref _w179Settles);
            Console.WriteLine(FormattableString.Invariant(
                $"WO179-SETTLE seq={s.Seq} '{s.Name}' settled {(Environment.TickCount64 - end) / 1000.0:F2} s after the video's end ({(fadeSeen ? "after its own fade" : "no fade: 2 s")}) at ({x:F1}, {y:F1}, {z:F1}), {moved:F0} m from where it started -- told to the partner"));
            if (moved > 8f) { s.Relocated = true; Interlocked.Increment(ref _w179Relocations); }
            if (_w179Bus && LivePartners().Count > 0) await Wo179SendAsync(Wo179Rules.Dest(s.Seq, x, y, z));
        }
        catch (Exception ex) { Console.WriteLine($"WO179-SETTLE seq={s.Seq} failed: {ex.GetType().Name}: {ex.Message}"); }
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
            if (s is null && last is not null && sinceEnd >= 0 && (sinceEnd < Wo179Rules.JumpAfterSceneMs || (last is { Video: true, Sent: true, Settled: false } && sinceEnd < Wo179Rules.SettleCapMs))) s = last;
        }
        if (s is { Video: true, Sent: true })
        {
            // WO-179B: a video's partner is moved once, after it, to where Henry settles -- a jump under or after the video is in that report
            string dj = Wo179Rules.Dist2D(fx, fy, tx, ty).ToString("F0", CultureInfo.InvariantCulture);
            Console.WriteLine(s.Settled
                ? $"WO179-SETTLE seq={s.Seq} a {dj} m jump after the settle report -- not sent (one move per scene)"
                : $"WO179-SETTLE seq={s.Seq} a {dj} m jump {(s.Ended ? "after" : "under")} the video -- part of its settle report (one move per scene)");
            return;
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

    private long _w179SkipPressMs;

    /// <summary>
    /// The skip KEY (the mod's action hook) -- the only skip that goes on the wire (WO-179B item 1). The game's own skip key arms on the
    /// first press and skips on a second press within 4 s: only that second press is a skip here too (a first press ends nothing).
    /// </summary>
    private void Wo179OnSkipKey()
    {
        long now = Environment.TickCount64;
        long prev = Interlocked.Exchange(ref _w179SkipPressMs, now);
        if (!Wo179Rules.SkipConfirmed(prev, now))
        {
            Console.WriteLine("WO179-VIDEO skip key pressed once -- armed (the game's skip wants a second press within 4 s); nothing ends yet");
            return;
        }
        Interlocked.Exchange(ref _w179SkipPressMs, 0);
        var s = _w179Cur;
        if (s is { Video: true, Sent: true } && !s.SkipKey)
        {
            s.SkipKey = true;
            Console.WriteLine($"WO179-VIDEO skip pressed here seq={s.Seq} -- this game ends its video its own way; the partner's copy ends on the same beat{(s.PartnerSkipKey ? " (both pressed: one end)" : "")}");
            _ = Wo179SendAsync(Wo179Rules.SkipKey(s.Seq));
            return;
        }
        foreach (var r in _w179Remote.Values)
            if (r.Kind == 'V' && !r.Ended && !r.SkipKey)
            {
                r.SkipKey = true;
                Console.WriteLine($"WO179-VIDEO skip pressed here on the partner's video seq={r.Seq} -- told; this copy ends here{(r.TriggererSkipKey ? " (both pressed: one end)" : "")}");
                _ = Wo179SendAsync(Wo179Rules.SkipKey(r.Seq), r.Src);
                if (r.CopyPlaying) _ = Wo179CopyEndAsync(r, "skip-key");
            }
    }

    /// <summary>The copy is this agent's to end (once): false when it already ended.</summary>
    private bool Wo179TakeCopy(W179Remote r)
    {
        lock (_w179Lock)
        {
            if (!r.CopyPlaying) return false;
            r.CopyPlaying = false;
            return true;
        }
    }

    /// <summary>End the partner's copy. An end that comes from the triggerer's side is the start of the black and the move (item 3).</summary>
    private async Task Wo179CopyEndAsync(W179Remote r, string why)
    {
        if (Wo179Rules.CopyEndFades(why) && _w179Bring && !r.OwnCopy) { await Wo179VideoLandAsync(r, why); return; }
        if (!Wo179TakeCopy(r)) return;
        await ExecLuaAsync($"if KCD2MP_W179StopVideo then KCD2MP_W179StopVideo(\"{why}\") end");
    }

    /// <summary>WO-179B item 4: the copy's skip check held (counted: the ladder and a mod move can overlap).</summary>
    private async Task Wo179HoldAsync(bool on)
    {
        int n = on ? Interlocked.Increment(ref _w179HoldCount) : Interlocked.Decrement(ref _w179HoldCount);
        if (!on && n < 0) { Interlocked.Exchange(ref _w179HoldCount, 0); n = 0; }
        if ((on && n == 1) || (!on && n == 0))
            await ExecLuaAsync($"if KCD2MP_W179VideoHold then KCD2MP_W179VideoHold({(on ? "true" : "false")}) end");
    }

    private async Task Wo179HoldBriefAsync(string why)
    {
        Interlocked.Increment(ref _w179Holds);
        Console.WriteLine($"WO179-VIDEO copy skip check held {Wo179Rules.OwnMoveHoldMs} ms: {why}");
        await Wo179HoldAsync(true);
        try { await Task.Delay((int)Wo179Rules.OwnMoveHoldMs); }
        finally { await Wo179HoldAsync(false); }
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
                if (m.Kind == 'V')
                {
                    _ = Wo179RemoteVideoAsync(r);
                    if (_w179WatchSafe && _w179Video && !Wo179Rules.IsCrimeScene(m.Name)) _ = Wo179WatchSafeAsync(r);   // WO-179B item 2: at the START
                }
                break;
            }
            case 'R':
            {
                if (!_w179Remote.TryGetValue(src, out var r) || r.Seq != m.Seq) { r = new W179Remote { Src = src, Seq = m.Seq, Kind = 'R', Name = "scene", AtMs = now }; _w179Remote[src] = r; }
                r.X = m.X; r.Y = m.Y; r.Z = m.Z; r.HaveDest = true; r.DestAtMs = now;
                if (r.Kind == 'V')
                {
                    // WO-179B item 3: a video's destination is the triggerer's settle report -- the landing after the video uses it
                    if (r.OwnCopy) { Console.WriteLine($"WO179-LAND seq={r.Seq} settle report ignored: this game played its own quest copy (its own quest moves this Henry)"); break; }
                    int landing = Volatile.Read(ref r.Landing);
                    if (landing == 2 && r.LateDest && !r.Brought)
                    {
                        Interlocked.Increment(ref _w179LateDests);
                        Console.WriteLine(FormattableString.Invariant($"WO179-LAND seq={r.Seq} the settle report came after the 3 s black ({(now - r.FadeAtMs) / 1000.0:F1} s) -- moved now behind a short fade (still one move)"));
                        _ = Wo179BringAlongAsync(r, "settle-late");
                    }
                    else if (landing != 1)
                        Console.WriteLine($"WO179-LAND seq={r.Seq} settle report kept: {(landing == 0 ? "the video has not ended here yet" : "already landed")}");
                    break;
                }
                _ = Wo179BringAlongAsync(r, "destination");
                break;
            }
            case 'E':
                if (_w179Remote.TryGetValue(src, out var re) && re.Seq == m.Seq)
                {
                    re.Ended = true; re.How = m.How; re.AtMs = now;
                    if (re.Kind == 'V')
                    {
                        // C1: the video that ended there ends here on the same beat (WO-179B: on any end while mp_scene_skip_both is on)
                        if (re.CopyPlaying && (_w179SkipBoth || m.How == "end" || re.SkipKey))
                        {
                            Console.WriteLine($"WO179-VIDEO the triggerer's video ended ({m.How}) -- this copy ends on the same beat");
                            _ = Wo179CopyEndAsync(re, "partner-ended");   // never awaited here: the reader loop must deliver the settle report
                        }
                        else if (re.CopyPlaying)
                            Console.WriteLine("WO179-VIDEO the triggerer skipped alone -- this copy plays on until this player skips or it ends (mp_scene_skip_both off); the move follows its end");
                        else if (!re.OwnCopy && _w179Bring && Volatile.Read(ref re.Landing) == 0)
                            _ = Wo179VideoLandAsync(re, "triggerer-ended");   // this player was not watching any more (or had no copy)
                    }
                    Wo179RunHeld();
                }
                break;
            case 'K':
            {
                var own = _w179Cur;
                bool mine = own is { Video: true, Sent: true } && own.Seq == m.Seq;
                if (mine)
                {
                    // the partner pressed skip on THIS game's story video: the game's own skip from outside is not proven (WO-179B item 1)
                    own!.PartnerSkipKey = true;
                    Console.WriteLine($"WO179-VIDEO the partner pressed skip on this game's video seq={m.Seq} -- {(own.SkipKey ? "both pressed: one end" : "it plays on here (the game's own skip from outside is not proven); his skip ended his own copy")}");
                    break;
                }
                if (_w179Remote.TryGetValue(src, out var rk) && rk.Seq == m.Seq)
                {
                    // the game's second press may only jump to the video's next chapter: the copy ends with the triggerer's real end (E)
                    rk.TriggererSkipKey = true;
                    Console.WriteLine($"WO179-VIDEO the triggerer pressed skip on seq={m.Seq}{(rk.SkipKey ? " -- both pressed: one end" : "")} -- this copy ends with the triggerer's video (its end message)");
                }
                break;
            }
            case 'W':
                // WO-179B item 2: a joiner watches this host's video -- this world (the real enemies) leaves his figure alone
                if (W137Host && _w179WatchSafe)
                {
                    Console.WriteLine(FormattableString.Invariant($"WO179-WATCH player {src} watches this game's video at ({m.X:F1}, {m.Y:F1}, {m.Z:F1}) -- the fight round his figure ends and this world's people leave it alone"));
                    await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W179Protect then KCD2MP_W179Protect(\"{src}\", {m.X:F2}, {m.Y:F2}, {m.Z:F2}, {Wo179Rules.IgnoreWindowS}) end"));
                }
                break;
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
        if (secs > 0)
        {
            // the copy's own end: black first, then the movie stops (the Lua length check is the fallback, 1.5 s later)
            await Task.Delay(TimeSpan.FromSeconds(secs));
            if (r.CopyPlaying) await Wo179CopyEndAsync(r, "ended");
        }
    }

    // ================================================================ WO-179B item 2: safe while watching

    /// <summary>
    /// At the start of the partner's video, on this (watching) player: the quiet exit ladder at once (the copy's skip check held around it)
    /// and the ignore window at his own spot for the whole video -- in the host's world: here when this is the host, else the host's (W).
    /// </summary>
    private async Task Wo179WatchSafeAsync(W179Remote r)
    {
        if (W179NotInWorld || Wo136Holding || _jj is not null) { Console.WriteLine($"WO179-WATCH seq={r.Seq} not done: loading or joining"); return; }
        Interlocked.Increment(ref _w179Watches);
        long t0 = Environment.TickCount64;
        Console.WriteLine($"WO179-WATCH seq={r.Seq} player {r.Src}'s video '{r.Name}' starts -- this player leaves what he is doing and the people near him leave him alone while he watches");
        await Wo179HoldAsync(true);
        try
        {
            string ladder = await AskModAsync("KCD2MP_W179Ladder", 3000);
            Console.WriteLine($"WO179-WATCH seq={r.Seq} ladder {Environment.TickCount64 - t0} ms after the start: {ladder}");
            float x = _w179HavePos ? _w179X : _lastX, y = _w179HavePos ? _w179Y : _lastY, z = _w179HavePos ? _w179Z : _lastZ;
            if (_w179Ignored)
            {
                if (_isDamageAuthority) await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W179Ignore then KCD2MP_W179Ignore(\"player\", {x:F2}, {y:F2}, {z:F2}, {Wo179Rules.IgnoreWindowS}) end"));
                else await Wo179SendAsync(Wo179Rules.Watch(r.Seq, x, y, z), r.Src);
                Console.WriteLine($"WO179-WATCH seq={r.Seq} ignore window started {Environment.TickCount64 - t0} ms after the start ({(_isDamageAuthority ? "this world" : "the host's world, for this player's figure")})");
            }
            else Console.WriteLine($"WO179-WATCH seq={r.Seq} no ignore window: mp_scene_partner_ignored off");
            if (ladder.Contains("riding=1", StringComparison.Ordinal))
            {
                for (int i = 0; i < 6; i++)
                {
                    await Task.Delay(500);
                    string dm = await AskModAsync("KCD2MP_Wo114Dismount", 2000);
                    if (dm.Contains("mounted=no", StringComparison.Ordinal)) break;
                }
            }
            await Task.Delay(300);   // the dismount or the stand-up settles before the copy's pose is taken again
        }
        catch (Exception ex) { Console.WriteLine($"WO179-WATCH seq={r.Seq} failed: {ex.GetType().Name}: {ex.Message}"); }
        finally { await Wo179HoldAsync(false); }
    }

    // ================================================================ WO-179B item 3: the move after the video

    /// <summary>
    /// The triggerer's video ended (its end, its skip, the copy's length): black at once (the copy stops behind it), wait for the settle
    /// report, land beside it, confirm the ground, lift. The black is bounded: 3 s in all, 1 s past the report; without a report the black
    /// lifts and the move follows the report behind a short fade (still one move).
    /// </summary>
    private async Task Wo179VideoLandAsync(W179Remote r, string why)
    {
        if (Interlocked.CompareExchange(ref r.Landing, 1, 0) != 0)
        {
            if (Wo179TakeCopy(r)) await ExecLuaAsync($"if KCD2MP_W179StopVideo then KCD2MP_W179StopVideo(\"{why}\") end");
            return;
        }
        bool faded = false, lifted = false;
        long fadeAt = Environment.TickCount64;
        r.FadeAtMs = fadeAt;
        try
        {
            if (W179NotInWorld || Wo136Holding || _jj is not null)
            {
                if (Wo179TakeCopy(r)) await ExecLuaAsync($"if KCD2MP_W179StopVideo then KCD2MP_W179StopVideo(\"{why}\") end");
                Console.WriteLine($"WO179-LAND seq={r.Seq} not moved: this player is loading or joining -- caught up on the next join as today");
                return;
            }
            Interlocked.Increment(ref _w179Lands);
            var fr = await _combat.Wo163Async(Protocol.Wo179OpFade, [1, .. BitConverter.GetBytes(Wo179Rules.VideoEndFadeS)]);
            faded = fr is { Ok: true } f && f.Payload.Length > 0 && f.Payload[0] == 1;
            if (Wo179TakeCopy(r))
            {
                await Task.Delay(60);   // the fader is black before the movie's last frame goes
                await ExecLuaAsync($"if KCD2MP_W179StopVideo then KCD2MP_W179StopVideo(\"{why}\") end");
            }
            Console.WriteLine($"WO179-LAND seq={r.Seq} the triggerer's video ended ({why}) -- black now (fade {(faded ? "ok" : "REFUSED")}), waiting for where Henry settles (at most {Wo179Rules.PartnerFadeMaxMs / 1000} s)");
            // the settle report (kept if it already came)
            while (!r.HaveDest && Environment.TickCount64 < fadeAt + Wo179Rules.PartnerFadeMaxMs - 300) await Task.Delay(25);
            if (!r.HaveDest)
            {
                r.LateDest = true;
                Console.WriteLine(FormattableString.Invariant($"WO179-LAND seq={r.Seq} no settle report within {(Environment.TickCount64 - fadeAt) / 1000.0:F1} s -- the black is lifted; the move follows the report behind a short fade"));
                return;
            }
            long deadline = Wo179Rules.PartnerFadeDeadline(fadeAt, r.DestAtMs);
            float d0 = Wo179Rules.Dist2D(_w179HavePos ? _w179X : _lastX, _w179HavePos ? _w179Y : _lastY, r.X, r.Y);
            if (d0 < 8f) { r.Brought = true; Console.WriteLine($"WO179-LAND seq={r.Seq} already beside where Henry settled ({d0:F1} m): nothing to move"); return; }
            var own = _w179Cur;
            if (own is not null && own.Type != "Dialogue")
            {
                Interlocked.Increment(ref _w179Queued);
                _w179Held.Enqueue((r.Src, r.Seq));
                Console.WriteLine($"WO179-QUEUE seq={r.Seq} landing held -- this player's own scene '{own.Name}' runs first");
                return;
            }
            // C3 again, bounded: whatever he started after his own skip ends the game's own way
            string ladder = await AskModAsync("KCD2MP_W179Ladder", (int)Math.Clamp(deadline - Environment.TickCount64 - 300, 200, 1000));
            if (ladder.Contains("riding=1", StringComparison.Ordinal))
                while (Environment.TickCount64 < deadline - 400)
                {
                    string dm = await AskModAsync("KCD2MP_Wo114Dismount", 500);
                    if (dm.Contains("mounted=no", StringComparison.Ordinal)) break;
                    await Task.Delay(150);
                }
            var (pr, ring) = await Wo179PlaceAsync(r.X, r.Y, r.Z);
            // the ground: two reads GroundReadGapMs apart, the height steady and near the placed spot (or the deadline: lifted anyway)
            bool ground = false;
            if (pr is { Ok: true })
                while (Environment.TickCount64 < deadline)
                {
                    float z1 = _w179Z;
                    await Task.Delay((int)Wo179Rules.GroundReadGapMs);
                    if (_w179HavePos && Math.Abs(_w179Z - z1) < Wo179Rules.GroundSteadyM && Wo179Rules.Dist2D(_w179X, _w179Y, pr.After[0], pr.After[1]) < 2f
                        && Math.Abs(_w179Z - pr.After[2]) < 3f) { ground = true; break; }
                }
            if (pr is not { Ok: true })
            {
                Console.WriteLine($"WO179-LAND seq={r.Seq} NOT placed (no free ground within 10 m{(pr is null ? "; no answer from the plugin" : "")}) -- the black is lifted");
                return;
            }
            r.Brought = true;
            Interlocked.Increment(ref _w179BringsPlaced);
            float ax = pr.After[0], ay = pr.After[1], az = pr.After[2];
            await Wo179LiftAsync(faded);
            lifted = true;
            Console.WriteLine(FormattableString.Invariant(
                $"WO179-LAND seq={r.Seq} placed {Wo179Rules.Dist2D(ax, ay, r.X, r.Y):F1} m from where the triggerer settled (ring {ring:F0} m) at ({ax:F1}, {ay:F1}, {az:F1}); black {Environment.TickCount64 - fadeAt} ms (report {r.DestAtMs - fadeAt} ms in, bound {deadline - fadeAt} ms); ground {(ground ? "confirmed" : "not confirmed by the bound -- lifted anyway")}"));
            await Wo179AfterLandingAsync(r, ax, ay, az);
        }
        catch (Exception ex) { Console.WriteLine($"WO179-LAND seq={r.Seq} failed: {ex.GetType().Name}: {ex.Message}"); }
        finally
        {
            if (!lifted && faded) await Wo179LiftAsync(true);
            if (Wo179TakeCopy(r)) await ExecLuaAsync($"if KCD2MP_W179StopVideo then KCD2MP_W179StopVideo(\"{why}\") end");
            Volatile.Write(ref r.Landing, 2);
        }
    }

    private async Task Wo179LiftAsync(bool faded)
    {
        if (!faded) return;
        await _combat.Wo163Async(Protocol.Wo179OpFade, [0, .. BitConverter.GetBytes(Wo179Rules.VideoLandFadeInS)]);
    }

    /// <summary>C4: a free ground spot 3, 6 or 10 m from (x,y,z), on its floor (the DLL's navmesh snap within 3 m height).</summary>
    private async Task<(CombatPipe.JoinPlaceReport? Report, float Ring)> Wo179PlaceAsync(float x, float y, float z)
    {
        CombatPipe.JoinPlaceReport? pr = null;
        float ring = 0;
        foreach (float rm in Wo179Rules.LandingRingsM)
        {
            _w179OwnMoveMs = Environment.TickCount64;
            pr = await _combat.JoinPlaceAsync(x, y, z, rm);
            ring = rm;
            if (pr is { Ok: true }) break;
        }
        return (pr, ring);
    }

    /// <summary>C4: the landing's ignore window runs in the host's world (the scene's people are the host's).</summary>
    private async Task Wo179AfterLandingAsync(W179Remote r, float ax, float ay, float az)
    {
        if (!_w179Ignored) return;
        if (_isDamageAuthority) await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W179Ignore then KCD2MP_W179Ignore(\"player\", {ax:F2}, {ay:F2}, {az:F2}, {Wo179Rules.IgnoreWindowS}) end"));
        else await Wo179SendAsync(Wo179Rules.Landed(r.Seq, ax, ay, az));
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
            // WO-179B: a video's partner is never moved under the video any more (Wo179VideoLandAsync); what comes here moves behind the own fade
            var fr = await _combat.Wo163Async(Protocol.Wo179OpFade, [1, .. BitConverter.GetBytes(Wo179Rules.OwnFadeS)]);
            bool faded = fr is { Ok: true } f && f.Payload.Length > 0 && f.Payload[0] == 1;
            await Task.Delay((int)(Wo179Rules.OwnFadeS * 1000) + 50);
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
            var (pr, ring) = await Wo179PlaceAsync(r.X, r.Y, r.Z);
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
                $"WO179-BRING seq={r.Seq} placed {Wo179Rules.Dist2D(ax, ay, r.X, r.Y):F1} m from the relocated Henry (ring {ring:F0} m) at ({ax:F1}, {ay:F1}, {az:F1}) behind the fade"));
            await Wo179AfterLandingAsync(r, ax, ay, az);
        }
        catch (Exception ex) { Console.WriteLine($"WO179-BRING seq={r.Seq} failed: {ex.GetType().Name}: {ex.Message}"); }
        finally { Volatile.Write(ref _w179BringBusy, 0); }
    }
}
