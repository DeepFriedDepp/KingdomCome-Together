// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-184 -- 0.60.0: the joiner's progress is saved, every time (Wo184Rules has the pure rules; docs/WO-184-findings.md the evidence).
//
//   the owner    every host save (WorldSaved in) asks the joiner's game for its Henry snapshot at once, as WO-125 did. Two hard blocks only:
//                a save pending on this machine (the engine's own state, through the DLL; its own log lines when the DLL cannot answer) and
//                not in the world (loading, a join, the host's reload). The engine's own "not now" (its QuickSave refused) is asked again
//                every 2 s. A snapshot that cannot be taken now is OWED: it pairs with the newest host save it follows; no cap, never dropped
//                silently.
//   stuck (B3)   a taken request with no file and no "ready" in 60 s is stuck: logged; the engine's own cancel (CancelPendingSaveGame, the
//                call every load makes) clears it and a fresh snapshot is taken; the players are told when it cannot be cleared or sticks
//                again ("restart your game").
//   leaving (B4) a final snapshot when the joiner leaves the host's world (the exit dialog before a quit, the mod's leave, the host gone,
//                the relay lost; the host's reload while one is owed); mp_save_on_leave (default on) takes it at every leave. A quit is
//                prepared when the ESC menu opens (the DLL's 0xAD edge): the exit dialog alone leaves about 1 s before the game ends.
//   health (B5)  every host save ends in a pair or a logged reason within 60 s; two in a row unpaired = both players told; mp_save_status on
//                both machines; the host shows "Partner saved." on the joiner's pair (W164 kind 16).
//   the clock    (B2) the conversation clock behind WO-179's guard (A3) and WO-167's scene release: a real conversation of the player's
//                only, with every end (Wo184Rules.ConversationClock). A combat shout, a bark or a monologue never starts it.
// Log tags: WO184-SNAP, WO184-FINAL, WO184-HEALTH, WO184-CLOCK, WO184-FLAG, WO184-STATUS, WO184-PARTNER, WO184-CFG.
public partial class GameBridge
{
    // ================================================================ switches (the mod's w184_cfg)

    private volatile bool _w184SaveOnLeave = true, _w184Recover = true, _w184PartnerSaved = true;

    private void Wo184OnCfg(string? arg)
    {
        foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            bool on = kv.EndsWith("=on", StringComparison.Ordinal);
            if (kv.StartsWith("save_on_leave=", StringComparison.Ordinal)) _w184SaveOnLeave = on;
            else if (kv.StartsWith("recover=", StringComparison.Ordinal)) _w184Recover = on;
            else if (kv.StartsWith("partner_saved=", StringComparison.Ordinal)) _w184PartnerSaved = on;
        }
        Console.WriteLine($"WO184-CFG save_on_leave={On(_w184SaveOnLeave)} recover={On(_w184Recover)} partner_saved={On(_w184PartnerSaved)}");
    }

    private void Wo184OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w184_cfg": Wo184OnCfg(arg); return;
            case "w184_status": _ = Wo184StatusAsync(); return;
            case "w184_test": if (W184Test) _ = Wo184TestAsync(arg); return;
        }
    }

    // Harness-only (KCDMP_TEST_W184=1, never set by the launcher): the live tests' one blocker the game gives no script call for -- the
    // screen faded by the game's own fader (the DLL's op 33, the one the bring-along uses). "fade on|off". And B3's recovery on a save
    // that is really pending: "cancel" starts a QuickSave and at once asks the engine's own cancel (what a stuck snapshot gets after 60 s).
    private static readonly bool W184Test = Environment.GetEnvironmentVariable("KCDMP_TEST_W184") == "1";

    private async Task Wo184TestAsync(string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length >= 2 && f[0] == "fade")
        {
            var r = await _combat.Wo163Async(Protocol.Wo179OpFade, [(byte)(f[1] == "on" ? 1 : 0), .. BitConverter.GetBytes(0.5f)]);
            Console.WriteLine($"WO184-TEST fade {f[1]}: {(r is { Ok: true } ? "the game's own fader asked" : "REFUSED")}");
        }
        else if (f.Length >= 1 && f[0] == "cancel")
        {
            var before = await _combat.Wo184SaveStateAsync();
            await ExecLuaAsync("pcall(function() Game.QuickSave() end)");
            var mid = await _combat.Wo184SaveStateAsync();
            var c = await _combat.Wo184SaveCancelAsync("wo184-test");
            var after = await _combat.Wo184SaveStateAsync();
            Console.WriteLine($"WO184-TEST cancel: before={before} after_the_quicksave={mid} cancel={(c is { } x ? $"result={x.Result} pending_before={x.Before} pending_after={x.After}" : "no answer")} after={after}");
        }
    }

    // ================================================================ B2: the conversation clock

    private readonly Wo184Rules.ConversationClock _w184Clock = new();
    private long _w184ClockStarts, _w184ClockEnds;

    private void Wo184ClockEdge(Wo184Rules.Edge e, string via)
    {
        if (!e.Any) return;
        if (e.Ended)
        {
            Interlocked.Increment(ref _w184ClockEnds);
            Console.WriteLine($"WO184-CLOCK conversation ended ({e.Why}) via={via}");
            Wo167OnDudeDialogue(true);
        }
        if (e.Started)
        {
            Interlocked.Increment(ref _w184ClockStarts);
            Console.WriteLine($"WO184-CLOCK conversation started ({e.Why}) via={via}");
            Wo167OnDudeDialogue(false);
        }
    }

    /// <summary>The log tail's QuestLine (Wo137OnQuestLine): the player's own request and the start attempts (only these come only this way).</summary>
    private void Wo184OnQuestLine(string line)
    {
        var d = Wo184Rules.Parse(line);
        if (d.Kind is Wo184Rules.DialogKind.Request or Wo184Rules.DialogKind.Attempt)
            Wo184ClockEdge(_w184Clock.Feed(d, Environment.TickCount64), "engine");
    }

    /// <summary>The mod's talk events (WO-164's talk line: ask / start / end).</summary>
    private void Wo184OnTalk(string what, string npc, int id)
    {
        long now = Environment.TickCount64;
        var e = what switch
        {
            "ask" => _w184Clock.Ask(npc, now),
            "start" => _w184Clock.TalkStart(npc, id, now),
            "end" => _w184Clock.TalkEnd(npc),
            _ => Wo184Rules.Edge.None,
        };
        Wo184ClockEdge(e, "talk-" + what);
    }

    // ================================================================ the engine's lines (Wo144OnEngineLine)

    private long _w184LinePendingSince;   // the engine's own "BeforeGameSave ... started async waiting" with no "is ready" since (0 = none)
    private volatile bool _w184ReadySeen;
    private long _w184ExitDialogMs = -1_000_000;

    /// <summary>Never consumes: the clock's lines, the engine's save readiness lines, the exit dialog.</summary>
    private void Wo184OnEngineLine(string line)
    {
        long now = Environment.TickCount64;
        var d = Wo184Rules.Parse(line);
        if (d.Kind is not (Wo184Rules.DialogKind.None or Wo184Rules.DialogKind.Request or Wo184Rules.DialogKind.Attempt))
            Wo184ClockEdge(_w184Clock.Feed(d, now), "engine");
        if (line.StartsWith(Wo184Rules.ObserverPrefix, StringComparison.Ordinal))
        {
            if (line.Contains("started async waiting", StringComparison.Ordinal)) Volatile.Write(ref _w184LinePendingSince, Math.Max(now, 1));
            else if (line.Contains("is ready", StringComparison.Ordinal)) { Volatile.Write(ref _w184LinePendingSince, 0); _w184ReadySeen = true; }
            return;
        }
        if (line.StartsWith(Wo184Rules.ExitDialogLine, StringComparison.Ordinal)) Wo184OnExitDialog();
    }

    // ================================================================ the owner (B1)

    private sealed class W184Want
    {
        public WorldSaved W;
        public byte Src;
        public long SinceMs, NextTryMs;
        public string Why = "";
        public string Purpose = "host save";   // WO-189 A4: the joiner's own snapshot owes one too
        public int Tries;
        public bool Toasted;
    }

    private W184Want? _w184Want;                  // the newest host save still wanting a snapshot (owed)
    private readonly object _w184Lock = new();
    private int _w184Busy;                         // the worker (single flight)
    private WorldSaved? _w184LastHost;            // the newest host save received while joined (a final snapshot pairs with it)
    private byte _w184LastHostSrc;
    private WorldSaved? _w184JoinOffer;           // the join's own save (pairs a final snapshot when the host has not saved since)
    private long _w184ReqAtMs;                    // a taken request waiting for its file (0 = none)
    private int _w184StuckRun;
    private bool _w184StuckTold;
    private volatile bool _w184DllArmed;
    private readonly Wo184Rules.SaveHealth _w184Health = new();
    private (uint Seq, string Md5, long AtMs, string Snap, string How) _w184LastPair = (0, "", 0, "", "");
    private long _w184Asked, _w184Paired, _w184EngineNotNow, _w184Stuck, _w184Cancels, _w184Finals, _w184FinalNot, _w184Superseded, _w184Skipped, _w184NotStored;
    private string _w184LastNotNow = "";

    private enum W184Take { Paired, NotNow, Stuck, Failed }

    /// <summary>A host world save (not a branch entry) arrived (OnWorldSavedInAsync).</summary>
    private void Wo184OnHostSaveIn(WorldSaved w, byte src)
    {
        string md5 = Convert.ToHexString(w.Md5).ToLowerInvariant();
        long now = Environment.TickCount64;
        if (!_joinedWorld)
        {
            Interlocked.Increment(ref _w184Skipped);
            Console.WriteLine($"WO184-SNAP host save seq={w.Seq} md5={md5[..8]}: no snapshot -- this game is not in the host's world{(_jj is not null ? " (a join is running: its own pair covers it)" : "")}");
            return;
        }
        _w184Health.Arrived(w.Seq, md5, now);
        lock (_w184Lock) { _w184LastHost = w; _w184LastHostSrc = src; }
        string perm = _joinedTag is null ? "the host's world is not known on this machine"
                    : _resetTag == _joinedTag ? "mp_henry_reset this session (a new choice at the next join)"
                    : !_peerHenryWorld ? "the host's player is not Henry" : "";
        if (perm != "")
        {
            Console.WriteLine($"WO184-SNAP host save seq={w.Seq} md5={md5[..8]}: no snapshot -- {perm}");
            _w184Health.Reason(w.Seq, perm);
            return;
        }
        Wo184Owe(w, src, now);
        _ = Wo184KickAsync();
    }

    private void Wo184Owe(WorldSaved w, byte src, long now)
    {
        lock (_w184Lock)
        {
            if (_w184Want is { } o)
            {
                if (o.W.Seq != w.Seq)
                {
                    Interlocked.Increment(ref _w184Superseded);
                    Console.WriteLine($"WO184-SNAP host save seq={w.Seq} supersedes seq={o.W.Seq} (still owed: {(o.Why.Length > 0 ? o.Why : "being asked")}) -- one snapshot, paired with the newest host save it follows");
                }
                o.W = w; o.Src = src; o.NextTryMs = 0;
                return;
            }
            _w184Want = new W184Want { W = w, Src = src, SinceMs = now };
        }
    }

    /// <summary>The two hard blocks (B1): a save pending on this machine (the engine's own state), or not in the world (loading, a join, a rewind).</summary>
    private async Task<string> Wo184BlockAsync(bool ignoreRewind = false)
    {
        bool pending = Volatile.Read(ref _w184ReqAtMs) != 0;
        if (!pending) pending = (await Wo184EngineStateAsync()).Pending;
        return Wo184Rules.SaveBlock(new Wo184Rules.SaveGate(InWorld: _where is not (GameWhere.Menu or GameWhere.Loading), Joining: _jj is not null,
                                                            Rewinding: _rewinding && !ignoreRewind, SavePending: pending));
    }

    /// <summary>The engine's own save state: the DLL's read (IsAnySaveGamePending / IsSaving / CanSave(Quick)); without it, the engine's log lines.</summary>
    private async Task<(bool Known, bool Pending, bool CanQuick)> Wo184EngineStateAsync()
    {
        try
        {
            var s = await _combat.Wo184SaveStateAsync();
            if (s is { } x) { _w184DllArmed = true; return (true, x.Pending || x.Saving, x.CanQuick); }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException) { }
        _w184DllArmed = false;
        return (false, Volatile.Read(ref _w184LinePendingSince) != 0, true);
    }

    /// <summary>The worker: takes the owed snapshot as soon as the two hard blocks allow (single flight; the 1 s tick and every host save kick it).</summary>
    private async Task Wo184KickAsync()
    {
        if (Interlocked.CompareExchange(ref _w184Busy, 1, 0) != 0) return;
        try
        {
            for (int round = 0; round < 8; round++)
            {
                W184Want? o;
                lock (_w184Lock) o = _w184Want;
                if (o is null) return;
                long now = Environment.TickCount64;
                if (now < o.NextTryMs) return;
                string block = await Wo184BlockAsync();
                if (block != "")
                {
                    if (block != o.Why) { o.Why = block; Console.WriteLine($"WO184-SNAP owed: host save seq={o.W.Seq} -- {block}; asked as soon as that ends (no cap)"); }
                    return;
                }
                lock (_w184Lock) { if (!ReferenceEquals(_w184Want, o)) continue; _w184Want = null; }
                o.Tries++;
                var res = await Wo184TakeAsync(o.W, o, o.Purpose, Wo184Rules.StuckMs);
                if (res is W184Take.Paired or W184Take.Failed) continue;   // a newer host save may be wanting one already
                lock (_w184Lock)
                {
                    if (_w184Want is null) _w184Want = o;   // owed again (the tick asks again)
                    else
                    {
                        Interlocked.Increment(ref _w184Superseded);
                        Console.WriteLine($"WO184-SNAP host save seq={_w184Want.W.Seq} supersedes seq={o.W.Seq} (not taken: {o.Why})");
                        _w184Want.SinceMs = Math.Min(_w184Want.SinceMs, o.SinceMs);
                    }
                }
                return;
            }
        }
        finally { Volatile.Write(ref _w184Busy, 0); }
    }

    /// <summary>
    /// One snapshot, paired with host save <paramref name="w"/>: the engine is asked (Game.QuickSave), the file awaited (the engine's readiness
    /// may wait), verified and stored, the QuickSave moved out of the playline. <paramref name="owed"/> receives the reason when not taken.
    /// </summary>
    private async Task<W184Take> Wo184TakeAsync(WorldSaved w, W184Want? owed, string purpose, long waitMs)
    {
        string md5 = Convert.ToHexString(w.Md5).ToLowerInvariant();
        long tIn = Environment.TickCount64;
        void NotNow(string why, long retryMs)
        {
            if (owed is not null) { owed.Why = why; owed.NextTryMs = Environment.TickCount64 + retryMs; }
            if (why != _w184LastNotNow) { _w184LastNotNow = why; Console.WriteLine($"WO184-SNAP {purpose} for host save seq={w.Seq}: not now -- {why}; asked again in {retryMs / 1000} s"); }
        }
        if (Interlocked.CompareExchange(ref _snapBusy, 1, 0) != 0) { NotNow("the previous snapshot is still running", Wo184Rules.EngineRetryMs); return W184Take.NotNow; }
        HenryStore.LedgerEntry? led = null;
        try
        {
            string? saves = ResolveSavesDirForJoin();
            int pl = _lastWorldPlayline;
            if (saves is null || pl < 0)
            {
                Console.WriteLine($"WO184-SNAP host save seq={w.Seq} md5={md5[..8]}: no snapshot -- the playline of this world is not known");
                _w184Health.Reason(w.Seq, "the playline of this world is not known");
                return W184Take.Failed;
            }
            string dir = Path.Combine(saves, $"playline{pl}");
            int qs = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "quicksave*.whs").Count() : 0;
            if (qs >= SnapshotQuicksaveGuard)
            {
                string why = $"playline{pl} holds {qs} quicksaves (the engine rotates at 100; a snapshot could push out the player's oldest)";
                Console.WriteLine($"WO184-SNAP host save seq={w.Seq} md5={md5[..8]}: no snapshot -- {why}");
                _w184Health.Reason(w.Seq, why);
                return W184Take.Failed;
            }
            // the engine's own "not now", read first (no request into a refusal); without the DLL the request itself is the question
            var st = await Wo184EngineStateAsync();
            if (st.Known && st.Pending) { NotNow("a save is pending on this machine", Wo184Rules.EngineRetryMs); return W184Take.NotNow; }
            if (st.Known && !st.CanQuick) { Interlocked.Increment(ref _w184EngineNotNow); NotNow("the engine cannot save now (its own CanSave said no: a fight, a cutscene, a dialogue lock...)", Wo184Rules.EngineRetryMs); return W184Take.NotNow; }
            // WO-189 A3: CanSave(Quick) does not see the readiness wait, nor a scene positioning the host's copies (a request then cancels every NPC's
            // fast-forward under the save lock -- the field's stuck cutscene); asked only when the engine can take it, else owed (15 s)
            string notReady = await Wo189AskBlockAsync();
            if (notReady.Length > 0) { Interlocked.Increment(ref _w184EngineNotNow); NotNow(notReady, Wo189Rules.NotReadyRetryMs); return W184Take.NotNow; }
            // keep the engine's cached list in step with the disk: a new quicksave is named max(SaveId in the list)+1 (observed)
            await _combat.SaveListAsync(1, pl, "-");
            led = _henry.LedgerAdd(new HenryStore.LedgerEntry { Playline = pl, SinceUtc = DateTime.UtcNow.AddSeconds(-1), WorldTag = _joinedTag!, Kind = "snapshot" });
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var since = DateTime.UtcNow.AddSeconds(-1);
            lock (_snapGate) _snapWait = (pl, since, tcs);
            _w184ReadySeen = false;
            long tReq = Environment.TickCount64;
            Volatile.Write(ref _w184ReqAtMs, tReq);
            Interlocked.Increment(ref _w184Asked);
            string r = await AskModAsync("KCD2MP_Wo125Snapshot", 6000);
            if (!r.StartsWith("ok=true", StringComparison.Ordinal))
            {
                _henry.LedgerSet(led.Id, e => e.State = "done");
                Interlocked.Increment(ref _w184EngineNotNow);
                NotNow($"the engine refused the QuickSave ({(r.Length > 0 ? r : "no answer from the mod")})", Wo184Rules.EngineRetryMs);
                return W184Take.NotNow;
            }
            // the file -- or stuck (B3). The engine's readiness may make it wait: that is the game's own "not now".
            string? path = null;
            bool named = false;
            while (path is null)
            {
                var done = await Task.WhenAny(tcs.Task, Task.Delay(500));
                if (done == tcs.Task) { path = tcs.Task.Result; break; }
                long now = Environment.TickCount64;
                if (!named && !_w184ReadySeen && now - tReq >= Wo189Rules.PendingNameMs) { named = true; await Wo189NamePendingAsync($"the {purpose}'s save", tReq); }   // WO-189 F
                if (_w184ReadySeen && now - tReq > 8_000 && Wo184FindSnapshotFile(dir, since) is string found)
                {
                    path = found;   // the engine wrote it and said ready; the folder watch missed the file
                    Console.WriteLine($"WO184-SNAP host save seq={w.Seq}: the engine said ready but the folder watch saw no file -- found {SaveDisplay(found)} in the playline");
                    break;
                }
                if (now - tReq >= waitMs)
                {
                    if (waitMs < Wo184Rules.StuckMs)
                    {
                        Wo184LateWindow(pl, since, w, led, purpose);   // a file that still lands pairs (never a leak)
                        await Wo189CancelAfterWaitAsync(purpose, tReq);   // WO-189 A1: never left pending (the field: 21 minutes, every later save refused)
                        NotNow("its file had not come when the leave went on", 0);
                        return W184Take.NotNow;
                    }
                    bool landed = await Wo184StuckAsync(w, tReq, tcs.Task, purpose);
                    if (landed) { path = tcs.Task.Result; break; }
                    Console.WriteLine(Wo189Rules.CancelLine(purpose, Environment.TickCount64 - tReq, "B3's stuck handling made the engine's own cancel (WO184-SNAP lines)"));   // WO-189 A1
                    if (owed is not null) { owed.Why = "the previous snapshot got stuck (cleared: asked again)"; owed.NextTryMs = 0; }
                    Wo184LateWindow(pl, since, w, led, purpose);
                    return W184Take.Stuck;
                }
            }
            return Wo184Pair(w, md5, path!, pl, saves, led, tIn, tReq, purpose);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WO184-SNAP {purpose} for host save seq={w.Seq} failed: {ex.GetType().Name}: {ex.Message}");
            _w184Health.Reason(w.Seq, $"the snapshot failed ({ex.GetType().Name})");
            return W184Take.Failed;
        }
        finally
        {
            lock (_snapGate) _snapWait = null;
            Volatile.Write(ref _w184ReqAtMs, 0);
            Volatile.Write(ref _snapBusy, 0);
        }
    }

    // A taken request whose wait ended without its file (stuck, or a leave that went on): a QuickSave that still lands in that playline
    // within LateFileMs is that snapshot's -- paired with its host save (the late rule), never moved out as a "leak" (0.50.9 did that).
    private (int Playline, DateTime Since, WorldSaved W, HenryStore.LedgerEntry Led, string Purpose, long UntilMs)? _w184Late;

    private void Wo184LateWindow(int pl, DateTime since, WorldSaved w, HenryStore.LedgerEntry led, string purpose)
    {
        lock (_snapGate) _w184Late = (pl, since, w, led, purpose, Environment.TickCount64 + Wo184Rules.LateFileMs);
        _henry.LedgerSet(led.Id, e => e.State = "pending");   // a crash before it lands: the sweep moves its file out
    }

    /// <summary>Wo125OnJoinerSaveObserved: a QuickSave that is the late file of a request whose wait ended -- paired now. True = taken.</summary>
    private bool Wo184TakeLateFile(string path)
    {
        (int Playline, DateTime Since, WorldSaved W, HenryStore.LedgerEntry Led, string Purpose, long UntilMs) late;
        lock (_snapGate)
        {
            if (_w184Late is not { } l || Environment.TickCount64 > l.UntilMs) return false;
            var id = WorldSaved.ParsePath(path);
            if (id is not { } i || i.Playline != l.Playline || i.Kind != Protocol.SaveKindQuick || File.GetLastWriteTimeUtc(path) < l.Since) return false;
            late = l;
            _w184Late = null;
        }
        string? saves = ResolveSavesDirForJoin();
        if (saves is null) return false;
        string md5 = Convert.ToHexString(late.W.Md5).ToLowerInvariant();
        Console.WriteLine($"WO184-SNAP the {late.Purpose}'s file landed late ({SaveDisplay(path)}) -- paired with host save seq={late.W.Seq} all the same (never a leak)");
        _ = Task.Run(() =>
        {
            try { Wo184Pair(late.W, md5, path, late.Playline, saves, late.Led, Environment.TickCount64, Environment.TickCount64, late.Purpose + ", late"); }
            catch (Exception ex) { Console.WriteLine($"WO184-SNAP the late file could not be paired: {ex.GetType().Name}: {ex.Message}"); }
        });
        return true;
    }

    /// <summary>The newest engine-named quicksave in the playline written after <paramref name="since"/> (the watch missed it), or null.</summary>
    private static string? Wo184FindSnapshotFile(string dir, DateTime since)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "quicksave*.whs").Select(f => new FileInfo(f)).Where(f => f.LastWriteTimeUtc >= since && f.Length > 0)
                            .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The snapshot's file: verified, its Henry stored paired with host save <paramref name="w"/>, the QuickSave moved out.</summary>
    private W184Take Wo184Pair(WorldSaved w, string md5, string path, int pl, string saves, HenryStore.LedgerEntry led, long tIn, long tReq, string purpose)
    {
        long tFile = Environment.TickCount64;
        byte[] bytes = WhsSave.ReadShared(path);
        var v = WhsSave.Verify(bytes);
        _henry.LedgerSet(led.Id, e => { e.File = Path.GetFileName(path); e.Md5 = v.Md5; e.State = "written"; });
        string? storeWhy = null;
        WhsSave.HenryParts? parts = null;
        if (!v.Ok) storeWhy = "the snapshot does not verify: " + v.Reason;
        else
        {
            var c = WhsSave.Inflate(bytes);
            uint? seed = WhsSave.ReadSeed(c.Raw);
            if (seed is not uint s || Wo157TagFor(s) != _joinedTag) storeWhy = "the snapshot is not of the host's world (seed)";
            else if (!WhsSave.PlayerOf(c.Raw).IsHenry) storeWhy = "the snapshot's player is not Henry";
            else parts = WhsSave.PartsFromStream(c.Raw, WhsSave.DescriptionSummary(c.Desc).GetValueOrDefault("BuildInfo") ?? "", WhsSave.HenryParts.OriginSnapshot);
        }
        W184Take res;
        if (storeWhy is null)
        {
            // WO-184: a snapshot taken later than the host's save pairs with it all the same (the late rule: "a late snapshot pairs with the
            // newest host save it follows", accepted by the maintainer) -- WO-125's 3 s rejection is gone: it lost a pair for a slow request
            var snap = _henry.Store(_joinedTag!, parts!, md5, HenryStore.SourceSnapshot, $"host {Protocol.SaveKindName(w.Kind)} playline{w.Playline}/{w.FileName} seq={w.Seq} ({purpose})");
            Wo134OnSnapshotStored(snap);   // WO-134: the chest ledger pairs with it
            Interlocked.Increment(ref _w184Paired);
            _w184Health.Paired(w.Seq);
            _w184LastPair = (w.Seq, md5, Environment.TickCount64, snap.Short, purpose);
            _w184Menu.Paired(Environment.TickCount64);
            _w184StuckRun = 0;
            _w184StuckTold = false;
            _w184LastNotNow = "";   // the next "not now" is logged again
            Console.WriteLine(FormattableString.Invariant(
                $"MP-HENRY joiner: PAIRED host save seq={w.Seq} md5={md5[..8]} with snapshot {snap.Short} ({purpose}): gate-open -> request {(tReq - tIn) / 1000.0:F2} s, -> file verified {(tFile - tReq) / 1000.0:F2} s ({SaveDisplay(path)}, {bytes.Length} B)"));
            // WO-157 3b.8: "does my inventory save?" -- said on screen; its words are about a host save (a leave's or the menu's snapshot is not one)
            if (purpose.StartsWith("host save", StringComparison.Ordinal)) Wo157TellCharacterSaved();
            _ = W164SendAsync(Protocol.JoinTargetHost, Protocol.W164SaveHealth, Wo184Rules.PairedText(w.Seq, md5));   // B5: the host shows "Partner saved."
            res = W184Take.Paired;
        }
        else
        {
            Interlocked.Increment(ref _w184NotStored);
            Console.WriteLine($"MP-HENRY joiner: host save seq={w.Seq} md5={md5[..8]}: snapshot NOT stored -- {storeWhy}");
            _w184Health.Reason(w.Seq, "snapshot not stored: " + storeWhy);
            res = W184Take.Failed;
        }
        // The QuickSave is a copy of the host's world in this player's playline: out at once.
        bool gone = _henry.MoveOut(path, "a snapshot's QuickSave (the host's world)");
        _ = Task.Run(async () =>
        {
            var after = await _combat.SaveListAsync(1, pl, Path.GetFileNameWithoutExtension(path));
            string expect = OwnSaves(saves, HostSeedForOwn(), null, Wo157HostedSeed).FirstOrDefault(s => s.Save.Playline == pl)?.Save.Base ?? "-";
            Console.WriteLine($"MP-HENRY joiner: snapshot file {SaveDisplay(path)} moved out={On(gone)}; rescan listed={(after is null ? "?" : On(after.Listed))}; Continue would load playline{after?.ContinuePlayline}/{after?.ContinueName ?? "?"} (own newest there: {expect})");
        });
        if (gone) _henry.LedgerSet(led.Id, e => e.State = "done");
        return res;
    }

    // ================================================================ B3: stuck

    /// <summary>
    /// A taken request with no file and no "ready" in 60 s. Logged; the engine's own cancel clears it (mp_save_recover); the players are told
    /// when it cannot be cleared, or when it sticks again (a restart clears what the cancel cannot). True = the file landed meanwhile.
    /// </summary>
    private async Task<bool> Wo184StuckAsync(WorldSaved w, long tReq, Task<string> fileTask, string purpose)
    {
        Interlocked.Increment(ref _w184Stuck);
        int run = Interlocked.Increment(ref _w184StuckRun);
        long now = Environment.TickCount64;
        Console.WriteLine($"WO184-SNAP STUCK: the {purpose} for host save seq={w.Seq} was asked {Wo184Rules.Seconds(now - tReq)} s ago -- no file, {(_w184ReadySeen ? "although the engine said its save was ready (the file was not found)" : "and the engine never said its save was ready (BeforeGameSave waiting)")} [stuck {run} in a row]");
        (byte Result, bool Before, bool After)? c = null;
        if (_w184Recover)
        {
            try { c = await _combat.Wo184SaveCancelAsync("wo184-stuck"); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException) { }
        }
        if (fileTask.IsCompletedSuccessfully) { Console.WriteLine("WO184-SNAP the stuck snapshot's file landed after all -- paired"); return true; }
        bool recovered = c is { Result: 1 or 0 };   // cancelled, or nothing pending any more
        if (recovered)
        {
            Interlocked.Increment(ref _w184Cancels);
            Volatile.Write(ref _w184LinePendingSince, 0);
            Console.WriteLine($"WO184-SNAP recovered: {(c!.Value.Result == 1 ? "the engine's own CancelPendingSaveGame (the call every load makes) cleared the pending save" : "the engine had nothing pending any more")} -- a fresh snapshot is asked for now");
            _ = W164SendAsync(Protocol.JoinTargetHost, Protocol.W164SaveHealth, Wo184Rules.RecoveredWord);
        }
        else
            Console.WriteLine($"WO184-SNAP NOT recovered: {(!_w184Recover ? "mp_save_recover is off" : c is null ? "the DLL did not answer (no engine cancel here)" : c.Value.Result == 2 ? "the engine's cancel is not armed on this build" : c.Value.Result == 4 ? "the engine still says a save is pending after its own cancel" : "the cancel faulted")} -- the pending save blocks every snapshot until this game restarts or loads");
        if ((!recovered || run >= 2) && !_w184StuckTold)
        {
            _w184StuckTold = true;
            Console.WriteLine($"WO184-SNAP the joiner and the host are told: the joiner's character can't be saved now ({(recovered ? "it stuck again after the engine's own cancel" : "the engine's pending save could not be cleared")})");
            await W164NoticeAsync(Wo184Rules.JoinerStuckText);
            _ = W164SendAsync(Protocol.JoinTargetHost, Protocol.W164SaveHealth, Wo184Rules.StuckWord);
        }
        return false;
    }

    // ================================================================ B4: leaving

    /// <summary>
    /// A final snapshot before this game leaves the host's world (paired with the newest host save it follows). "" = taken and paired, or
    /// nothing owed with mp_save_on_leave off ("-" is returned then); else why not (said to the player by the caller).
    /// </summary>
    private async Task<string> Wo184FinalSnapshotAsync(string why, bool ignoreRewind = false, bool onlyIfOwed = false, bool optional = false)
    {
        // optional: the ESC menu's early snapshot -- not taken, it leaves nothing owed of its own (a host save's owed one stays owed)
        // a snapshot already on its way captures Henry as he is now: it is the final one (waited for, bounded)
        long t0 = Environment.TickCount64;
        if (Volatile.Read(ref _w184ReqAtMs) != 0 || Volatile.Read(ref _snapBusy) != 0)
        {
            Console.WriteLine($"WO184-FINAL {why}: a snapshot is on its way already -- it is the final one (waited for, at most {Wo184Rules.FinalWaitMs / 1000} s)");
            while ((Volatile.Read(ref _w184ReqAtMs) != 0 || Volatile.Read(ref _snapBusy) != 0) && Environment.TickCount64 - t0 < Wo184Rules.FinalWaitMs) await Task.Delay(250);
            bool newOwed;
            lock (_w184Lock) newOwed = _w184Want is not null;
            if (_w184LastPair.AtMs >= t0 && !newOwed) { Console.WriteLine($"WO184-FINAL {why}: done -- host save seq={_w184LastPair.Seq} paired"); return ""; }
        }
        WorldSaved? host;
        bool owed;
        lock (_w184Lock) { owed = _w184Want is not null; host = _w184Want?.W ?? _w184LastHost ?? _w184JoinOffer; }
        if (onlyIfOwed && !owed) return "-";
        bool pending = Volatile.Read(ref _w184ReqAtMs) != 0 || (await Wo184EngineStateAsync()).Pending;
        string block = Wo184Rules.FinalSnapshotBlock(_joinedWorld, _where is not (GameWhere.Menu or GameWhere.Loading) && (ignoreRewind || !_rewinding) && _jj is null,
                                                     pending, owed, _w184SaveOnLeave, host is not null);
        if (block == "-") { Console.WriteLine($"WO184-FINAL {why}: nothing owed -- the last pair stands (mp_save_on_leave off)"); return "-"; }
        if (block != "")
        {
            if (!optional) Interlocked.Increment(ref _w184FinalNot);
            Console.WriteLine($"WO184-FINAL {why}: NOT taken -- {block}{(owed ? $" (host save seq={host?.Seq} stays without a pair)" : "")}");
            return block;
        }
        W184Want? was;
        lock (_w184Lock) { was = _w184Want; _w184Want = null; }   // this final snapshot takes the owed one
        Interlocked.Increment(ref _w184Finals);
        var hw = host!.Value;
        Console.WriteLine($"WO184-FINAL {why}: one final snapshot, paired with host save seq={hw.Seq} md5={Convert.ToHexString(hw.Md5)[..8].ToLowerInvariant()} (the newest host save it follows){(owed ? " -- it takes the owed one" : "")}");
        var probe = new W184Want { W = hw, SinceMs = Environment.TickCount64 };
        var res = await Wo184TakeAsync(hw, probe, "final snapshot (" + why + ")", Wo184Rules.FinalWaitMs);
        if (res == W184Take.Paired) return "";
        lock (_w184Lock) _w184Want ??= optional ? was : was ?? probe;   // still owed (a later chance: the tick, the next host save)
        if (!optional) Interlocked.Increment(ref _w184FinalNot);
        string reason = probe.Why.Length > 0 ? probe.Why : res.ToString().ToLowerInvariant();
        Console.WriteLine($"WO184-FINAL {why}: the final snapshot was NOT paired -- {reason}");
        return reason;
    }

    // ================================================================ B4: the ESC menu (the DLL's 0xAD edge; a session only)

    private readonly Wo184Rules.MenuTime _w184Menu = new();
    private long _w184MenuSnaps, _w184MenuSkips;

    private bool Wo184LastPairIsMenu => _w184LastPair.How == "final snapshot (" + Wo184Rules.MenuPurpose + ")";

    /// <summary>
    /// The DLL's 0xAD: the ESC menu opened / closed. ON the pipe reader: nothing here waits (the WO-131 trap). An opening starts the final
    /// snapshot (Wo184Rules.MenuSnapshotBlock): a quit from the menu then finds it paired, or on its way -- the exit dialog alone left it
    /// about 1 s, and a player who confirmed faster quit before the file was written (live, 0.3 s: nothing paired).
    /// </summary>
    private void Wo184OnMenu(bool opened)
    {
        long now = Environment.TickCount64;
        if (!opened) { _w184Menu.Closed(now); return; }
        long session = _w184Menu.Opened(now);
        long since = _w184LastPair.AtMs > 0 ? now - _w184LastPair.AtMs : -1;
        string block = Wo184Rules.MenuSnapshotBlock(W137Joiner, _joinedWorld && _jj is null, _w184SaveOnLeave,
                                                    Volatile.Read(ref _w184ReqAtMs) != 0 || Volatile.Read(ref _snapBusy) != 0, since);
        if (block == "-") return;
        if (block != "") { Interlocked.Increment(ref _w184MenuSkips); Console.WriteLine($"WO184-FINAL the ESC menu opened (#{session}) -- no snapshot now: {block}"); return; }
        Interlocked.Increment(ref _w184MenuSnaps);
        Interlocked.Exchange(ref _w184MenuSnapSession, session);
        Console.WriteLine($"WO184-FINAL the ESC menu opened (#{session}) in the host's world -- the final snapshot starts now, before any quit");
        _ = Task.Run(async () =>
        {
            string r = await Wo184FinalSnapshotAsync(Wo184Rules.MenuPurpose, optional: true);
            if (r is not ("" or "-")) Console.WriteLine($"WO184-FINAL the ESC menu's snapshot (#{session}) was not taken: {r} -- the exit dialog tries again");
        });
    }

    /// <summary>"C_UISaveLoad::HandleExit()": the game's exit dialog opened. A quit follows when the player confirms: the final snapshot now.</summary>
    private void Wo184OnExitDialog()
    {
        if (!W137Joiner) return;
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _w184ExitDialogMs) < 30_000) return;   // one per 30 s (a cancelled exit dialog opened again)
        Interlocked.Exchange(ref _w184ExitDialogMs, now);
        if (Volatile.Read(ref _w184ReqAtMs) == 0 && Volatile.Read(ref _snapBusy) == 0
            && Wo184Rules.MenuPairCoversQuit(Wo184LastPairIsMenu, _w184Menu.ClosedSincePair(now)))
        {
            Console.WriteLine($"WO184-FINAL the game's exit dialog opened -- the ESC menu's own snapshot paired {Wo184Rules.Ago(now - _w184LastPair.AtMs)} and the player has been in the menu since: it is the final one (no second save racing the quit)");
            return;
        }
        Console.WriteLine("WO184-FINAL the game's exit dialog opened in the host's world -- one final snapshot now, before the player confirms the quit");
        _ = Task.Run(async () =>
        {
            string r = await Wo184FinalSnapshotAsync("quit (the exit dialog)");
            if (r is not ("" or "-")) await W164NoticeAsync($"Your last progress isn't saved yet ({r}). Wait a moment before you quit.");
        });
    }

    private long _w184MenuSnapSession;                                  // the menu session whose opening started the newest menu snapshot
    private (long Session, bool InMenu) _w184QuitMenu;                 // the menu at the quit (Wo184TakeOwedAtQuit)

    /// <summary>
    /// Wo124OnGameQuit's part, synchronous: the owed snapshot (if any) is taken out before the world is marked left, and the menu's state at
    /// the quit is kept (the menu's snapshot may land after it: Wo184AfterQuitAsync waits for it).
    /// </summary>
    private object? Wo184TakeOwedAtQuit()
    {
        long now = Environment.TickCount64;
        _w184QuitMenu = (_w184Menu.Session, _w184Menu.InMenu(now));
        lock (_w184Lock) { var o = _w184Want; _w184Want = null; return o; }
    }

    /// <summary>
    /// "CSystem::Quit invoked" (Wo124OnGameQuit): what this player's character is saved up to, said in the launcher -- after a final snapshot
    /// still on its way has landed (the sweep waits for it, at most 20 s).
    /// </summary>
    private async Task Wo184AfterQuitAsync(string? tag, object? owedAtQuit)
    {
        for (int i = 0; i < 40 && Volatile.Read(ref _w184ReqAtMs) != 0; i++) await Task.Delay(500);
        Wo125Sweep("game quit");
        var o = owedAtQuit as W184Want;
        long now = Environment.TickCount64;
        // "up to the moment you quit": the exit dialog's own snapshot, or this menu's snapshot with the player in the menu until the quit
        bool menuFinal = Wo184LastPairIsMenu && _w184QuitMenu.InMenu && Interlocked.Read(ref _w184MenuSnapSession) == _w184QuitMenu.Session;
        bool finalPaired = _w184LastPair.AtMs > 0 && now - _w184LastPair.AtMs < 120_000
                           && (_w184LastPair.How.StartsWith("final snapshot (quit", StringComparison.Ordinal) || menuFinal);
        string text;
        if (o is not null)
        {
            Console.WriteLine($"WO184-SNAP owed host save seq={o.W.Seq} NOT taken: the game quit ({o.Why}) -- this player's character is kept up to the last pair ({Wo184PairText(now)})");
            text = $"Your character's last progress was NOT saved ({o.Why}). It is kept up to {Wo184PairText(now)}.";
        }
        else if (finalPaired) text = "Your character is saved, up to the moment you quit.";
        else if (_w184LastPair.AtMs > 0 && Wo184LastPairIsMenu) text = $"Your character is saved as it was when you last opened the menu ({Wo184Rules.Ago(now - _w184LastPair.AtMs)}).";
        else if (_w184LastPair.AtMs > 0) text = $"Your character is saved up to {Wo184PairText(now)}.";
        else text = "Your character is kept as it was when you joined.";
        var last = tag is not null ? _henry.Snapshots(tag).FirstOrDefault() : null;
        Console.WriteLine($"MP-JOIN joiner: the game quit from the host's world -- its Henry for world {tag ?? "?"} is the newest pair ({last?.Short ?? "none"}); told: {text}");
        SetJoinUi("notice", text);
    }

    private string Wo184PairText(long now) =>
        _w184LastPair.AtMs > 0 ? $"your host's save #{_w184LastPair.Seq} ({Wo184Rules.Ago(now - _w184LastPair.AtMs)})" : "the moment you joined";

    // ================================================================ the 1 s tick (the WO-151 loop, through Wo167TickAsync)

    private long _w184OwedToastMs;

    private async Task Wo184TickAsync()
    {
        long now = Environment.TickCount64;
        Wo184ClockEdge(_w184Clock.Tick(now), "time");
        W184Want? o;
        lock (_w184Lock) o = _w184Want;
        if (o is not null)
        {
            if (!_joinedWorld)
            {
                lock (_w184Lock) if (ReferenceEquals(_w184Want, o)) _w184Want = null;
                Console.WriteLine($"WO184-SNAP owed host save seq={o.W.Seq} NOT taken: this game left the host's world ({o.Why}) -- the leave's own final snapshot decided");
            }
            else
            {
                await Wo184KickAsync();
                lock (_w184Lock) o = _w184Want;
                if (o is { Toasted: false } && now - o.SinceMs >= 120_000 && now - _w184OwedToastMs >= 120_000)
                {
                    o.Toasted = true;
                    _w184OwedToastMs = now;
                    Console.WriteLine($"WO184-SNAP still owed after {Wo184Rules.Seconds(now - o.SinceMs)} s ({o.Why}) -- the player is told");
                    await W164NoticeAsync($"Your character hasn't been saved with your host's last save yet ({o.Why}). It is saved as soon as the game allows.");
                }
            }
        }
        // B5: every host save ends in a pair or a reason within 60 s; two in a row unpaired = both told
        var due = _w184Health.Tick(now, Wo184StateNow, out bool tellBoth);
        foreach (var d in due)
            Console.WriteLine($"WO184-HEALTH host save seq={d.Seq} md5={Wo184Rules.Md5Short(d.Md5)}: NOT paired within {Wo184Rules.HealthMs / 1000} s -- {d.Reason} [unpaired in a row: {_w184Health.FailRun}]");
        if (tellBoth)
        {
            Console.WriteLine("WO184-HEALTH two host saves in a row did not pair -- the joiner and the host are told");
            await W164NoticeAsync(Wo184Rules.JoinerTwoUnpairedText);
            _ = W164SendAsync(Protocol.JoinTargetHost, Protocol.W164SaveHealth, Wo184Rules.UnpairedText(_w184Health.FailRun));
        }
        Wo184FlagEnds(now);
    }

    /// <summary>The current state, for a host save that reached 60 s without a pair (its one logged reason).</summary>
    private string Wo184StateNow()
    {
        W184Want? o;
        lock (_w184Lock) o = _w184Want;
        if (o is not null) return $"owed: {(o.Why.Length > 0 ? o.Why : "being asked")} ({o.Tries} tries)";
        if (Volatile.Read(ref _w184ReqAtMs) != 0) return "its snapshot was asked and its file is awaited";
        if (!_joinedWorld) return "this game left the host's world";
        return "superseded by a newer host save, or no snapshot was asked (see the WO184-SNAP lines)";
    }

    // ================================================================ B2: the flags with no end of their own

    private long _w184StopsSinceMs, _w184RewindSinceMs;

    private void Wo184FlagEnds(long now)
    {
        // WO-179's local scene with no end line: forgotten after 30 min (a load or its end line clear it first)
        var cur = _w179Cur;
        if (cur is not null && !cur.Ended && now - cur.StartMs > 30 * 60_000)
        {
            lock (_w179Lock) if (ReferenceEquals(_w179Cur, cur)) _w179Cur = null;
            Console.WriteLine($"WO184-FLAG scene '{cur.Name}' forgotten: no end line in 30 min");
        }
        // WO-167's interactive-scene count: no queue line (enqueue, end, cleanup, fader) for 10 min while it says one runs = it runs no more
        if (Wo170Rules.ScenesRunning(Interlocked.Read(ref _w167IsmEnq), Interlocked.Read(ref _w167IsmEnd)) > 0 && now - Volatile.Read(ref _w167ProgressMs) > 10 * 60_000
            && Volatile.Read(ref _w167ProgressMs) > 0)
        {
            Console.WriteLine($"WO184-FLAG interactive scenes counted running ({Interlocked.Read(ref _w167IsmEnq) - Interlocked.Read(ref _w167IsmEnd)}) with no queue line for 10 min -- the count is reset");
            Wo167SceneReset();
            Volatile.Write(ref _w167ProgressMs, now);
        }
        // WO-139's guard stops on this joiner: an outcome that never came (a load kills the chain that reports it) -- 15 min
        if (_w139StoppedHere.IsEmpty) _w184StopsSinceMs = 0;
        else if (_w184StopsSinceMs == 0) _w184StopsSinceMs = now;
        else if (now - _w184StopsSinceMs > 15 * 60_000)
        {
            Console.WriteLine($"WO184-FLAG guard stop(s) {string.Join(",", _w139StoppedHere.Keys)} with no outcome in 15 min -- forgotten");
            _w139StoppedHere.Clear();
            _w184StopsSinceMs = 0;
        }
        // WO-125's rewind: the host never came back from its reload (connected, silent) -- 10 min, then this game leaves its world
        if (!_rewinding) _w184RewindSinceMs = 0;
        else if (_w184RewindSinceMs == 0) _w184RewindSinceMs = now;
        else if (now - _w184RewindSinceMs > 10 * 60_000 && _joinedWorld && _jj is null)
        {
            _w184RewindSinceMs = 0;
            Console.WriteLine("WO184-FLAG the host's reload never came back in 10 min -- this game leaves the host's world");
            _ = LeaveSharedWorldAsync("host-reload-timeout", "Your host's reload never finished.");
        }
    }

    /// <summary>Any world load: a load ends every conversation and every arrest step, and cancels any pending save (LoadSavedGame does).</summary>
    private void Wo184OnLoad()
    {
        W184Want? o;
        lock (_w184Lock) { o = _w184Want; _w184Want = null; }
        if (o is not null)
            Console.WriteLine($"WO184-SNAP owed host save seq={o.W.Seq}: a load -- {(_jj is not null || _rejoinPending ? "the rejoin's own pair (the Henry it loads) replaces it" : "this game left the host's world")} ({o.Why})");
        Volatile.Write(ref _w184LinePendingSince, 0);
        Wo184ClockEdge(_w184Clock.Reset("a load"), "load");
        if (_w138Dialog) { _w138Dialog = false; Console.WriteLine("WO184-FLAG the dialogue pause reason cleared by the load (its Lua chain may not run again)"); _ = _sendPauseIfChanged?.Invoke(); }
        if (!_w139StoppedHere.IsEmpty) { Console.WriteLine($"WO184-FLAG guard stop(s) {string.Join(",", _w139StoppedHere.Keys)} cleared by the load"); _w139StoppedHere.Clear(); }
    }

    /// <summary>
    /// The session's end (the relay's loop). The owed snapshot and the newest host save stay: the leave that follows (relay lost, host gone)
    /// takes its final snapshot with them while this game is still in the host's world; the next join starts them again (Wo184OnJoined).
    /// </summary>
    private void Wo184Reset(string why) => Wo184ClockEdge(_w184Clock.Reset(why), why);

    /// <summary>The join's Ready (Wo125AfterReady): the join save is this world's first pair; a final snapshot pairs with it until the host saves.</summary>
    private void Wo184OnJoined(byte[]? offerMd5, uint seq)
    {
        lock (_w184Lock)
        {
            _w184Want = null;
            _w184LastHost = null;
            _w184JoinOffer = offerMd5 is { Length: 16 } m ? new WorldSaved(seq, 0, 0, 0, 0, m) : null;
        }
        _w184Health.Clear();
        if (offerMd5 is { Length: 16 } om) _w184LastPair = (seq, Convert.ToHexString(om).ToLowerInvariant(), Environment.TickCount64, "the join save", "join");
    }

    // ================================================================ B5: the host's side, the status

    private readonly ConcurrentDictionary<byte, (uint Seq, string Md5, long AtMs)> _w184PartnerPairs = new();
    private readonly ConcurrentDictionary<byte, (string Word, long AtMs)> _w184PartnerWords = new();
    private long _w184HealthIn, _w184HealthMalformed;

    /// <summary>W164 kind 16 in (the host): a joiner's word about his own save. Runs in the relay's reader: nothing here is awaited (WO-179B).</summary>
    private Task Wo184OnHealthInAsync(byte src, string text)
    {
        if (!W137Host || !Wo184Rules.TryParseHealth(text, out string word, out uint seq, out string md5, out int n)) { Interlocked.Increment(ref _w184HealthMalformed); return Task.CompletedTask; }
        Interlocked.Increment(ref _w184HealthIn);
        long now = Environment.TickCount64;
        _w184PartnerWords[src] = (word, now);
        void Say(string t) => _ = ExecLuaAsync($"if KCD2MP_W184Say then KCD2MP_W184Say(\"{EscapeLua(t)}\") end");
        switch (word)
        {
            case "paired":
                // a later snapshot of the same host save (the joiner's menu or leave): logged, not shown again
                bool known = _w184PartnerPairs.TryGetValue(src, out var prev);
                bool again = !Wo184Rules.PartnerSavedShows(known, prev.Seq, prev.Md5 ?? "", seq, md5);
                _w184PartnerPairs[src] = (seq, md5, now);
                Wo189OnPartnerPaired();   // WO-189 A4: the reminder's clock starts again
                Console.WriteLine($"WO184-PARTNER player {src} saved: its character paired with this host's save seq={seq} md5={md5}{(again ? " (again: a later snapshot of the same save -- not shown again)" : "")}");
                if (_w184PartnerSaved && !again) Say(Wo184Rules.HostPartnerSavedText);
                break;
            case "unpaired":
                Console.WriteLine($"WO184-PARTNER player {src}: {n} of this host's saves in a row did not save its character -- told");
                Say(Wo184Rules.HostTwoUnpairedText);
                break;
            case Wo184Rules.StuckWord:
                Console.WriteLine($"WO184-PARTNER player {src}: its save is stuck (the engine's pending save could not be cleared, or stuck again) -- told");
                Say(Wo184Rules.HostStuckText);
                break;
            case Wo184Rules.RecoveredWord:
                Console.WriteLine($"WO184-PARTNER player {src}: its stuck save was cleared by the engine's own cancel; a fresh snapshot follows");
                if (Interlocked.Exchange(ref _w189RecoveredTold, 1) == 0) Say("Your partner's stuck save was cleared; it is taken again.");   // WO-189 A2: both told, once
                break;
        }
        return Task.CompletedTask;
    }

    /// <summary>mp_save_status (both machines): the log line and a short line on screen.</summary>
    private async Task Wo184StatusAsync()
    {
        long now = Environment.TickCount64;
        string shortText;
        if (W137Host)
        {
            var parts = _w184PartnerPairs.Select(kv => $"player {kv.Key}: saved with your save #{kv.Value.Seq} {Wo184Rules.Ago(now - kv.Value.AtMs)}").ToList();
            var words = _w184PartnerWords.Where(kv => kv.Value.Word != "paired").Select(kv => $"player {kv.Key}: '{kv.Value.Word}' {Wo184Rules.Ago(now - kv.Value.AtMs)}").ToList();
            Console.WriteLine($"WO184-STATUS role=host partners={_w184PartnerPairs.Count} {string.Join("; ", parts)}{(words.Count > 0 ? " | " + string.Join("; ", words) : "")} health_in={Interlocked.Read(ref _w184HealthIn)} malformed={Interlocked.Read(ref _w184HealthMalformed)} partner_saved={On(_w184PartnerSaved)}");
            shortText = parts.Count == 0 ? "No partner save confirmed yet this session." : "Partner saves: " + string.Join("; ", parts) + ".";
        }
        else
        {
            var st = await Wo184EngineStateAsync();
            W184Want? o;
            lock (_w184Lock) o = _w184Want;
            string owed = o is null ? "none" : $"host save #{o.W.Seq} owed {Wo184Rules.Seconds(now - o.SinceMs)} s ({o.Why}, {o.Tries} tries)";
            string pair = _w184LastPair.AtMs > 0 ? $"host save #{_w184LastPair.Seq} md5={Wo184Rules.Md5Short(_w184LastPair.Md5)} {Wo184Rules.Ago(now - _w184LastPair.AtMs)} (snapshot {_w184LastPair.Snap}, {_w184LastPair.How})" : "none this session";
            Console.WriteLine($"WO184-STATUS role=joiner joined={On(_joinedWorld)} world={_joinedTag ?? "-"} last_pair={pair} owed={owed} in_flight={On(Volatile.Read(ref _w184ReqAtMs) != 0)} stuck_run={_w184StuckRun} "
                              + $"engine={(st.Known ? $"pending={On(st.Pending)} can_save={On(st.CanQuick)}" : $"unknown (lines: pending={On(Volatile.Read(ref _w184LinePendingSince) != 0)})")} clock={_w184Clock.Describe(now)} "
                              + $"asked={Interlocked.Read(ref _w184Asked)} paired={Interlocked.Read(ref _w184Paired)} not_now={Interlocked.Read(ref _w184EngineNotNow)} stuck={Interlocked.Read(ref _w184Stuck)} cancels={Interlocked.Read(ref _w184Cancels)} "
                              + $"finals={Interlocked.Read(ref _w184Finals)} final_not={Interlocked.Read(ref _w184FinalNot)} menu_snaps={Interlocked.Read(ref _w184MenuSnaps)} menu_skips={Interlocked.Read(ref _w184MenuSkips)} superseded={Interlocked.Read(ref _w184Superseded)} not_stored={Interlocked.Read(ref _w184NotStored)} "
                              + $"health_paired={_w184Health.PairedCount} health_failed={_w184Health.FailedCount} unpaired_run={_w184Health.FailRun} save_on_leave={On(_w184SaveOnLeave)} recover={On(_w184Recover)}");
            shortText = !_joinedWorld ? "Not in your host's world: nothing to save here."
                      : o is not null ? $"Your character: NOT saved with your host's save #{o.W.Seq} yet ({o.Why}). Last saved with {Wo184PairText(now)}."
                      : _w184LastPair.AtMs > 0 ? $"Your character was saved with {Wo184PairText(now)}. Nothing waiting."
                      : "Your character is kept as it was when you joined; your host has not saved since.";
        }
        Console.WriteLine($"WO189-STATUS role={(W137Host ? "host" : "joiner")} {Wo189StatusText(now)}");   // WO-189 A5
        shortText += Wo189StatusShort(now);
        await ExecLuaAsync($"if KCD2MP_W184Say then KCD2MP_W184Say(\"{EscapeLua(shortText)}\") end");
    }

    private string Wo184StatsText() =>
        $"asked={Interlocked.Read(ref _w184Asked)} paired={Interlocked.Read(ref _w184Paired)} not_now={Interlocked.Read(ref _w184EngineNotNow)} stuck={Interlocked.Read(ref _w184Stuck)} cancels={Interlocked.Read(ref _w184Cancels)} finals={Interlocked.Read(ref _w184Finals)} final_not={Interlocked.Read(ref _w184FinalNot)} menu_snaps={Interlocked.Read(ref _w184MenuSnaps)} menu_skips={Interlocked.Read(ref _w184MenuSkips)} superseded={Interlocked.Read(ref _w184Superseded)} skipped={Interlocked.Read(ref _w184Skipped)} clock_starts={Interlocked.Read(ref _w184ClockStarts)} clock_ends={Interlocked.Read(ref _w184ClockEnds)} dll={On(_w184DllArmed)}";
}
