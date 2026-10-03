// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

// WO-154 Phase 4.2: a joiner's load is given up only on a responsive main menu, never while the game is busy, and a
// placed world file is deleted only when no load can be reading it (docs/WO-154-findings.md).
//
// The field (another tester): the world transfer was perfect, then the joiner's main thread hung 50+ s at the load.
// The agent waited 20 s for kcd.log's "Loading saved game" -- a line the engine wrote late -- gave up, and deleted the
// world file while the game may still have been reading it. Now the agent asks the game where it is (a tokened Lua
// round trip: a load that holds the main thread answers nothing) and waits while it is busy or loading; three answers
// in a row from a menu (or, before the engine took the load, from the player's own world) abort; a world answer after
// the engine took the load is the load's end, its log line late. mp_join_patient on|off (default on; off = 0.44.0).
public partial class GameBridge
{
    private volatile bool _w154JoinPatient = true;
    private long _w154JoinWaits, _w154JoinWaitAborts, _w154JoinWaitWorlds, _w154JoinDeferredRemovals;

    /// <summary>Where the game is now: menu | world | loading, "busy" when the console did not answer in time.</summary>
    private async Task<string> Wo154WhereNowAsync(int timeoutMs = 4000)
    {
        string r = await AskModAsync("KCD2MP_W154Where", timeoutMs);
        return r is "menu" or "world" or "loading" ? r : r == "timeout" ? "busy" : "unknown";
    }

    /// <summary>
    /// The engine did not show the load's next step in time (<paramref name="stage"/>: "accept" = the load command,
    /// "gameplay" = the end of the load). True: go on (the step came, or the game answers from the new world);
    /// false: abort (a failed load, a responsive menu, or a game that stayed silent for 15 min).
    /// </summary>
    private async Task<bool> Wo154WaitLoadAsync(JoinerJoin j, string stage)
    {
        Interlocked.Increment(ref _w154JoinWaits);
        var watch = new Wo154Rules.JoinLoadWatch();
        var t0 = DateTime.UtcNow;
        string told = "";
        bool Done() => (stage == "accept" && j.LoadStarted!.Task.IsCompleted) || j.GameplayStarted!.Task.IsCompleted;
        Console.WriteLine($"MP-JOIN joiner: join 0x{j.JoinId:x8}: no sign of the load's {(stage == "accept" ? "start" : "end")} yet -- asking the game where it is; it is never given up while it is busy (mp_join_patient)");
        while (true)
        {
            if (Done()) return true;
            if (j.LoadFailed!.Task.IsCompleted) return false;
            string w = await Wo154WhereNowAsync();
            if (Done()) return true;
            double waited = (DateTime.UtcNow - t0).TotalSeconds;
            var v = watch.Feed(w, waited, loadAccepted: j.LoadStarted!.Task.IsCompleted);
            if (w != told)
            {
                told = w;
                Console.WriteLine(FormattableString.Invariant($"MP-JOIN joiner: join 0x{j.JoinId:x8}: the game is {(w == "busy" ? "busy (no answer: a load holds it)" : w)} after {waited:F0} s -- {(v == Wo154Rules.JoinLoadWatch.Verdict.Wait ? "waiting" : v.ToString())}"));
                SetJoinUi("loading", w is "busy" or "loading" ? "Loading your host's world (your game is busy, please wait)..." : "Loading your host's world...");
            }
            switch (v)
            {
                case Wo154Rules.JoinLoadWatch.Verdict.InWorld:
                    Interlocked.Increment(ref _w154JoinWaitWorlds);
                    Console.WriteLine($"MP-JOIN joiner: join 0x{j.JoinId:x8}: the game answers from the loaded world -- the load went through (its log line came late or not at all)");
                    if (j.GameplayUtc == default) j.GameplayUtc = DateTime.UtcNow;
                    j.GameplayStarted.TrySetResult(true);
                    return true;
                case Wo154Rules.JoinLoadWatch.Verdict.Abort:
                    Interlocked.Increment(ref _w154JoinWaitAborts);
                    Console.WriteLine($"MP-JOIN joiner: join 0x{j.JoinId:x8}: the game answers from {(w == "menu" ? "the main menu" : "its own world")}, not loading ({Wo154Rules.JoinLoadWatch.IdleAnswersToAbort} times) -- the load never took");
                    return false;
                case Wo154Rules.JoinLoadWatch.Verdict.GaveUpBusy:
                    Interlocked.Increment(ref _w154JoinWaitAborts);
                    Console.WriteLine(FormattableString.Invariant($"MP-JOIN joiner: join 0x{j.JoinId:x8}: the game gave no usable answer for {Wo154Rules.JoinLoadWatch.MaxWaitS:F0} s -- giving the join up (its world file stays until no load can read it)"));
                    return false;
            }
            await Task.Delay(2000);
        }
    }

    /// <summary>
    /// A placed world file the join is done with: deleted now when no load can be reading it, else when the game answers
    /// from a menu or a world (checked every 3 s, 15 min at most; the next join's sweep removes one left behind).
    /// </summary>
    private bool Wo154RemovePlacedLater(JoinerJoin j, string path)
    {
        bool commanded = j.LoadCmdUtc != default;
        bool past = (j.GameplayStarted?.Task.IsCompleted ?? false) || (j.LoadFailed?.Task.IsCompleted ?? false);
        if (!_w154JoinPatient || Wo154Rules.PlacedFileMayGo(commanded, past, false, "")) return false;
        Interlocked.Increment(ref _w154JoinDeferredRemovals);
        int pl = j.Playline; string name = j.Name; uint id = j.JoinId;
        Console.WriteLine($"MP-JOIN joiner: join 0x{id:x8}: {SaveDisplay(path)} is kept until no load can be reading it");
        _ = Task.Run(async () =>
        {
            var t0 = DateTime.UtcNow;
            string told = "";
            while ((DateTime.UtcNow - t0).TotalSeconds < Wo154Rules.JoinLoadWatch.MaxWaitS)
            {
                string w = await Wo154WhereNowAsync();
                if (Wo154Rules.PlacedFileMayGo(true, false, false, w))
                {
                    try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) { Console.WriteLine($"MP-JOIN joiner: could not delete {SaveDisplay(path)}: {ex.Message}"); }
                    var after = await _combat.SaveListAsync(1, pl, name);
                    Console.WriteLine($"MP-JOIN joiner: join 0x{id:x8}: removed {SaveDisplay(path)} now that the game answers from {(w == "menu" ? "the main menu" : "a world")}; rescan listed={(after is null ? "?" : On(after.Listed))}");
                    return;
                }
                if (w != told) { told = w; Console.WriteLine($"MP-JOIN joiner: join 0x{id:x8}: {SaveDisplay(path)} still kept -- the game is {w}"); }
                await Task.Delay(3000);
            }
            Console.WriteLine($"MP-JOIN joiner: join 0x{id:x8}: {SaveDisplay(path)} left in place -- the game never answered from a menu or a world (the next join removes it)");
        });
        return true;
    }

    // ---- Phase 4.1: the host's join bar through the engine's hold ----------------------------------------------------
    // The old bar is DrawText rows the mod's 8 ms timer draws; while the engine holds the world no timer runs, and the
    // field's bar was set, updated once and gone for the hold's 77 s. A console call does run during the hold (L1:
    // hud.ShowTutorial pushed then showed and updated 40% -> 70%), so the agent pushes the game's own tutorial panel
    // with the same text (KCD2MP_JoinBarText): on a stage change, at 25% steps of the transfer (3 s apart at most once),
    // and every 15 s while the partner loads; hidden when the join ends. The panel queues and fades on every push
    // (the dice board's lesson): each push flushes the queue first, and pushes stay rare. mp_join_panel on|off.
    private volatile bool _w154JoinPanel = true;
    private string _w154PanelPhase = "";
    private int _w154PanelBucket = -1;
    private long _w154PanelAt, _w154PanelPhaseSince, _w154PanelPushes;
    private readonly object _w154PanelGate = new();

    /// <summary>Host: the panel for this join's stage, pushed when it is due (the 1 s tick and every progress step call it).</summary>
    private void Wo154HostJoinPanel(HostJoin? j)
    {
        if (!_w154JoinPanel || j is not { PausedUtc: not null }) return;
        string phase = j.Phase switch { "saving" => "saving", "sending" => "sending", "waiting-ready" => "loading", _ => "" };
        if (phase == "") return;
        long now = Environment.TickCount64;
        int pct = phase == "sending" ? Math.Clamp(j.SentPct, 0, 100) : phase == "loading" ? 100 : 0;
        int bucket = phase == "sending" ? pct / 25 : -1;
        lock (_w154PanelGate)
        {
            bool newPhase = phase != _w154PanelPhase;
            if (newPhase) _w154PanelPhaseSince = now;
            bool due = newPhase || (bucket != _w154PanelBucket && now - _w154PanelAt >= 3000) || now - _w154PanelAt >= 15000;
            if (!due) return;
            _w154PanelPhase = phase; _w154PanelBucket = bucket; _w154PanelAt = now;
        }
        Interlocked.Increment(ref _w154PanelPushes);
        long stageS = (now - _w154PanelPhaseSince) / 1000;
        _ = ExecLuaAsync(FormattableString.Invariant(
            $"if KCD2MP_W154JoinPanel then KCD2MP_W154JoinPanel(\"{EscapeLua(j.Partner)}\", \"{phase}\", {pct}, {stageS}) end"));
    }

    /// <summary>Host: the join ended (in, cancelled, failed): the panel goes.</summary>
    private void Wo154HostJoinPanelHide(string why)
    {
        lock (_w154PanelGate)
        {
            if (_w154PanelPhase == "") return;
            _w154PanelPhase = ""; _w154PanelBucket = -1;
        }
        _ = ExecLuaAsync($"if KCD2MP_W154JoinPanelHide then KCD2MP_W154JoinPanelHide(\"{EscapeLua(why)}\") end");
    }

    private string Wo154JoinStatsText() => FormattableString.Invariant(
        $"join_patient={(_w154JoinPatient ? "on" : "off")} join_waits={_w154JoinWaits} join_wait_aborts={_w154JoinWaitAborts} join_wait_worlds={_w154JoinWaitWorlds} placed_deferred={_w154JoinDeferredRemovals} join_panel={(_w154JoinPanel ? "on" : "off")} panel_pushes={_w154PanelPushes}");
}
