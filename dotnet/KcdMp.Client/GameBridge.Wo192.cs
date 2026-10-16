// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

// WO-192 -- saves, simplified (Wo192Rules has the pure rules; docs/WO-192-findings.md the evidence). The field (0.60.9): after each in-game
// cutscene WO-189's "a scene is still positioning the host's copies" refused every joiner snapshot for minutes, the ESC and exit snapshots
// included, and a rejoin restored a snapshot about 16 minutes old. This WO subtracts:
//   1  the joiner's snapshot: two hard blocks only (a save pending here, not in the host's world) and the engine's own "not now"; a scene is
//      a log line (WO192-SAVE during a scene). Gone: WO-189 A3's gate, the ESC menu's early snapshot, mp_joiner_autosnap, mp_save_reminder.
//   2  the host's autosave every 30 minutes of play, the schnapps way (mp_host_autosave, mp_host_autosave_min): never pushed, tried again
//      every 60 s while the game can't save; any host save starts the next interval. WO192-AUTOSAVE done|waiting why=...
//   4b the joiner's wake after a shared sleep or wait waits behind the game's fader for the host's "woke" (at most 15 s) (mp_wake_hold)
//   4c a station's save lock (the trough's 'waterTub') held 15 s is the game's interaction that never ended: ended the game's own way and
//      the lock released (mp_station_release)
// Log tags: WO192-SAVE, WO192-AUTOSAVE, WO192-CFG, WO192-WAKE, WO192-STATION.
public partial class GameBridge
{
    // ================================================================ switches (the mod's w192_cfg)

    private volatile bool _w192Autosave = Wo192Rules.DefaultHostAutosave, _w192WakeHold = true, _w192StationRelease = true;
    private long _w192AutosaveMs = (long)(Wo192Rules.DefaultAutosaveMinutes * 60_000);

    private void Wo192OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w192_cfg": Wo192OnCfg(arg); return;
            case "w192_test": if (W192Test) _ = Wo192TestAsync(arg); return;
        }
    }

    // Harness-only (KCDMP_TEST_W192=1, never set by the launcher): what a solo machine cannot stage the game's way -- a scene positioning the
    // host's copies (the positioning watch's count) and the host's "woke" after this joiner's own wake (the hold, with no shared skip).
    private static readonly bool W192Test = Environment.GetEnvironmentVariable("KCDMP_TEST_W192") == "1";

    private async Task Wo192TestAsync(string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string verb = f.Length > 0 ? f[0] : "";
        long now = Environment.TickCount64;
        switch (verb)
        {
            case "posopen": _w189Pos.Enqueued(now); Console.WriteLine($"WO192-TEST posopen: {_w189Pos.Open} scene(s) counted open"); break;
            case "posclose": _w189Pos.Ended(); Console.WriteLine($"WO192-TEST posclose: {_w189Pos.Open} scene(s) counted open"); break;
            case "wake":   // wake <host_after_ms>: this joiner woke; the host's "woke" follows after the given time (none = never: the 15 s bound)
                Volatile.Write(ref _w192HostWokeMs, 0);
                Console.WriteLine($"WO192-TEST wake: the host's woke after {(f.Length > 1 ? f[1] + " ms" : "never")}");
                await Wo192OnLocalWokeAsync(force: true);
                if (f.Length > 1 && int.TryParse(f[1], out int ms)) { await Task.Delay(ms); Wo192OnHostWoke(); }
                break;
            case "status": Console.WriteLine($"WO192-TEST status: {Wo192StatusText(now)}"); break;
        }
    }

    /// <summary>"autosave=on|off minutes=&lt;m&gt; wakehold=on|off station=on|off".</summary>
    private void Wo192OnCfg(string? arg)
    {
        foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq <= 0) continue;
            string k = kv[..eq], v = kv[(eq + 1)..];
            switch (k)
            {
                case "autosave": _w192Autosave = v == "on"; break;
                case "minutes" when Wo192Rules.ParseMinutes(v) is double m && m > 0: Interlocked.Exchange(ref _w192AutosaveMs, (long)(m * 60_000)); break;
                case "wakehold": _w192WakeHold = v == "on"; break;
                case "station": _w192StationRelease = v == "on"; break;
            }
        }
        Console.WriteLine($"WO192-CFG host_autosave={On(_w192Autosave)} every={Wo189Rules.Secs(Interlocked.Read(ref _w192AutosaveMs) / 60)} min of play wake_hold={On(_w192WakeHold)} station_release={On(_w192StationRelease)}");
    }

    private void Wo192OnConnect() => _ = ExecLuaAsync("if KCD2MP_W192Cfg then KCD2MP_W192Cfg() end");

    // ================================================================ 1: a snapshot asked during a scene (a log line, never a block)

    private string _w192LastNote = "";
    private long _w192SceneAsks;

    private async Task Wo192SceneNoteAsync(string purpose, uint seq)
    {
        if (!W137Joiner) return;
        var r = await Wo189ReadyAsync();
        string note = Wo192Rules.SceneNote(r?.PositioningWaiting == true, _w189Pos.Open, r is { Armed: true } x ? x.Ready : null, r?.NotReady ?? "", _w189Cut.StuckName);
        if (note.Length == 0) { _w192LastNote = ""; return; }
        Interlocked.Increment(ref _w192SceneAsks);
        Console.WriteLine(Wo192Rules.SceneLine(purpose, seq, note));
        _w192LastNote = note;
    }

    // ================================================================ 2: the host's autosave

    private readonly Wo192Rules.Autosave _w192Auto = new();
    private volatile bool _w192MenuOpen;
    private int _w192AskBusy;
    private (string File, long AtMs) _w192LastFile = ("", 0);

    /// <summary>The DLL's 0xAD (the ESC menu's edges): only a flag for the autosave's "a menu" (no snapshot starts here any more).</summary>
    private void Wo192OnMenu(bool opened) => _w192MenuOpen = opened;

    private async Task Wo192TickAsync(long now)
    {
        await Wo192StationTickAsync(now);

        bool session = W137Host && Wo134Peers().Any();
        bool inWorld = _where is not (GameWhere.Menu or GameWhere.Loading);
        _w192Auto.Tick(now, session && _w192Autosave, inWorld);
        if (_w192Auto.Asked && now - _w192Auto.AskedAtMs > 120_000)
        {
            _w192Auto.Wait(now, "the save asked 2 minutes ago gave no result");
            Console.WriteLine(Wo192Rules.WaitLine(_w192Auto.LastWait, _w192Auto.PlayMs));
        }
        long interval = Interlocked.Read(ref _w192AutosaveMs);
        if (!_w192Auto.Due(now, interval)) return;
        if (Interlocked.CompareExchange(ref _w192AskBusy, 1, 0) != 0) return;
        try
        {
            string why = !inWorld ? "not in the world (a menu or a load)"
                       : _w192MenuOpen ? "the game's menu is open"
                       : Volatile.Read(ref _w178SaveBusy) != 0 ? "a save is already waiting for its file"
                       : "";
            if (why.Length == 0)
            {
                var st = await Wo184EngineStateAsync();
                if (st.Known && st.Pending) why = "a save is pending";
                else if (st.Known && !st.CanQuick) why = "the engine's CanSave says no";
            }
            if (why.Length == 0)
            {
                var (asked, w) = Wo192Rules.ParseAskAnswer(await AskModAsync("KCD2MP_W192Autosave", 3_000));
                if (asked) { _w192Auto.Ask(now); Console.WriteLine($"WO192-AUTOSAVE asked after {Wo189Rules.Secs(_w192Auto.PlayMs / 60)} min of play -- the schnapps way (no item used)"); return; }
                why = w;
            }
            _w192Auto.Wait(now, why);
            Console.WriteLine(Wo192Rules.WaitLine(why, _w192Auto.PlayMs));
        }
        finally { Volatile.Write(ref _w192AskBusy, 0); }
    }

    /// <summary>Wo178SaveAsync's result for the autosave's word: saved -> done; anything else -> wait 60 s.</summary>
    private void Wo192OnSaveResult(string result, string file)
    {
        long now = Environment.TickCount64;
        if (result == "saved")
        {
            Console.WriteLine(Wo192Rules.DoneLine(file, _w192Auto.PlayMs));
            _w192Auto.Saved(wasAutosave: true);
            _w192LastFile = (file, now);
            return;
        }
        _w192Auto.Wait(now, $"the engine's save answered '{result}'");
        Console.WriteLine(Wo192Rules.WaitLine(_w192Auto.LastWait, _w192Auto.PlayMs));
    }

    /// <summary>The host's folder watch announced a save (OnWorldSaveObservedAsync): any host save starts the next interval.</summary>
    private void Wo192OnHostSaved(string display)
    {
        if (!_w192Auto.InSession) return;
        if (_w192Auto.Asked) return;   // the autosave's own file: its result line restarts the clock
        if (display == _w192LastFile.File && Environment.TickCount64 - _w192LastFile.AtMs < 120_000) return;
        Console.WriteLine($"WO192-AUTOSAVE a host save ({display}) after {Wo189Rules.Secs(_w192Auto.PlayMs / 60)} min of play -- the next autosave in the full interval");
        _w192Auto.Saved(wasAutosave: false);
    }

    // ================================================================ 4b: the joiner's wake waits for the host

    private long _w192HostWokeMs;          // the host's "woke" for this shared skip (0 = not yet)
    private long _w192HoldSince;           // the hold's start (0 = none)
    private volatile bool _w192WakeHolding;
    private long _w192WakeHolds, _w192WakeBounds;

    /// <summary>This game's own skip began (the DLL's edge): a new shared skip, the host has not woken in it yet.</summary>
    private void Wo192OnSkipBegan() => Volatile.Write(ref _w192HostWokeMs, 0);

    /// <summary>The host's "woke" reached this joiner (Wo140OnCancelAsync): a held wake ends a moment later.</summary>
    private void Wo192OnHostWoke()
    {
        if (_isDamageAuthority) return;
        Volatile.Write(ref _w192HostWokeMs, Math.Max(Environment.TickCount64, 1));
        if (_w192WakeHolding) _ = Task.Run(async () => { await Task.Delay((int)Wo192Rules.WakeSettleMs); await Wo192ReleaseWakeAsync("the host woke"); });
    }

    /// <summary>
    /// This joiner's own shared skip ended (Wo140OnLocalEndedAsync) before the host's: the wake waits behind the game's own fader until the
    /// host's "woke" (and a moment for the clock to meet it), at most 15 s -- the world's jump is never seen.
    /// </summary>
    private async Task Wo192OnLocalWokeAsync(bool force = false)
    {
        if (_isDamageAuthority || (!W140SharedSkip && !force)) return;
        long now = Environment.TickCount64;
        if (Volatile.Read(ref _w192HostWokeMs) != 0) { Console.WriteLine("WO192-WAKE the host woke first -- no hold"); return; }
        if (!_w192WakeHold) { Console.WriteLine("WO192-WAKE this player woke before the host -- not held (mp_wake_hold is off)"); return; }
        var fr = await _combat.Wo163Async(Protocol.Wo179OpFade, [1, .. BitConverter.GetBytes(0.3f)]);
        if (fr is not { Ok: true }) { Console.WriteLine("WO192-WAKE this player woke before the host -- the game's fader refused: not held"); return; }
        Interlocked.Increment(ref _w192WakeHolds);
        Volatile.Write(ref _w192HoldSince, now);
        _w192WakeHolding = true;
        Console.WriteLine($"WO192-WAKE this player woke before the host -- held behind the game's fader until the host's 'woke' (at most {Wo192Rules.WakeHoldMaxMs / 1000} s)");
        _ = Task.Run(async () =>
        {
            await Task.Delay((int)Wo192Rules.WakeHoldMaxMs);
            if (_w192WakeHolding && Volatile.Read(ref _w192HoldSince) == now) { Interlocked.Increment(ref _w192WakeBounds); await Wo192ReleaseWakeAsync("the 15 s bound"); }
        });
    }

    private async Task Wo192ReleaseWakeAsync(string why)
    {
        if (!_w192WakeHolding) return;
        _w192WakeHolding = false;
        long held = Environment.TickCount64 - Volatile.Read(ref _w192HoldSince);
        try { await _combat.Wo163Async(Protocol.Wo179OpFade, [0, .. BitConverter.GetBytes(0.5f)]); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException) { }
        Console.WriteLine($"WO192-WAKE released after {Wo189Rules.Secs(held)} s ({why})");
        _ = ExecLuaAsync("if KCD2MP_ReportWorldTime then KCD2MP_ReportWorldTime() end");   // the pin: the clock meets the host's
    }

    // ================================================================ 4c: a station's save lock

    private readonly Wo192Rules.StationLockWatch _w192Locks = new();
    private long _w192StationReleases;

    /// <summary>The engine lines (Wo144OnEngineLine): the game's own save-lock lines -- never consumes.</summary>
    private void Wo192OnEngineLine(string line)
    {
        if (line.StartsWith(Wo192Rules.AddLockPrefix, StringComparison.Ordinal) || line.StartsWith(Wo192Rules.RemoveLockPrefix, StringComparison.Ordinal))
            _w192Locks.Line(line, Environment.TickCount64);
    }

    private void Wo192OnLoad() => _w192Locks.Clear();

    private async Task Wo192StationTickAsync(long now)
    {
        foreach (var (name, held) in _w192Locks.Due(now))
        {
            if (!_w192StationRelease) { Console.WriteLine($"WO192-STATION the game's lock '{name}' held {Wo189Rules.Secs(held)} s -- not released (mp_station_release is off)"); continue; }
            Interlocked.Increment(ref _w192StationReleases);
            string r = await AskModAsync("KCD2MP_W192ReleaseStation", 3_000, "'" + name + "'");
            Console.WriteLine($"WO192-STATION the game's lock '{name}' held {Wo189Rules.Secs(held)} s (a wash takes about 5 s): its interaction never ended -- ended the game's own way: {r}");
        }
    }

    // ================================================================ status (mp_save_status)

    private string Wo192StatusText(long now)
    {
        long interval = Interlocked.Read(ref _w192AutosaveMs);
        string auto = !_w192Autosave ? "off" : !W137Host ? "host only" : !_w192Auto.InSession ? "no session"
                    : $"in {Wo189Rules.Secs(_w192Auto.LeftMs(interval))} s of play (done {_w192Auto.Done}, waits {_w192Auto.Waits}{(_w192Auto.LastWait.Length > 0 ? ", last: " + _w192Auto.LastWait : "")})";
        return $"host_autosave={auto} scene_asks={Interlocked.Read(ref _w192SceneAsks)} wake_holds={Interlocked.Read(ref _w192WakeHolds)} wake_bounds={Interlocked.Read(ref _w192WakeBounds)} station_releases={Interlocked.Read(ref _w192StationReleases)}";
    }

    private string Wo192StatusShort(long now)
    {
        if (!_w192Autosave || !_w192Auto.InSession) return "";
        long left = _w192Auto.LeftMs(Interlocked.Read(ref _w192AutosaveMs));
        return left <= 0 ? $" Autosave waiting ({(_w192Auto.LastWait.Length > 0 ? _w192Auto.LastWait : "asked")})." : $" Next autosave in {Math.Max(1, left / 60_000)} min of play.";
    }
}
