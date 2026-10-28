// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-189 -- 0.60.6: the joiner never gets stuck in half of the host's quest sequence, and its saves never stall silently
// (Wo189Rules has the pure rules; docs/WO-189-findings.md the evidence). The field: the host's tutorial wrapped a world layer in a barrier;
// the joiner's game ran only its end, its scene positioning waited 750 s on the host's paused copies, the ESC menu's snapshot request cancelled
// every NPC's fast-forward under the save lock, the barrier's end cutscene then sat at "initialized" and the save stayed pending 21 minutes.
//
//   A1  a snapshot wait that ends without its file cancels the engine's pending save at once (any purpose)          (mp_save_recover)
//   A2  the watchdog: a save pending on the joiner's engine for more than 60 s is named, cancelled, told, owed     (mp_save_recover)
//   (A3, the ask gate, and A4, the joiner's own snapshot and the host's reminder, were removed by WO-192: GameBridge.Wo192.cs)
//   A5  mp_save_status: WO189-STATUS beside WO184-STATUS
//   B   the host's quest world layers mirrored on the joiner (mp_profile_mirror, default on: its live check passed; the WO asked for off)
//   C   a cutscene stuck at "initialized" 10 s on the joiner: its copies' fast-forward stopped, then released the game's own way (mp_cutscene_release)
//   D   a scene still positioning 20 s after it began: its copies' fast-forward stopped (mp_positioning_bound); never a save request
//   F   the waits named: the readiness tasks that hold a save, the barrier's two concept States, what the AI task waits on
// Log tags: WO189-SAVE, WO189-READY, WO189-WATCHDOG, WO189-PROFILE, WO189-CUTSCENE, WO189-POSITIONING, WO189-BARRIER, WO189-CFG, WO189-STATUS.
public partial class GameBridge
{
    // ================================================================ switches (the mod's w189_cfg)

    private volatile bool _w189Mirror = Wo189Rules.DefaultProfileMirror, _w189Release = Wo189Rules.DefaultCutsceneRelease, _w189PosBound = Wo189Rules.DefaultPositioningBound;

    private void Wo189OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w189_cfg": Wo189OnCfg(arg); return;
            case "w189_test": if (W189Test) _ = Wo189TestAsync(arg); return;
        }
    }

    // Harness-only (KCDMP_TEST_W189=1, never set by the launcher): each new DLL op called once, its answer logged (WO189-TEST).
    private static readonly bool W189Test = Environment.GetEnvironmentVariable("KCDMP_TEST_W189") == "1";

    private async Task Wo189TestAsync(string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string verb = f.Length > 0 ? f[0] : "";
        string rest = f.Length > 1 ? string.Join(' ', f.Skip(1)) : "";
        try
        {
            switch (verb)
            {
                case "ready":
                    var r = await Wo189ReadyAsync();
                    Console.WriteLine($"WO189-TEST ready: {(r is null ? "no answer" : $"armed={On(r.Armed)} ready={On(r.Ready)} not_ready='{r.NotReady}' pos_armed={On(r.PosArmed)} pos_state={r.PosState} pos_active={On(r.PosActive)} pos_secs={r.PosSecs:F1}")} engine={await Wo184EngineStateAsync()}");
                    break;
                case "cutscene":
                    var c = await _combat.Wo189CutsceneAsync();
                    Console.WriteLine($"WO189-TEST cutscene: {(c is null ? "not armed / no answer" : $"active={On(c.Value.Active)} in_progress={On(c.Value.InProgress)} cfg=0x{c.Value.Cfg:X}")}");
                    break;
                case "release":   // release <1|2>: the active configuration, whatever it is (a refusal is the expected answer with none)
                    var cs = await _combat.Wo189CutsceneAsync();
                    byte? rr = await _combat.Wo189CutsceneReleaseAsync(rest == "2" ? (byte)2 : (byte)1, cs?.Cfg ?? 0);
                    Console.WriteLine($"WO189-TEST release mode={rest}: result={(rr?.ToString() ?? "no answer")}");
                    break;
                case "status":
                    Console.WriteLine($"WO189-TEST status: {await _combat.Wo189StatusAsync() ?? "no answer"}");
                    break;
                case "profiles":
                    var ap = await _combat.Wo189ActiveProfilesAsync();
                    Console.WriteLine($"WO189-TEST profiles: {(ap is null ? "no answer" : $"{ap.Count} active; quest ones: [{string.Join(",", Wo189Rules.CatchUp(ap, Wo189Rules.QuestProfiles))}]; first: [{string.Join(",", ap.Take(12))}]")}");
                    break;
                case "active":
                    Console.WriteLine($"WO189-TEST active '{rest}': {(await _combat.Wo189ProfileAsync(4, rest))?.ToString() ?? "no answer"}");
                    break;
                case "ffstop":
                    Console.WriteLine($"WO189-TEST ffstop: {await Wo189StopCopiesAsync()}");
                    break;
                case "barrier":   // barrier <holder> <dialogue>
                    if (f.Length >= 3) _w189Dialogue = (f[2], [], Environment.TickCount64);
                    await Wo189BarrierReadAsync(f.Length >= 2 ? f[1] : "");
                    break;
                case "concept":   // concept <path> <port>
                    if (f.Length >= 3) Console.WriteLine($"WO189-TEST concept {f[1]} . {f[2]}: {(await _combat.Wo189ConceptReadAsync(f[1], f[2]))?.ToString() ?? "no answer"}");
                    break;
                case "stats":
                    Console.WriteLine($"WO189-TEST stats: {Wo189StatusText(Environment.TickCount64)}");
                    break;
            }
        }
        catch (Exception ex) { Console.WriteLine($"WO189-TEST {verb} failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>"mirror=on|off release=on|off posbound=on|off".</summary>
    private void Wo189OnCfg(string? arg)
    {
        foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq <= 0) continue;
            string k = kv[..eq], v = kv[(eq + 1)..];
            bool on = v == "on";
            switch (k)
            {
                case "mirror": _w189Mirror = on; break;
                case "release": _w189Release = on; break;
                case "posbound": _w189PosBound = on; break;
            }
        }
        _w189CfgPushed = -1;   // the DLL's switches follow at the next tick
        Console.WriteLine($"WO189-CFG profile_mirror={On(_w189Mirror)} cutscene_release={On(_w189Release)} positioning_bound={On(_w189PosBound)}");
    }

    // the DLL's role and switches (pushed on a change, and every 30 s)
    private int _w189CfgPushed = -1;
    private long _w189CfgPushedMs;

    private async Task Wo189PushCfgAsync(long now)
    {
        byte role = W137Host ? (byte)1 : W137Joiner ? (byte)2 : (byte)0;
        int want = role | (_w189Release ? 4 : 0) | (_w189PosBound ? 8 : 0) | (_w189Mirror ? 16 : 0);
        if (want == _w189CfgPushed && now - _w189CfgPushedMs < 30_000) return;
        try
        {
            var r = await _combat.Wo189ConfigAsync(role, (byte)(_w189Release ? 1 : 0), (byte)((_w189PosBound || _w189Release) ? 1 : 0), (byte)(_w189Mirror ? 1 : 0));
            if (r is null) return;
            _w189CfgPushed = want;
            _w189CfgPushedMs = now;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException) { }
    }

    // ================================================================ A3 + F: the engine's readiness

    private int _w189RecoveredTold;
    private long _w189Watchdog, _w189WatchdogCancels, _w189A1Cancels;

    /// <summary>The DLL's readiness read (null = no answer). WO-192: read for log lines only, never a gate.</summary>
    private async Task<Wo189Rules.ReadyRead?> Wo189ReadyAsync()
    {
        try { return await _combat.Wo189ReadyAsync(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException) { return null; }
    }

    /// <summary>F: a requested save still pending: the readiness tasks that hold it, named once per stretch.</summary>
    private async Task Wo189NamePendingAsync(string what, long sinceMs)
    {
        var r = await Wo189ReadyAsync();
        long now = Environment.TickCount64;
        string tasks = r is null ? "the DLL did not answer" : !r.Armed ? "the readiness service is not armed on this build" : r.Ready ? "none (the service says ready)" : r.NotReady;
        Console.WriteLine($"WO189-READY {what} pending {Wo189Rules.Secs(now - sinceMs)} s -- not-ready tasks: {tasks}; positioning={(r is { PosArmed: true } p ? $"state {p.PosState} active={On(p.PosActive)} {p.PosSecs:F0} s" : "not read")}"
                          + $"{(r is { Ready: false } && (r.NotReady.Contains("[0x2]", StringComparison.Ordinal) || r.NotReady.Contains("0x2A", StringComparison.Ordinal)) ? " -- the AI task holds it: which copy is in a behaviour execution is not readable with the existing reads (WO-188)" : "")}");
    }

    // ================================================================ A1: a wait that ended without its file

    /// <summary>A1 (Wo184TakeAsync): the engine's pending save is cancelled at once (its own CancelPendingSaveGame), whatever the purpose.</summary>
    private async Task Wo189CancelAfterWaitAsync(string purpose, long tReq)
    {
        long waited = Environment.TickCount64 - tReq;
        if (!_w184Recover) { Console.WriteLine($"WO189-SAVE the {purpose}'s wait ended after {Wo189Rules.Secs(waited)} s with no file -- NOT cancelled (mp_save_recover is off)"); return; }
        await Wo189NamePendingAsync($"the {purpose}'s save", tReq);
        (byte Result, bool Before, bool After)? c = null;
        try { c = await _combat.Wo184SaveCancelAsync("wo189-a1"); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException) { }
        string res = c is null ? "the DLL did not answer (nothing cancelled)" : c.Value.Result switch
        {
            1 => "the engine's own CancelPendingSaveGame cleared it",
            0 => "nothing was pending any more",
            2 => "the engine's cancel is not armed on this build",
            4 => "the engine STILL says a save is pending after its own cancel",
            _ => "the cancel faulted",
        };
        if (c is { Result: 1 or 0 }) { Interlocked.Increment(ref _w189A1Cancels); Volatile.Write(ref _w184LinePendingSince, 0); _w189Pending.Reset(); }
        Console.WriteLine(Wo189Rules.CancelLine(purpose, waited, res));
    }

    // ================================================================ A2: the watchdog (every 5 s on the joiner)

    private readonly Wo189Rules.PendingWatch _w189Pending = new();
    private long _w189WatchNextMs;
    private bool _w189WatchTold;

    private async Task Wo189WatchdogAsync(long now)
    {
        if (now < _w189WatchNextMs) return;
        _w189WatchNextMs = now + Wo189Rules.WatchdogPeriodMs;
        if (!W137Joiner || !_joinedWorld) { _w189Pending.Reset(); return; }
        if (Volatile.Read(ref _w184ReqAtMs) != 0) return;   // an in-flight request: its own wait (A1, B3) decides
        var st = await Wo184EngineStateAsync();
        long known = Volatile.Read(ref _w184LinePendingSince);
        bool due = _w189Pending.Feed(st.Pending, now, known);
        if (st.Pending && _w189Pending.NameDue(now)) await Wo189NamePendingAsync("a save", _w189Pending.SinceMs);
        if (!due) return;
        Interlocked.Increment(ref _w189Watchdog);
        Console.WriteLine($"WO189-WATCHDOG the engine has had a save pending for {Wo189Rules.Secs(now - _w189Pending.SinceMs)} s with no request of the agent waiting on it -- named, cancelled, told, owed");
        await Wo189NamePendingAsync("the stalled save", _w189Pending.SinceMs);
        (byte Result, bool Before, bool After)? c = null;
        if (_w184Recover)
        {
            try { c = await _combat.Wo184SaveCancelAsync("wo189-watchdog"); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException) { }
        }
        bool cleared = c is { Result: 1 or 0 };
        if (cleared)
        {
            Interlocked.Increment(ref _w189WatchdogCancels);
            Volatile.Write(ref _w184LinePendingSince, 0);
            Console.WriteLine($"WO189-WATCHDOG cleared: {(c!.Value.Result == 1 ? "the engine's own CancelPendingSaveGame" : "nothing was pending any more")} -- a snapshot is owed and taken by the gate when the engine can");
        }
        else Console.WriteLine($"WO189-WATCHDOG NOT cleared: {(!_w184Recover ? "mp_save_recover is off" : c is null ? "the DLL did not answer" : c.Value.Result == 2 ? "the engine's cancel is not armed" : c.Value.Result == 4 ? "the engine still says pending after its own cancel" : "the cancel faulted")}");
        if (!_w189WatchTold)
        {
            _w189WatchTold = true;
            if (cleared)
            {
                await W164NoticeAsync("A save of your character was stuck; it was cleared and is taken again as soon as the game allows.");
                _ = W164SendAsync(Protocol.JoinTargetHost, Protocol.W164SaveHealth, Wo184Rules.RecoveredWord);
            }
            else
            {
                await W164NoticeAsync(Wo184Rules.JoinerStuckText);
                _ = W164SendAsync(Protocol.JoinTargetHost, Protocol.W164SaveHealth, Wo184Rules.StuckWord);
            }
        }
        if (cleared) Wo189OweNewest("the watchdog cleared a stalled save");
    }

    /// <summary>A snapshot owed with the newest host save (or the join's), retried by the WO-184 gate.</summary>
    private void Wo189OweNewest(string why, string purpose = "host save")
    {
        WorldSaved? host;
        lock (_w184Lock) host = _w184LastHost ?? _w184JoinOffer;
        if (host is not { } w) { Console.WriteLine($"WO189-SAVE no snapshot owed ({why}): no host save is known on this machine"); return; }
        lock (_w184Lock)
        {
            if (_w184Want is { } o) { o.NextTryMs = 0; return; }
            _w184Want = new W184Want { W = w, Src = _w184LastHostSrc, SinceMs = Environment.TickCount64, Why = why, Purpose = purpose };
        }
        Console.WriteLine($"WO189-SAVE a snapshot is owed ({why}), paired with host save seq={w.Seq} -- the gate takes it when the engine can");
        _ = Wo184KickAsync();
    }

    // ================================================================ B: the quest world layers

    private readonly Wo189Rules.ProfileCapture _w189Capture = new();
    private readonly ConcurrentDictionary<string, byte> _w189Mirrored = new(StringComparer.Ordinal);
    private long _w189ProfilesSent, _w189ProfilesIn, _w189ProfilesApplied;

    private void Wo189OnProfileLine(string line)
    {
        var d = _w189Capture.Feed(line, Wo189Rules.QuestProfiles);
        if (d is null) return;
        bool host = W137Host && Wo134Peers().Any();
        if (!W137Host) return;   // the joiner's own lines: its own game's business (the mirror's result is in the DLL's log)
        Console.WriteLine($"WO189-PROFILE host {(d.Op > 0 ? "activated" : "deactivated")} '{d.Name}': {(d.Forward ? (host ? "forwarded to the joiner" : "a quest profile (no partner now)") : d.Why)}");
        if (!d.Forward || !host) return;
        Interlocked.Increment(ref _w189ProfilesSent);
        string text = Wo189Rules.ProfileText(d.Op > 0 ? '+' : '-', d.Name);
        if (text.Length > Protocol.W164TextMax) { Console.WriteLine($"WO189-PROFILE '{d.Name}' not sent: the name is longer than the message"); return; }
        _ = Task.Run(async () => { foreach (byte j in Wo134Peers()) await W164SendAsync(j, Protocol.W164Profile, text); });
    }

    /// <summary>W164 kind 17 in: the host sends layers, the joiner asks for the catch-up.</summary>
    private async Task Wo189OnProfileInAsync(byte src, string text)
    {
        if (!Wo189Rules.TryParseProfileText(text, out char op, out string name)) return;
        Interlocked.Increment(ref _w189ProfilesIn);
        if (op == '?')
        {
            if (!W137Host) return;
            var active = await _combat.Wo189ActiveProfilesAsync();
            if (active is null) { Console.WriteLine("WO189-PROFILE catch-up asked: the host's active profiles could not be read (the DLL)"); return; }
            var list = Wo189Rules.CatchUp(active, Wo189Rules.QuestProfiles);
            Console.WriteLine($"WO189-PROFILE catch-up for player {src}: {list.Count} quest profile(s) active here of {active.Count} [{string.Join(",", list.Take(20))}{(list.Count > 20 ? ",..." : "")}]");
            foreach (var n in list) { string t = Wo189Rules.ProfileText('=', n); if (t.Length <= Protocol.W164TextMax) await W164SendAsync(src, Protocol.W164Profile, t); }
            return;
        }
        if (!W137Joiner) return;
        if (!_w189Mirror) { Console.WriteLine($"WO189-PROFILE host '{op}{name}' -- not applied (mp_profile_mirror is off)"); return; }
        if (!Wo189Rules.IsQuestProfile(name, Wo189Rules.QuestProfiles)) { Console.WriteLine($"WO189-PROFILE host '{op}{name}' refused: not a quest profile"); return; }
        if (op != '=') Wo210ForgetOwnProfiles();   // WO-210 H.6: a live change -- the next catch-up reads this player's profiles afresh
        else if (await Wo210ProfileSkipAsync(op, name)) return;
        byte? r = await _combat.Wo189ProfileAsync(op == '-' ? (byte)3 : (byte)2, name);
        string res = r switch
        {
            1 => op == '-' ? "released (the handle this mod took)" : "enabled (the handle kept)",
            0 => op == '-' ? "not held by this mod (its own quest's, or never enabled here): left alone" : "already active here (left to its own quest)",
            4 => "no such profile in this level",
            2 => "not armed on this build",
            5 => "refused (not the joiner, or the switch is off in the DLL)",
            null => "the DLL did not answer",
            _ => "faulted",
        };
        if (r == 1) { Interlocked.Increment(ref _w189ProfilesApplied); if (op == '-') _w189Mirrored.TryRemove(name, out _); else _w189Mirrored[name] = 1; }
        Console.WriteLine($"WO189-PROFILE host '{op}{name}' on the joiner: {res}");
    }

    /// <summary>B3: the join's Ready -- the host's active quest profiles asked for (a few seconds after, the world settled).</summary>
    private void Wo189OnJoined()
    {
        if (!W137Joiner || !_w189Mirror) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(5_000);
            if (_joinedWorld) await W164SendAsync(Protocol.JoinTargetHost, Protocol.W164Profile, "?");
        });
    }

    // ================================================================ C + D: cutscenes and scene positioning

    private readonly Wo189Rules.CutsceneWatch _w189Cut = new();
    private readonly Wo189Rules.PositioningWatch _w189Pos = new();
    private (string Dialogue, List<string> Npcs, long AtMs) _w189Dialogue = ("", [], 0);
    private ulong _w189ReleasedCfg;
    private string _w189ConfirmName = "";
    private long _w189ConfirmUntil;
    private readonly List<string> _w189ConfirmStages = [];
    private long _w189CutStuck, _w189CutReleased, _w189CutInterrupts, _w189CutConfirmed, _w189PosActs, _w189FfStopped;
    private static readonly Regex W189Souls = new(@"^New dialogue '(?<name>[^ ']+) \([^']*' is starting\. Params: souls = '(?<souls>[^']*)'", RegexOptions.Compiled);

    /// <summary>The engine lines (Wo144OnEngineLine) -- never consumes.</summary>
    private void Wo189OnEngineLine(string line)
    {
        if (line.StartsWith("[Info] C_GameProfileManager: ", StringComparison.Ordinal) || line.StartsWith("GameProfileManager: ", StringComparison.Ordinal))
        {
            Wo189OnProfileLine(line);
            return;
        }
        if (Wo189Rules.TryParseInitialized(line, out string type, out string name, out string holder))
        {
            _w189Cut.Initialized(type, name, holder, Environment.TickCount64);
            return;
        }
        var m = W189Souls.Match(line);
        if (m.Success && Wo184Rules.HasDude(m.Groups["souls"].Value))
        {
            var npcs = m.Groups["souls"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim()).Select(s => s.StartsWith("Ex: ", StringComparison.Ordinal) ? s[4..] : s)
                        .Where(s => s.Length > 0 && s != "Dude").ToList();
            _w189Dialogue = (m.Groups["name"].Value, npcs, Environment.TickCount64);
        }
    }

    /// <summary>WO-151's stage feed (every CutscenePlayer:: stage).</summary>
    private void Wo189OnSceneStage(string stage, string type, string name)
    {
        _w189Cut.Stage(stage, name);
        if (_w189ConfirmName.Length > 0 && name == _w189ConfirmName)
        {
            lock (_w189ConfirmStages) _w189ConfirmStages.Add(stage);
            if (stage is "ReleaseScene" or "Interrupt")
            {
                Interlocked.Increment(ref _w189CutConfirmed);
                Console.WriteLine($"WO189-CUTSCENE '{name}' released the normal way: {string.Join(" -> ", Wo189ConfirmStagesCopy())} (the quest's barrier input is no longer waited for)");
                _w189ConfirmName = "";
            }
        }
    }

    private List<string> Wo189ConfirmStagesCopy() { lock (_w189ConfirmStages) return [.. _w189ConfirmStages]; }

    /// <summary>WO-167's InteractiveSceneManager stages (enqueue, end, interrupt).</summary>
    private void Wo189OnIsmStage(string stage)
    {
        if (stage == "IsmEnqueue") _w189Pos.Enqueued(Environment.TickCount64);
        else if (stage is "IsmEnd" or "IsmInterrupt") _w189Pos.Ended();
    }

    private void Wo189OnLoad()
    {
        _w189Cut.Clear();
        _w189Pos.Reset();
        _w189ConfirmName = "";
        _w189Pending.Reset();
        _w189Capture.GameplayStarted();
        Wo210ForgetOwnProfiles();
        if (!_w189Mirrored.IsEmpty)
        {
            Console.WriteLine($"WO189-PROFILE a load: {_w189Mirrored.Count} mirrored profile(s) forgotten (the loaded save sets its own layers; the catch-up asks again at the next join)");
            _w189Mirrored.Clear();
        }
        _ = Task.Run(async () => { try { await _combat.Wo189ProfileAsync(5, ""); } catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException) { } });
    }

    private void Wo189OnLoadStarted() => _w189Capture.Loading();

    /// <summary>The copies of the scene the joiner is in: the last conversation's NPCs that are the host's copies.</summary>
    private List<uint> Wo189SceneCopies(out string names)
    {
        var d = _w189Dialogue;
        var eids = new List<uint>();
        var nm = new List<string>();
        if (Environment.TickCount64 - d.AtMs < 30 * 60_000)
            foreach (var n in d.Npcs)
                if (_npcEntityIds.TryGetValue(n, out uint e) && e != 0) { eids.Add(e); nm.Add(n); }
        names = nm.Count > 0 ? string.Join(",", nm) : "-";
        return eids;
    }

    /// <summary>C2 / D: the copies' fast-forward stopped (the engine's own per-NPC call); none known = every copy within 40 m.</summary>
    private async Task<string> Wo189StopCopiesAsync()
    {
        var eids = Wo189SceneCopies(out string names);
        float maxD = 0;
        if (eids.Count == 0) { eids = _npcEntityIds.Values.Where(e => e != 0).Distinct().Take(60).ToList(); maxD = 40f; names = $"every copy within {maxD:F0} m"; }
        if (eids.Count == 0) return "no copies known";
        byte[]? r = null;
        try { r = await _combat.Wo189StopFastForwardAsync(eids, maxD); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException) { }
        if (r is null) return $"{names}: the DLL did not answer";
        int stopped = r.Count(b => b == 1), far = r.Count(b => b == 2), none = r.Count(b => b == 0), refused = r.Count(b => b == 5);
        Interlocked.Add(ref _w189FfStopped, stopped);
        return $"{names}: stopped={stopped} far={far} no_npc={none} refused={refused}";
    }

    private int _w189CutBusy;

    private async Task Wo189CutsceneTickAsync(long now)
    {
        if (!W137Joiner) return;
        if (_w189ConfirmName.Length > 0 && now > _w189ConfirmUntil)
        {
            string nm = _w189ConfirmName;
            _w189ConfirmName = "";
            var cs = await _combat.Wo189CutsceneAsync();
            if (cs is { Active: true } a && a.Cfg == _w189ReleasedCfg && !Wo189ConfirmStagesCopy().Contains("ReleaseScene"))
            {
                string seen = string.Join(" -> ", Wo189ConfirmStagesCopy());
                Console.WriteLine($"WO189-CUTSCENE '{nm}' did not follow its normal sequence within {Wo189Rules.ReleaseConfirmMs / 1000} s (stages seen: {(seen.Length > 0 ? seen : "none")}) -- the last resort: the player's Interrupt");
                byte? ir = await _combat.Wo189CutsceneReleaseAsync(2, a.Cfg);
                if (ir == 1) Interlocked.Increment(ref _w189CutInterrupts);
                Console.WriteLine($"WO189-CUTSCENE Interrupt result={(ir?.ToString() ?? "no answer")}");
            }
            else Console.WriteLine($"WO189-CUTSCENE '{nm}': the player moved on ({string.Join(" -> ", Wo189ConfirmStagesCopy())})");
        }
        if (!_w189Cut.Due(now)) return;
        if (Interlocked.CompareExchange(ref _w189CutBusy, 1, 0) != 0) return;
        try
        {
            string name = _w189Cut.Name, holder = _w189Cut.Holder;
            var st = await _combat.Wo189CutsceneAsync();
            string player = st is null ? "the player not read" : $"active={On(st.Value.Active)} in_progress={On(st.Value.InProgress)}";
            if (st is { InProgress: true }) { Console.WriteLine($"WO189-CUTSCENE '{name}' initialized {Wo189Rules.Secs(now - _w189Cut.SinceMs)} s ago but the player says it is in progress -- not stuck"); _w189Cut.Clear(); return; }
            Interlocked.Increment(ref _w189CutStuck);
            Console.WriteLine($"WO189-CUTSCENE stuck name={name} holder={(holder.Length > 0 ? holder : "-")} type={_w189Cut.Type} since={Wo189Rules.Secs(now - _w189Cut.SinceMs)} s ({player})");
            await Wo189BarrierReadAsync(holder);
            if (!_w189Release) { Console.WriteLine("WO189-CUTSCENE not released: mp_cutscene_release is off"); return; }
            if (st is not { Active: true } s) { Console.WriteLine("WO189-CUTSCENE not released: the player has no active configuration (or was not read)"); return; }
            string ff = await Wo189StopCopiesAsync();
            Console.WriteLine($"WO189-CUTSCENE its scene's copies' fast-forward: {ff}");
            byte? r = await _combat.Wo189CutsceneReleaseAsync(1, s.Cfg);
            if (r == 1)
            {
                Interlocked.Increment(ref _w189CutReleased);
                _w189Cut.Released(now);
                _w189ReleasedCfg = s.Cfg;
                lock (_w189ConfirmStages) _w189ConfirmStages.Clear();
                _w189ConfirmName = name;
                _w189ConfirmUntil = Environment.TickCount64 + Wo189Rules.ReleaseConfirmMs;
                Console.WriteLine($"WO189-CUTSCENE released '{name}' the game's own way (C_CutscenePlayer::PlayCutscene, the call its barrier's PlayCutscene input makes) -- its normal sequence is awaited {Wo189Rules.ReleaseConfirmMs / 1000} s");
            }
            else Console.WriteLine($"WO189-CUTSCENE '{name}' NOT released: result={(r?.ToString() ?? "no answer")} (0 changed/in progress, 2 not armed, 3 fault, 5 refused here)");
        }
        catch (Exception ex) { Console.WriteLine($"WO189-CUTSCENE tick failed: {ex.GetType().Name}: {ex.Message}"); }
        finally { Volatile.Write(ref _w189CutBusy, 0); }
    }

    /// <summary>F: the barrier's two waits, through the concept reader (the values land in the native log).</summary>
    private async Task Wo189BarrierReadAsync(string holder)
    {
        string quest = Wo189Rules.QuestOfDialogue(_w189Dialogue.Dialogue);
        if (Wo189Rules.BarrierPaths(holder, quest) is not { } p) { Console.WriteLine($"WO189-BARRIER no barrier read: holder '{holder}' {(quest.Length == 0 ? "and no quest known from a conversation" : "is not a barrier's")}"); return; }
        List<CombatPipe.QuestStateRead>? r = null;
        try { r = await _combat.Wo137ReadStatesAsync([p.StopCrime, p.Streaming]); }   // WO-137's quest State reader (read only)
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException) { }
        string V(int i) => r is null || r.Count <= i ? "no answer" : !r[i].Found ? "not found" : !r[i].Ok ? "unreadable" : r[i].Val.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string sh = r is { Count: > 1 } && r[1] is { Found: true, Ok: true } x ? Wo189Rules.StreamingName(x.Val) : V(1);
        string branch = r is { Count: > 1 } && r[0] is { Found: true, Ok: true } w && r[1] is { Found: true, Ok: true } z
                        ? (w.Val != 0 ? "the stopcrime step: the crime reactors (the copies) never answered" : z.Val == 4 ? "the same-value Unstreamed set (no OnUnstreamed)" : "neither wait is visible now")
                        : "not decidable (a State was not read)";
        Console.WriteLine($"WO189-BARRIER {p.StopCrime}={V(0)}; {p.Streaming}={sh} -- {branch}");
    }

    private int _w189PosBusy;

    private async Task Wo189PositioningTickAsync(long now)
    {
        if (!W137Joiner || !_w189PosBound || _w189Pos.Open == 0) return;
        bool cutscenePlays = _w151Scenes.Values.Any(s => s.PlayAtMs > 0 && s.EndAtMs == 0);
        bool? waits = null;
        if (now - _w189Pos.OldestMs >= Wo189Rules.PositioningBoundMs)
        {
            var r = await Wo189ReadyAsync();
            if (r is { PosArmed: true }) waits = r.PositioningWaiting;
        }
        if (!_w189Pos.Due(now, _w184Clock.Running, cutscenePlays, waits)) return;
        if (Interlocked.CompareExchange(ref _w189PosBusy, 1, 0) != 0) return;
        try
        {
            Interlocked.Increment(ref _w189PosActs);
            string ff = await Wo189StopCopiesAsync();
            Console.WriteLine($"WO189-POSITIONING a scene has been positioning {Wo189Rules.Secs(now - _w189Pos.OldestMs)} s (bound {Wo189Rules.PositioningBoundMs / 1000} s; manager {(waits is null ? "not read" : waits.Value ? "waits for its fast-forward set" : "idle")}) "
                              + $"-- the copies stay where the host's stream puts them; their fast-forward stopped (the engine's own per-NPC call, never a save request): {ff}");
        }
        finally { Volatile.Write(ref _w189PosBusy, 0); }
    }

    // ================================================================ the tick (1 s, the WO-151 loop through Wo167TickAsync)

    private async Task Wo189TickAsync()
    {
        long now = Environment.TickCount64;
        await Wo189PushCfgAsync(now);
        await Wo189WatchdogAsync(now);
        await Wo192TickAsync(now);
        await Wo189CutsceneTickAsync(now);
        await Wo189PositioningTickAsync(now);
    }

    // ================================================================ A5: mp_save_status

    private string Wo189StatusText(long now)
    {
        string pend = _w189Pending.SinceMs > 0 ? $"pending {Wo189Rules.Secs(now - _w189Pending.SinceMs)} s" : "none";
        return $"engine_pending={pend} a1_cancels={Interlocked.Read(ref _w189A1Cancels)} watchdog={Interlocked.Read(ref _w189Watchdog)} "
               + $"watchdog_cancels={Interlocked.Read(ref _w189WatchdogCancels)} {Wo192StatusText(now)} "
               + $"cutscene_stuck={Interlocked.Read(ref _w189CutStuck)} released={Interlocked.Read(ref _w189CutReleased)} confirmed={Interlocked.Read(ref _w189CutConfirmed)} "
               + $"interrupts={Interlocked.Read(ref _w189CutInterrupts)} positioning_bounds={Interlocked.Read(ref _w189PosActs)} ff_stopped={Interlocked.Read(ref _w189FfStopped)} "
               + $"profiles_sent={Interlocked.Read(ref _w189ProfilesSent)} profiles_in={Interlocked.Read(ref _w189ProfilesIn)} profiles_applied={Interlocked.Read(ref _w189ProfilesApplied)} mirrored={_w189Mirrored.Count} "
               + $"mirror={On(_w189Mirror)} release={On(_w189Release)} posbound={On(_w189PosBound)}";
    }

    /// <summary>A5: the short line on screen's WO-189 half.</summary>
    private string Wo189StatusShort(long now)
    {
        if (W137Host) return Wo192StatusShort(now);
        return _w189Pending.SinceMs > 0 ? $" A save has been pending {Wo189Rules.Secs(now - _w189Pending.SinceMs)} s." : "";
    }
}
