// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;

namespace KcdMp.Client;

// WO-194 -- shared in-game cutscenes (Wo194Rules has the pure rules; docs/WO-194-findings.md the evidence). In-game cutscenes play for both
// players the way pre-rendered videos already do (WO-179B): the same moment, one skip for both, both come out where the quest continues, and
// nothing about a scene ever holds or requests a save (WO-192 Part 1). Everything outside in-game cutscenes stays as it was.
//   eyes     the DLL's EnqueueCutscene events (op 60, polled at 10 Hz): this machine's quest enqueued an in-game cutscene (or the mod did: B2;
//            or it was held: B3); WO-151's stage feed (PlayCutscene, OnCutsceneEnd, OnRequestFastForwardedBehavior, OnPositioningFinished)
//   bus      W164 kind 15, WO-179's scene bus with appended letters: C started by this quest (Q its handler's path first), A the answer,
//            G the sequence runs, P its clock (1 s), J a skip's jump, X a skip's stop, Z its end
//   A        the joiner's end positioning never stalls on copies (op 64 from OnCutsceneEnd until OnPositioningFinished), his Henry nudged off
//   B        the other machine's own copy through its own handler (op 61); the second copy's clock aligned (op 63); the joiner's hold (op 62)
//   C        a skip key press that jumped or stopped this copy is sent; the partner applies the time or stops
//   D        the joiner's own time of day is off (the DLL); the host's clock reaches it at once (a world-time reading right after the host's
//            scene starts, so the clock-jump watcher reports it within ~2 s)
// Log tags: WO194-SCENE, WO194-BUS, WO194-START, WO194-HOLD, WO194-CLOCK, WO194-SKIP, WO194-END, WO194-NUDGE, WO194-TIME, WO194-CFG, WO194-STATS.
public partial class GameBridge
{
    private volatile bool _w194Shared = Wo194Rules.DefaultShared, _w194Hold = Wo194Rules.DefaultHold, _w194FfStop = Wo194Rules.DefaultFfStop,
                          _w194TimeGate = Wo194Rules.DefaultTimeGate, _w194Nudge = Wo194Rules.DefaultNudge, _w194FigureHide = Wo194Rules.DefaultFigureHide,
                          _w194SkipBoth = Wo194Rules.DefaultSkipBoth;

    private sealed class W194Own
    {
        public int Seq; public string Name = "", Path = "";
        public long EnqueueMs, PlayMs, EndMs, PositionedMs;
        public bool Mod, Held, Sent, FirstAligned, HostPlayedIt, FigureHidden;
        public double LastT = -1; public long LastTAtMs;
        public byte Partner = 255;   // the partner it is shared with (a C we answered, or a G/A for our C)
    }

    private sealed class W194Remote
    {
        public byte Src; public int Seq; public string Name = "", Path = "";
        public long AtMs, RunsAtMs, EndedMs; public int Answer = -1;
        public double LastT = -1; public long LastTAtMs;
    }

    private readonly object _w194Lock = new();
    private W194Own? _w194Own;
    private readonly ConcurrentDictionary<byte, W194Remote> _w194Remote = new();
    private readonly ConcurrentDictionary<(byte, int), string> _w194Paths = new();
    private readonly ConcurrentDictionary<string, long> _w194Played = new(StringComparer.Ordinal);   // the scenes this machine played since its last load
    private uint _w194NatSeq;
    private bool _w194Primed;
    private string _w194CfgSent = "";
    private long _w194CfgAtMs;
    private long _w194Events, _w194Sent, _w194Started, _w194Bystander, _w194Held, _w194Releases, _w194Aligns, _w194Jumps, _w194Stops, _w194FfStops,
                 _w194Positioned, _w194Nudges, _w194TimeScenes;

    private string W194Role => _isDamageAuthority ? "host" : "joiner";
    private bool W194Partnered => LivePartners().Count > 0;

    // ================================================================ switches (the mod's w194_cfg)

    private void Wo194OnEvent(string name, string? arg)
    {
        if (name == "w194_test") { if (W194Test) _ = Wo194TestAsync(arg); return; }
        if (name != "w194_cfg") return;
        foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq <= 0) continue;
            bool on = kv[(eq + 1)..] == "on";
            switch (kv[..eq])
            {
                case "shared": _w194Shared = on; break;
                case "hold": _w194Hold = on; break;
                case "ffstop": _w194FfStop = on; break;
                case "timegate": _w194TimeGate = on; break;
                case "nudge": _w194Nudge = on; break;
                case "figure": _w194FigureHide = on; break;
                case "skip": _w194SkipBoth = on; break;
            }
        }
        _w194CfgSent = "";
        Console.WriteLine($"WO194-CFG ingame_shared={On(_w194Shared)} ingame_hold={On(_w194Hold)} ingame_ffstop={On(_w194FfStop)} ingame_time_host={On(_w194TimeGate)} ingame_nudge={On(_w194Nudge)} ingame_figure_hide={On(_w194FigureHide)} ingame_skip_both={On(_w194SkipBoth)} handlers={Wo194Rules.HandlerCount} time_scenes={Wo194Rules.TimeCount}");
    }

    private void Wo194OnConnect(CancellationToken ct)
    {
        _ = ExecLuaAsync("if KCD2MP_W194Cfg then KCD2MP_W194Cfg() end");
        _ = Wo194PollLoopAsync(ct);
    }

    private async Task Wo194PushCfgAsync(long now)
    {
        byte role = (byte)(W137Host ? 1 : W137Joiner ? 2 : 0);
        bool hold = _w194Shared && _w194Hold && W194Partnered;
        string want = $"{role}/{(hold ? 1 : 0)}/{(_w194FfStop ? 1 : 0)}/{(_w194TimeGate && _w194Shared ? 1 : 0)}";
        if (want == _w194CfgSent && now - _w194CfgAtMs < 10_000) return;
        var r = await _combat.Wo194ConfigAsync(role, (byte)(hold ? 1 : 0), (byte)(_w194FfStop ? 1 : 0), (byte)(_w194TimeGate && _w194Shared ? 1 : 0), 0xFFFF);
        if (r is null) return;
        if (want != _w194CfgSent) Console.WriteLine($"WO194-CFG the DLL: role={(role == 1 ? "host" : role == 2 ? "joiner" : "unknown")} hold={(hold ? "armed" : "off")} ff_stop={On(_w194FfStop)} time_gate={On(_w194TimeGate && _w194Shared)}");
        _w194CfgSent = want; _w194CfgAtMs = now;
    }

    // ================================================================ the DLL's events and the 1 s tick

    private async Task Wo194PollLoopAsync(CancellationToken ct)
    {
        long lastTick = 0, lastStats = Environment.TickCount64;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(100, ct); } catch { break; }
            try
            {
                await Wo194PollAsync();
                long now = Environment.TickCount64;
                if (now - lastTick >= 1000) { lastTick = now; await Wo194TickAsync(now); }
                if (now - lastStats >= 60_000) { lastStats = now; if (Interlocked.Read(ref _w194Events) + Interlocked.Read(ref _w194Sent) > 0) Console.WriteLine(Wo194StatsLine()); }
            }
            catch (Exception ex) { Console.WriteLine($"WO194 tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private string Wo194StatsLine() =>
        $"WO194-STATS events={_w194Events} sent={_w194Sent} started={_w194Started} bystander={_w194Bystander} held={_w194Held} releases={_w194Releases} aligns={_w194Aligns} jumps={_w194Jumps} stops={_w194Stops} ff_stops={_w194FfStops} positioned={_w194Positioned} nudges={_w194Nudges} time_scenes={_w194TimeScenes}";

    private async Task Wo194PollAsync()
    {
        var r = await _combat.Wo194PollAsync(_w194NatSeq);
        if (r is not { } x) return;
        if (x.Last < _w194NatSeq) _w194NatSeq = 0;   // a new DLL
        if (!_w194Primed) { _w194Primed = true; _w194NatSeq = x.Last; return; }   // an earlier agent's history
        foreach (var e in x.Events)
        {
            if (e.Seq <= _w194NatSeq) continue;
            _w194NatSeq = e.Seq;
            Interlocked.Increment(ref _w194Events);
            Wo194OnNativeEvent(e);
        }
        if (x.Last > _w194NatSeq) _w194NatSeq = x.Last;
    }

    private async Task Wo194TickAsync(long now)
    {
        await Wo194PushCfgAsync(now);
        // a menu or a load releases a held copy at once (it then plays solo; the DLL's own 3 s bound is the backstop)
        var own = _w194Own;
        if (own is { Held: true, PlayMs: 0 } && (W179NotInWorld))
        {
            own.Held = false;
            await _combat.Wo194ReleaseAsync(own.Name, 4);
            Console.WriteLine($"WO194-HOLD '{own.Name}' released: {_where} -- it plays solo");
        }
    }

    private void Wo194OnNativeEvent(CombatPipe.Wo194Event e)
    {
        long now = Environment.TickCount64;
        string tail = $"'{e.Name}' handler={(e.Path.Length > 0 ? e.Path : "-")} ({e.Detail}) {e.AgeMs} ms ago";
        switch (e.Kind)
        {
            case 1:   // this machine's quest enqueued it
            case 3:   // ... and the DLL held it (the joiner's own trigger first)
            {
                var o = new W194Own { Name = e.Name, Path = e.Path, EnqueueMs = now - e.AgeMs, Held = e.Kind == 3, Seq = Interlocked.Increment(ref _w179Seq) };
                lock (_w194Lock) _w194Own = o;
                if (o.Held) Interlocked.Increment(ref _w194Held);
                Console.WriteLine($"WO194-SCENE this quest enqueued {tail}{(o.Held ? " -- HELD at initialized while the host's copy starts (3 s at most)" : "")}");
                if (_w194Shared && W194Partnered && !Wo179Rules.IsCrimeScene(e.Name))
                {
                    o.Sent = true;
                    Interlocked.Increment(ref _w194Sent);
                    _ = Task.Run(async () =>
                    {
                        foreach (string q in Wo194Rules.HandlerPieces(o.Seq, o.Path)) await Wo179SendAsync(q);
                        await Wo179SendAsync(Wo194Rules.Started(o.Seq, o.Name));
                    });
                }
                else if (o.Held) _ = _combat.Wo194ReleaseAsync(o.Name, 4);   // nothing to wait for
                break;
            }
            case 2:   // the mod started it (B2): this copy follows the partner's
            {
                W194Remote? rem = _w194Remote.Values.Where(r => r.Name == e.Name).OrderByDescending(r => r.AtMs).FirstOrDefault();
                var o = new W194Own { Name = e.Name, Path = e.Path, EnqueueMs = now - e.AgeMs, Mod = true, Seq = Interlocked.Increment(ref _w179Seq), Partner = rem?.Src ?? 255, HostPlayedIt = rem is not null && !W137Host };
                lock (_w194Lock) _w194Own = o;
                Console.WriteLine($"WO194-SCENE the partner's scene started here through this quest's own handler {tail}");
                break;
            }
            case 4: Interlocked.Increment(ref _w194Releases); Console.WriteLine($"WO194-HOLD released {tail}"); { var o = _w194Own; if (o is not null && o.Name == e.Name) o.Held = false; } break;
            case 5: Interlocked.Increment(ref _w194TimeScenes); Console.WriteLine($"WO194-TIME {tail} -- the scene's own time of day is off here: the host's clock gives it"); break;
            case 6: Console.WriteLine($"WO194-TIME restored {tail}"); break;
            case 7: Console.WriteLine($"WO194-HOLD re-check {tail}"); break;
        }
    }

    // ================================================================ the stage feed (WO-151)

    private void Wo194OnSceneStage(string stage, string type, string name)
    {
        if (type != "Ingame") return;
        long now = Environment.TickCount64;
        var o = _w194Own;
        if (o is null || o.Name != name)
        {
            if (stage != "PlayCutscene") return;
            o = new W194Own { Name = name, EnqueueMs = now, Seq = Interlocked.Increment(ref _w179Seq) };   // an enqueue the hook did not see
            lock (_w194Lock) _w194Own = o;
        }
        switch (stage)
        {
            case "PlayCutscene":
            {
                o.PlayMs = now; o.Held = false;
                var rem = _w194Remote.Values.FirstOrDefault(r => r.Name == name && r.EndedMs == 0);
                if (rem is not null) { o.Partner = rem.Src; if (!W137Host) o.HostPlayedIt = true; }
                Console.WriteLine($"WO194-SCENE '{name}' runs here (role={W194Role}){(rem is not null ? FormattableString.Invariant($", {(now - rem.AtMs) / 1000.0:F2} s after the partner's start") : "")}");
                if (_w194Shared && W194Partnered && (o.Sent || rem is not null)) _ = Wo179SendAsync(Wo194Rules.Runs(o.Seq, name));
                if (_w194FigureHide && W194Partnered && !o.FigureHidden) { o.FigureHidden = true; _ = ExecLuaAsync("if KCD2MP_W194FigureHide then KCD2MP_W194FigureHide(true) end"); }
                Wo194TimeOnPlay(name);
                if (_w194Shared && W194Partnered) _ = Wo194ClockLoopAsync(o);
                break;
            }
            case "OnCutsceneEnd":
                o.EndMs = now;
                if (o.FigureHidden) { o.FigureHidden = false; _ = ExecLuaAsync("if KCD2MP_W194FigureHide then KCD2MP_W194FigureHide(false) end"); }
                if (o.Partner != 255 && _w194Shared) _ = Wo179SendAsync(Wo194Rules.Ended(o.Seq, name));
                if (W137Joiner && _w194FfStop) _ = Wo194EndWindowAsync(o);
                break;
            case "OnRequestFastForwardedBehavior":
                if (W137Joiner && _w194FfStop) _ = Wo194StopCopiesAsync(o, "after the game's second FastForward request");
                break;
            case "OnPositioningFinished":
            {
                o.PositionedMs = now;
                Interlocked.Increment(ref _w194Positioned);
                Console.WriteLine(FormattableString.Invariant($"WO194-END '{name}' positioned {(o.EndMs > 0 ? (now - o.EndMs) / 1000.0 : 0):F2} s after its content (role={W194Role})"));
                if (Wo194Rules.NudgeWanted(W137Joiner, _w194Nudge, o.HostPlayedIt)) _ = Wo194NudgeAsync(o);
                break;
            }
            case "ReleaseScene":
            case "Interrupt":
                if (o.PlayMs > 0) _w194Played[name] = now;
                if (o.FigureHidden) { o.FigureHidden = false; _ = ExecLuaAsync("if KCD2MP_W194FigureHide then KCD2MP_W194FigureHide(false) end"); }
                lock (_w194Lock) if (ReferenceEquals(_w194Own, o)) _w194Own = null;
                break;
        }
    }

    private void Wo194OnLoad()
    {
        var o = _w194Own;
        lock (_w194Lock) _w194Own = null;
        _w194Remote.Clear();
        _w194Paths.Clear();
        _w194Played.Clear();
        if (o is { FigureHidden: true }) _ = ExecLuaAsync("if KCD2MP_W194FigureHide then KCD2MP_W194FigureHide(false) end");
        _ = Task.Run(async () => { try { await _combat.Wo194ReleaseAsync("", 4); } catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException) { } });
    }

    // ================================================================ A: the end never stalls (the joiner)

    /// <summary>A: from OnCutsceneEnd, every copy's FastForward stopped every 250 ms until OnPositioningFinished (5 s at most).</summary>
    private async Task Wo194EndWindowAsync(W194Own o)
    {
        long t0 = Environment.TickCount64;
        int n = 0;
        while (o.PositionedMs == 0 && Environment.TickCount64 - t0 < Wo194Rules.FfStopWindowMs && !W179NotInWorld)
        {
            await Wo194StopCopiesAsync(o, n == 0 ? "at the scene's end" : null);
            n++;
            await Task.Delay((int)Wo194Rules.FfStopEveryMs);
        }
        if (o.PositionedMs == 0)
            Console.WriteLine(FormattableString.Invariant($"WO194-END '{o.Name}' NOT positioned {(Environment.TickCount64 - o.EndMs) / 1000.0:F1} s after its content (FastForward stopped {n} times) -- WO-189's 20 s bound is the backstop"));
    }

    /// <summary>A: every NPC of the joiner's world in a FastForward (all of them are the host's copies; live, the gate also waited on NPCs the agent
    /// never streamed -- the WO-131 guard's parked ones), never the local player or the mod's figures.</summary>
    private async Task Wo194StopCopiesAsync(W194Own o, string? why)
    {
        (int Stopped, int InFF, int NoNpc, int Before, int After)? r = null;
        try { r = await _combat.Wo194StopFFAsync(null); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException) { }
        if (r is not { } x) { if (why is not null) Console.WriteLine($"WO194-END '{o.Name}' {why}: the DLL did not answer"); return; }
        Interlocked.Add(ref _w194FfStops, x.Stopped);
        if (x.Stopped > 0 || why is not null)
            Console.WriteLine($"WO194-END '{o.Name}' {why ?? "again"}: {x.Stopped} NPC(s) of this world were in a FastForward and stopped (C_NPC's own call); the NPC manager's FastForward count {x.Before} -> {x.After}");
    }

    /// <summary>A: after OnPositioningFinished both Henrys stand on the same tag point -- the joiner's is moved about 1 m off it, on the ground.</summary>
    private async Task Wo194NudgeAsync(W194Own o)
    {
        await Task.Delay(300);   // the game's own placement settles first
        if (W179NotInWorld || _jj is not null) return;
        float x = _w179HavePos ? _w179X : _lastX, y = _w179HavePos ? _w179Y : _lastY, z = _w179HavePos ? _w179Z : _lastZ;
        _w179OwnMoveMs = Environment.TickCount64;
        var pr = await _combat.JoinPlaceAsync(x, y, z, Wo194Rules.NudgeM);
        if (pr is not { Ok: true }) { Console.WriteLine($"WO194-NUDGE '{o.Name}' not moved (the placement refused)"); return; }
        bool ground = false;
        for (int i = 0; i < 6 && !ground; i++)
        {
            float z1 = _w179Z;
            await Task.Delay((int)Wo179Rules.GroundReadGapMs);
            ground = _w179HavePos && Math.Abs(_w179Z - z1) < Wo179Rules.GroundSteadyM && Math.Abs(_w179Z - pr.After[2]) < 3f;
        }
        Interlocked.Increment(ref _w194Nudges);
        Console.WriteLine(FormattableString.Invariant($"WO194-NUDGE '{o.Name}' this Henry moved {Wo179Rules.Dist2D(x, y, pr.After[0], pr.After[1]):F1} m off the tag point both Henrys were placed on, to ({pr.After[0]:F1}, {pr.After[1]:F1}, {pr.After[2]:F1}); ground {(ground ? "steady" : "not confirmed")}"));
    }

    // ================================================================ B: the bus

    private async Task Wo194OnBusAsync(byte src, string text)
    {
        if (!Wo194Rules.TryParse(text, out var m)) { Interlocked.Increment(ref _w164Malformed); return; }
        Interlocked.Increment(ref _w179BusIn);
        Console.WriteLine($"WO194-BUS in from player {src}: '{text}'");
        long now = Environment.TickCount64;
        var own = _w194Own;
        bool mine = own is not null && own.Name == m.Name;
        switch (m.Op)
        {
            case 'Q': _w194Paths.AddOrUpdate((src, m.Seq), m.Path, (_, have) => have.Length + m.Path.Length <= 200 ? have + m.Path : have); break;
            case 'C':
            {
                var r = new W194Remote { Src = src, Seq = m.Seq, Name = m.Name, AtMs = now };
                if (_w194Paths.TryRemove((src, m.Seq), out var p)) r.Path = p;
                _w194Remote[src] = r;
                await _combat.Wo194PartnerPlayingAsync(m.Name, true);
                int ans = await Wo194StartOwnCopyAsync(r, own);
                r.Answer = ans;
                await Wo179SendAsync(Wo194Rules.Answer(m.Seq, ans, m.Name), src);
                break;
            }
            case 'A':
                if (own is not null && own.Seq == m.Seq)
                {
                    Console.WriteLine($"WO194-SCENE player {src} answered for '{m.Name}': {Wo194Rules.AnswerName(m.Code)}");
                    if (m.Code is Wo194Rules.AnsStarted or Wo194Rules.AnsOwnAlready) own.Partner = src;
                    if (m.Code is Wo194Rules.AnsStarted or Wo194Rules.AnsOwnAlready && !W137Host) own.HostPlayedIt = true;
                    if (m.Code is Wo194Rules.AnsBystander or Wo194Rules.AnsRefused && own.Held)
                    {
                        own.Held = false;
                        await _combat.Wo194ReleaseAsync(own.Name, 4);
                        Console.WriteLine($"WO194-HOLD '{own.Name}' released: the host does not play it -- it plays solo here");
                    }
                }
                break;
            case 'G':
            {
                var r = _w194Remote.GetOrAdd(src, _ => new W194Remote { Src = src, Seq = m.Seq, Name = m.Name, AtMs = now });
                if (r.Name != m.Name) { r.Name = m.Name; r.Seq = m.Seq; r.AtMs = now; r.EndedMs = 0; }
                r.RunsAtMs = now;
                await _combat.Wo194PartnerPlayingAsync(m.Name, true);
                if (mine && own!.Held)
                {
                    own.Held = false;
                    var n = await _combat.Wo194ReleaseAsync(own.Name, 1);
                    Console.WriteLine($"WO194-HOLD '{own.Name}' released: the host's copy runs ({n?.ToString(CultureInfo.InvariantCulture) ?? "no answer"} hold(s))");
                }
                if (mine) { own!.Partner = src; if (!W137Host) own.HostPlayedIt = true; }
                break;
            }
            case 'P':
                if (_w194Remote.TryGetValue(src, out var rp) && rp.Name == m.Name) { rp.LastT = m.TimeMs / 1000.0; rp.LastTAtMs = now; }
                if (mine && _w194Shared) await Wo194AlignAsync(own!, src, m.TimeMs / 1000.0, now);
                break;
            case 'J':
                if (mine && _w194SkipBoth && own!.PlayMs > 0 && own.EndMs == 0)
                {
                    long oneWay = Wo161Rules.OneWayMs(_clockRttMedianMs);
                    double t = Wo194Rules.AlignTargetS(m.TimeMs / 1000.0, oneWay, Environment.TickCount64 - now);
                    var r = await _combat.Wo194SeqSetAsync(own.Name, (float)t);
                    Interlocked.Increment(ref _w194Jumps);
                    own.FirstAligned = true;
                    Console.WriteLine(FormattableString.Invariant($"WO194-SKIP player {src} skipped '{m.Name}' to {m.TimeMs / 1000.0:F2} s -- this copy jumps too: {(r is { Res: 1 } s ? $"{s.Before:F2} -> {s.After:F2} s" : $"refused ({r?.Res.ToString(CultureInfo.InvariantCulture) ?? "no answer"})")}"));
                }
                break;
            case 'X':
                if (mine && _w194SkipBoth && own!.PlayMs > 0 && own.EndMs == 0)
                {
                    var r = await _combat.Wo194SeqStopAsync(own.Name);
                    Interlocked.Increment(ref _w194Stops);
                    Console.WriteLine($"WO194-SKIP player {src} skipped the rest of '{m.Name}' -- this copy stops too: {(r == 1 ? "stopped (its normal end follows)" : $"refused ({r?.ToString(CultureInfo.InvariantCulture) ?? "no answer"})")}");
                }
                break;
            case 'Z':
                if (_w194Remote.TryGetValue(src, out var rz) && rz.Name == m.Name) rz.EndedMs = now;
                await _combat.Wo194PartnerPlayingAsync(m.Name, false);
                break;
        }
    }

    /// <summary>B2: the partner's quest started a scene -- this machine's own copy through its own handler, when that handler is idle.</summary>
    private async Task<int> Wo194StartOwnCopyAsync(W194Remote r, W194Own? own)
    {
        long now = Environment.TickCount64;
        if (own is not null && own.Name == r.Name && own.EndMs == 0 && now - own.EnqueueMs < 180_000)
        {
            Console.WriteLine($"WO194-START '{r.Name}': this quest already {(own.Held ? "holds" : own.PlayMs > 0 ? "plays" : "queued")} its own copy (both triggered)");
            own.Partner = r.Src;
            if (!W137Host) own.HostPlayedIt = true;
            return Wo194Rules.AnsOwnAlready;
        }
        bool played = _w194Played.ContainsKey(r.Name);
        if (Wo194Rules.StartRefusal(_w194Shared, !(W179NotInWorld || _jj is not null), Wo179Rules.IsCrimeScene(r.Name), played) is string why)
        {
            if (played) Interlocked.Increment(ref _w194Bystander);
            Console.WriteLine($"WO194-START '{r.Name}' not started here: {why}");
            return played ? Wo194Rules.AnsBystander : Wo194Rules.AnsRefused;
        }
        var paths = new List<string>();
        if (r.Path.Length > 0) paths.Add(r.Path);
        foreach (var h in Wo194Rules.HandlersOf(r.Name)) { string p = Wo194Rules.HandlerPath(h); if (!paths.Contains(p)) paths.Add(p); }
        if (paths.Count == 0) { Interlocked.Increment(ref _w194Bystander); Console.WriteLine($"WO194-START '{r.Name}': no handler path (none sent, none in the data) -- a bystander here"); return Wo194Rules.AnsBystander; }
        byte last = 2;
        foreach (string p in paths)
        {
            var res = await _combat.Wo194StartAsync(p, r.Name);
            last = res?.Result ?? 3;
            Console.WriteLine($"WO194-START '{r.Name}' through {p}: {Wo194Rules.StartResultName(last)}{(res is { } x && x.State != 255 ? $" (handler state {x.State})" : "")}");
            if (last == 1) { Interlocked.Increment(ref _w194Started); return Wo194Rules.AnsStarted; }
            if (last is 0 or 7) break;   // the handler exists and is not at that step: no other path is tried
        }
        int a = Wo194Rules.AnswerOfStart(last);
        if (a == Wo194Rules.AnsBystander) Interlocked.Increment(ref _w194Bystander);
        Console.WriteLine($"WO194-START '{r.Name}': {Wo194Rules.AnswerName(a)} -- the partner's figure is a bystander here; nothing is forced");
        return a;
    }

    // ================================================================ B3: the clock

    /// <summary>B3: this copy's clock goes to the partner every second while it plays (the follower sets its own from it).</summary>
    private async Task Wo194ClockLoopAsync(W194Own o)
    {
        while (o.EndMs == 0 && !W179NotInWorld && ReferenceEquals(_w194Own, o))
        {
            var r = await _combat.Wo194SeqGetAsync(o.Name);
            long at = Environment.TickCount64;
            if (r is { Res: 1 } s && s.T >= 0)
            {
                o.LastT = s.T; o.LastTAtMs = at;
                if (o.Partner != 255 || _w194Remote.Values.Any(x => x.Name == o.Name && x.EndedMs == 0))
                    await Wo179SendAsync(Wo194Rules.Clock(o.Seq, (int)Math.Round(s.T * 1000), o.Name));
            }
            await Task.Delay((int)Wo194Rules.ClockEveryMs);
        }
    }

    private async Task Wo194AlignAsync(W194Own o, byte src, double partnerS, long arrivedMs)
    {
        if (o.PlayMs == 0 || o.EndMs != 0) return;
        long runsAt = _w194Remote.TryGetValue(src, out var rr) && rr.Name == o.Name ? rr.RunsAtMs : 0;
        if (!Wo194Rules.IsFollower(o.PlayMs, runsAt)) return;   // this copy ran first: the partner follows it
        var g = await _combat.Wo194SeqGetAsync(o.Name);
        if (g is not { Res: 1 } own || own.T < 0) return;
        long oneWay = Wo161Rules.OneWayMs(_clockRttMedianMs);
        double target = Wo194Rules.AlignTargetS(partnerS, oneWay, Environment.TickCount64 - arrivedMs);
        if (!Wo194Rules.NeedsSet(own.T, target, o.FirstAligned)) { o.FirstAligned = true; return; }
        var s = await _combat.Wo194SeqSetAsync(o.Name, (float)target);
        bool first = !o.FirstAligned;
        o.FirstAligned = true;
        Interlocked.Increment(ref _w194Aligns);
        Console.WriteLine(FormattableString.Invariant($"WO194-CLOCK '{o.Name}' {(first ? "aligned" : "drifted")}: this copy {own.T:F3} s, the partner's {partnerS:F3} s + {oneWay} ms one way -> {target:F3} s ({(target - own.T) * 1000:+0;-0} ms): {(s is { Res: 1 } x ? $"set ({x.Before:F3} -> {x.After:F3})" : $"refused ({s?.Res.ToString(CultureInfo.InvariantCulture) ?? "no answer"})")}"));
    }

    // ================================================================ C: one skip for both

    /// <summary>C: a skip key press (the mod's action hook) -- what it did to this copy is read 150 ms later and sent.</summary>
    private void Wo194OnSkipKey()
    {
        var o = _w194Own;
        if (o is null || o.PlayMs == 0 || o.EndMs != 0 || !_w194SkipBoth || !_w194Shared || o.Partner == 255 && !_w194Remote.Values.Any(r => r.Name == o.Name && r.EndedMs == 0)) return;
        double lastS = o.LastT; long lastAt = o.LastTAtMs;
        _ = Task.Run(async () =>
        {
            await Task.Delay((int)Wo194Rules.SkipReadDelayMs);
            var r = await _combat.Wo194SeqGetAsync(o.Name);
            bool playing = r is { Res: 1 } s && s.T >= 0 && o.EndMs == 0;
            double after = r?.T ?? -1;
            var seen = Wo194Rules.Classify(playing, after, lastS, Environment.TickCount64 - lastAt);
            switch (seen)
            {
                case Wo194Rules.SkipSeen.Jump:
                    o.LastT = after; o.LastTAtMs = Environment.TickCount64;
                    Interlocked.Increment(ref _w194Jumps);
                    Console.WriteLine(FormattableString.Invariant($"WO194-SKIP this player skipped '{o.Name}' to the next checkpoint ({after:F2} s) -- sent: the partner jumps too"));
                    await Wo179SendAsync(Wo194Rules.Jumped(o.Seq, (int)Math.Round(after * 1000), o.Name));
                    break;
                case Wo194Rules.SkipSeen.Stopped:
                    Interlocked.Increment(ref _w194Stops);
                    Console.WriteLine($"WO194-SKIP this player skipped the rest of '{o.Name}' (no checkpoint left: the scene ended) -- sent: the partner's stops too");
                    await Wo179SendAsync(Wo194Rules.Stopped(o.Seq, o.Name));
                    break;
                default:
                    Console.WriteLine("WO194-SKIP skip key: the scene did not jump (the game's arming press) -- nothing sent");
                    break;
            }
        });
    }

    // ================================================================ D: time of day

    /// <summary>D: a scene with its own time of day -- logged; on the host the world clock is read at once (the clock-jump watcher reports the
    /// jump to the joiner within ~2 s instead of the 10 s poll); on the joiner the DLL keeps the scene's own time off.</summary>
    private void Wo194TimeOnPlay(string name)
    {
        if (!Wo194Rules.TryTime(name, out string t)) return;
        Interlocked.Increment(ref _w194TimeScenes);
        Console.WriteLine($"WO194-TIME '{name}' sets the time of day to {t} -- {(W137Host ? "in the host's world (the joiner follows the host's clock)" : W137Joiner ? "not here: the host's clock gives it" : "this machine's own world")}");
        if (!W137Host || !_w194Shared) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            await ExecLuaAsync("if KCD2MP_ReportWorldTime then KCD2MP_ReportWorldTime() end");
            await Task.Delay(1500);
            await ExecLuaAsync("if KCD2MP_ReportWorldTime then KCD2MP_ReportWorldTime() end");
        });
    }

    // ================================================================ the live checks' reads

    /// <summary>"Readiness observer '&lt;scene&gt;' with category bitmask '15255' started async waiting | is ready": the scene's start black; the
    /// sequence clock is read at both edges (the live check: does it run under the black?).</summary>
    private void Wo194OnEngineLine(string line)
    {
        if (!line.StartsWith("Readiness observer '", StringComparison.Ordinal)) return;
        int e = line.IndexOf('\'', 20);
        if (e < 0) return;
        string name = line[20..e];
        var o = _w194Own;
        if (o is null || o.Name != name) return;
        string edge = line.Contains(" is ready", StringComparison.Ordinal) ? "ready" : line.Contains("started async waiting", StringComparison.Ordinal) ? "waiting" : "";
        if (edge.Length == 0) return;
        _ = Task.Run(async () =>
        {
            var r = await _combat.Wo194SeqGetAsync(name);
            Console.WriteLine(FormattableString.Invariant($"WO194-CLOCK '{name}' start readiness {edge}: the sequence clock reads {(r is { Res: 1 } s ? $"{s.T:F3} s" : $"nothing (res {r?.Res.ToString(CultureInfo.InvariantCulture) ?? "-"})")}, {(o.PlayMs > 0 ? Environment.TickCount64 - o.PlayMs : -1)} ms after PlayCutscene"));
        });
    }

    // Harness-only (KCDMP_TEST_W194=1, never set by the launcher): what one machine cannot stage the game's way.
    private static readonly bool W194Test = Environment.GetEnvironmentVariable("KCDMP_TEST_W194") == "1";

    private async Task Wo194TestAsync(string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string verb = f.Length > 0 ? f[0] : "";
        switch (verb)
        {
            case "state" when f.Length > 1:   // every handler of a scene: found, a handler, its state
                foreach (var h in Wo194Rules.HandlersOf(f[1]))
                {
                    string p = Wo194Rules.HandlerPath(h);
                    var s = await _combat.Wo194HandlerStateAsync(p);
                    Console.WriteLine($"WO194-TEST state '{f[1]}' {p}: {(s is { } x ? $"found={x.Found} handler={x.Handler} state={x.State} runtime(node..root)={x.Runtime}" : "no answer")}");
                }
                break;
            case "start" when f.Length > 1:   // start <scene> [own]: its handler pulsed (own = as this quest's own trigger: the hold applies)
            {
                bool own = f.Length > 2 && f[2] == "own";
                foreach (var h in Wo194Rules.HandlersOf(f[1]))
                {
                    string p = Wo194Rules.HandlerPath(h);
                    var r = await _combat.Wo194StartAsync(p, f[1], own);
                    Console.WriteLine($"WO194-TEST start '{f[1]}' {(own ? "as this quest's own" : "as the mod's")} through {p}: {(r is { } x ? Wo194Rules.StartResultName(x.Result) + $" (state {x.State})" : "no answer")}");
                    if (r is { Result: 1 }) break;
                }
                break;
            }
            case "copy" when f.Length > 2:   // copy <npc> <hexEid>: the NPC is paused and counted a copy (A's staging)
                if (uint.TryParse(f[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint eid))
                {
                    _npcEntityIds[f[1]] = eid;
                    await ExecLuaAsync($"System.ExecuteCommand(\"wh_ai_PauseNPC {f[1]}\")");
                    Console.WriteLine($"WO194-TEST copy {f[1]} eid=0x{eid:X}: paused and counted a copy");
                }
                break;
            case "statep" when f.Length > 1:   // statep <path>: any node, read only
            {
                var s = await _combat.Wo194HandlerStateAsync(f[1]);
                Console.WriteLine($"WO194-TEST statep {f[1]}: {(s is { } x ? $"found={x.Found} handler={x.Handler} state={x.State} runtime(node..root)={x.Runtime}" : "no answer")}");
                break;
            }
            case "startp" when f.Length > 2:   // startp <path> <scene> [own]
            {
                var r = await _combat.Wo194StartAsync(f[1], f[2], f.Length > 3 && f[3] == "own");
                Console.WriteLine($"WO194-TEST startp {f[1]} '{f[2]}': {(r is { } x ? Wo194Rules.StartResultName(x.Result) + $" (state {x.State})" : "no answer")}");
                break;
            }
            case "stop" when f.Length > 1:   // stop <scene>: StopSequence (the receive side of a partner's final skip)
                Console.WriteLine($"WO194-TEST stop '{f[1]}': {await _combat.Wo194SeqStopAsync(f[1])}");
                break;
            case "set" when f.Length > 2 && float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float tset):
            {
                var r = await _combat.Wo194SeqSetAsync(f[1], tset);
                Console.WriteLine($"WO194-TEST set '{f[1]}' {tset:F3}: {(r is { } x ? $"res={x.Res} {x.Before:F3} -> {x.After:F3}" : "no answer")}");
                break;
            }
            case "seq" when f.Length > 1:
            {
                var r = await _combat.Wo194SeqGetAsync(f[1]);
                Console.WriteLine($"WO194-TEST seq '{f[1]}': {(r is { } x ? $"res={x.Res} t={x.T.ToString("F3", CultureInfo.InvariantCulture)}" : "no answer")}");
                break;
            }
            case "status":
                Console.WriteLine($"WO194-TEST status: {Wo194StatusText()} dll: {await _combat.Wo194StatusAsync() ?? "no answer"}");
                break;
        }
    }

    // ================================================================ status

    private string Wo194StatusText() =>
        $"ingame_shared={On(_w194Shared)} hold={On(_w194Hold)} ffstop={On(_w194FfStop)} time_host={On(_w194TimeGate)} nudge={On(_w194Nudge)} figure_hide={On(_w194FigureHide)} skip_both={On(_w194SkipBoth)} "
        + $"scene={(_w194Own is { } o ? o.Name : "-")} {Wo194StatsLine()["WO194-STATS ".Length..]}";
}
