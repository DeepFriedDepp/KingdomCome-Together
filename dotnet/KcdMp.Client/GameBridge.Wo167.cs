// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-167 (docs/WO-167-findings.md) -- 0.48.5, the agent's half. The Lua half is kdcmp.lua's WO-167 section.
//   T2 the host: a collision bark (crime_reaction_barks.kolize) on its NPC is attributed by the mod (WO167-BUMP); the joiner: a copy the
//      host just used and that showed a combat state is let go of by the WO-132 engagement (w167_clear)
//   T3 the timeout line gains the copy's NPC-state (its behaviour: the unstance the DLL reads), its minigame and the last behaviour-tree
//      path the engine printed for it
//   T4 mp_scene_release (joiner, default ON): a dialogue's interactive scene still running 20 s after the dialogue ended is released the
//      engine's own way -- the WO-151 rescue save (the engine cancels every NPC's fast-forward and the scene positioning ends); the mod
//      then brings the faders back, stands the player up and tells them
//   C1 a stand-in body (the mod's w167_standin) gets the WO-155 walk pulse from the DLL once it is bound (avatar event kind 3)
//   C2 mp_copy_weapons (joiner, default ON): the host NPC's hand rows (WO-143) name the weapon class in its hand; in a fight that class
//      (or nothing) is told to the mod, which holds it on the copy; every host attack row played on a copy also says armed or unarmed
//   C3 mp_swing_watch (joiner, default ON): a row played on a copy counts as shown only when the DLL saw its action installed; one that
//      never animated is taken out of the played rows, so the hit's verdict plays the WO-163 lunge before its damage
//   C4 mp_sync_fallback (joiner, default ON): a paired (sync) row for a stand-in or an NPC the engine said has no sync data is played as
//      the plain attack row of the same weapon; the engine's sync-guard line is counted against the rows we sent
//   WO167-SCENE stuck|ended|not-ended|faders ...   WO167-PULSE / WO167-SHOWN / WO167-SYNCFALLBACK / WO167-SYNCGUARD   MP-WO167-STATS ...
public partial class GameBridge
{
    private volatile bool _w167SceneRelease = Wo167Rules.DefaultSceneRelease;
    private volatile bool _w167TalkFallback;   // the Lua decides; mirrored for the stats line

    // T4: the interactive scene's lines since the last load
    private long _w167IsmEnq, _w167IsmEnd, _w167DlgEndMs, _w167ProgressMs, _w167ReleasedForMs, _w167FaderHeldMs;
    private int _w167ReleaseBusy;
    private long _w167SceneReleases, _w167SceneEnded, _w167SceneNotEnded, _w167FaderRestores, _w167Bumps, _w167Clears, _w167BtLines;

    // T3: the last behaviour-tree path the engine printed per NPC (and when)
    private readonly ConcurrentDictionary<string, (string Path, long AtMs)> _w167Bt = new(StringComparer.Ordinal);

    private static long W167NowMs() => Environment.TickCount64;

    // C1-C4
    private volatile bool _w167CopyWeapons = Wo167Rules.DefaultCopyWeapons, _w167SwingWatch = Wo167Rules.DefaultSwingWatch, _w167SyncFallback = Wo167Rules.DefaultSyncFallback;
    private readonly ConcurrentDictionary<string, long> _w167StandinPulse = new(StringComparer.Ordinal);   // name -> spawn ms (pulse not asked yet)
    private readonly ConcurrentDictionary<string, bool> _w167StandinSet = new(StringComparer.Ordinal);     // every stand-in this session (C4)
    private readonly ConcurrentDictionary<string, long> _w167NoSync = new(StringComparer.Ordinal);         // C4: the engine said: no sync data
    private readonly ConcurrentDictionary<string, long> _w167SyncSentAt = new(StringComparer.Ordinal);     // C4: our last sync row per NPC
    private readonly ConcurrentDictionary<string, string> _w167HostWeapon = new(StringComparer.Ordinal);   // C2: the host NPC's weapon class in hand, "-" none
    private long _w167Shown, _w167NoAnim, _w167ShownUnknown, _w167SyncFallbacks, _w167SyncNoPlain, _w167SyncGuardAfterRow, _w167SyncGuardOther,
                 _w167Pulses, _w167WeaponCalls, _w167CfgTickN;
    private bool _w167WatchFaultTold;

    // ---------------------------------------------------------------- the mod's events

    private void Wo167OnEvent(string name, string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (name)
        {
            case "w167_cfg":   // scene_release=on|off, talk_fallback=on|off
                foreach (var kv in f)
                {
                    if (kv is "scene_release=on" or "scene_release=off")
                    {
                        bool v = kv.EndsWith("=on", StringComparison.Ordinal);
                        if (v != _w167SceneRelease) Console.WriteLine($"WO167-CFG mp_scene_release {(v ? "on" : "off")}");
                        _w167SceneRelease = v;
                    }
                    else if (kv is "talk_fallback=on" or "talk_fallback=off") _w167TalkFallback = kv.EndsWith("=on", StringComparison.Ordinal);
                    else if (kv is "copy_weapons=on" or "copy_weapons=off") _w167CopyWeapons = kv.EndsWith("=on", StringComparison.Ordinal);
                    else if (kv is "swing_watch=on" or "swing_watch=off") { _w167SwingWatch = kv.EndsWith("=on", StringComparison.Ordinal); _w167CfgTickN = 9; }
                    else if (kv is "sync_fallback=on" or "sync_fallback=off") _w167SyncFallback = kv.EndsWith("=on", StringComparison.Ordinal);
                }
                return;
            case "w167_clear":   // <npc>: T2 -- the host used this copy and it showed a combat state here: the WO-132 engagement lets go
                if (f.Length >= 1 && Wo137Text.IsNpc(f[0])) _ = Wo167ClearAsync(f[0]);
                return;
            case "w167_fault":     // <feature> <site>: the mod's half of a feature faulted -- this half goes off too (the mod said it on screen)
                if (f.Length >= 1 && f[0].Length is > 0 and <= 24 && f[0].All(c => c is >= 'a' and <= 'z' or '-'))
                {
                    long nf = Interlocked.Increment(ref _w167Faults);
                    _w167OffFeatures.TryAdd(f[0], 0);
                    if (f[0] == "scene-release") _w167SceneRelease = false;
                    if (f[0] == "copy-weapons") _w167CopyWeapons = false;
                    Console.WriteLine($"WO167-FAULT site=mod:{(f.Length > 1 ? f[1] : "?")} feature={f[0]} -- the mod's half faulted: {f[0]} is off for this session here too [faults={nf}]");
                }
                return;
            case "w167_ftpick":    // <dest> <how>: FT2 -- a pick on this player's travel map
                Wo167OnFtPick(arg);
                return;
            case "w167_standin":   // <npc>: C1 -- a stand-in body was spawned here (its walk pulse once bound; C4's set)
                if (f.Length >= 1 && Wo137Text.IsNpc(f[0])) { _w167StandinPulse[f[0]] = W167NowMs(); _w167StandinSet[f[0]] = true; }
                return;
        }
    }

    private async Task Wo167ClearAsync(string npc)
    {
        Interlocked.Increment(ref _w167Clears);
        bool engaged = false;
        if (!W167Off("talk-clear") && _w132Engaged.TryRemove(npc, out var eg))
        {
            engaged = true;
            try { await _combat.Wo132EngageAsync(false, eg.Eid, default); }
            catch (Exception ex) { Wo167Fault("t2-clear", "talk-clear", ex); }
        }
        Console.WriteLine($"WO167-T2 clear npc={npc} engagement={(engaged ? "released" : "none")} -- the host's conversation with it is over and its copy showed a combat state here");
    }

    // ---------------------------------------------------------------- the engine's lines

    /// <summary>T2 (host): the collision bark; T3: behaviour-tree paths; T4: the faders. Never consumes a line others need.</summary>
    private void Wo167OnEngineLine(string line)
    {
        Wo170OnEngineLine(line);   // WO-170: the engine's barks -- an NPC taking a partner's figure on is said (WO170-AGGRO)
        if (line.IndexOf("/(BT)", StringComparison.Ordinal) >= 0 && Wo167Rules.BtPath(line) is { } bt)
        {
            _w167Bt[bt.Npc] = (bt.Path, W167NowMs());
            Interlocked.Increment(ref _w167BtLines);
            if (_w167Bt.Count > 512) foreach (var k in _w167Bt.Where(kv => W167NowMs() - kv.Value.AtMs > 600_000).Select(kv => kv.Key).ToList()) _w167Bt.TryRemove(k, out _);
            return;
        }
        if (Wo167Rules.SyncGuardNpc(line) is { } sg && Wo137Text.IsNpc(sg))   // C4: the engine has no sync data for this NPC
        {
            long now = W167NowMs();
            _w167NoSync[sg] = now;
            bool after = _w167SyncSentAt.TryGetValue(sg, out long at) && now - at <= Wo167Rules.SyncGuardAfterRowMs;
            long n = after ? Interlocked.Increment(ref _w167SyncGuardAfterRow) : Interlocked.Increment(ref _w167SyncGuardOther);
            if (n <= 20 || n % 50 == 0)
                Console.WriteLine($"WO167-SYNCGUARD npc={sg} followed_row={(after ? "yes" : "no")} -- the engine has no sync data for it: its paired rows play as plain attacks from now on [n={n}]");
            return;
        }
        if (W137Host && Wo167Rules.KolizeSoul(line) is { } npc && Wo137Text.IsNpc(npc))
        {
            Interlocked.Increment(ref _w167Bumps);
            _ = ExecLuaAsync($"if KCD2MP_W167Bump then KCD2MP_W167Bump(\"{npc}\") end");
            return;
        }
        if (Wo167Rules.FaderTook(line) is double secs) Wo167OnSceneStage("FaderDone", "", secs.ToString("F2", CultureInfo.InvariantCulture));
        if (Wo167Rules.IsTravelInterruptLine(line)) Wo167OnTravelInterrupt();
    }

    /// <summary>T3: the behaviour-tree path the engine last printed for <paramref name="npc"/> (within 2 minutes), or "-".</summary>
    private string Wo167BtFor(string npc) =>
        _w167Bt.TryGetValue(npc, out var b) && W167NowMs() - b.AtMs < 120_000 ? $"{b.Path}@{(W167NowMs() - b.AtMs) / 1000}s" : "-";

    /// <summary>T3: the timeout line's tail -- the copy's NPC-state (the DLL's read), its minigame, the engine's last tree path.</summary>
    private async Task<string> Wo167ReadinessTailAsync(string npc)
    {
        string behaviour = "?", mg = "?";
        if (W167Off("talk-readiness")) return $"behaviour=off npc_minigame=off bt={Wo167BtFor(npc)}";
        try
        {
            var a = await _combat.Wo141ReadAsync(npc);
            if (a is { } s)
            {
                behaviour = s.Unstance == ActivityState.NoUnstance ? "none" : (await _combat.Wo141UnstanceNameAsync(s.Unstance) ?? $"id{s.Unstance}");
                if (s.Stance != 0) behaviour += $"+stance{s.Stance}";
                mg = s.Minigame == ActivityState.NoMinigame ? "none" : s.Minigame.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex) { Wo167Fault("t3-readiness", "talk-readiness", ex); }
        return $"behaviour={behaviour} npc_minigame={mg} bt={Wo167BtFor(npc)}";
    }

    // ---------------------------------------------------------------- T4: the scene

    /// <summary>The log tail's scene stages (the WO-151 feed) plus the InteractiveSceneManager lines and the faders.</summary>
    private void Wo167OnSceneStage(string stage, string type, string name)
    {
        Wo189OnIsmStage(stage);   // WO-189 D: the scenes' positioning bound
        long now = W167NowMs();
        switch (stage)
        {
            case "IsmEnqueue":
                Interlocked.Increment(ref _w167IsmEnq);
                Wo170PushScenes();   // WO-170 F4: the copies of a dialogue's scene are not paused again while it runs
                break;
            case "IsmEnd":
            case "IsmInterrupt":
                long ends = Interlocked.Increment(ref _w167IsmEnd);
                if (ends > Interlocked.Read(ref _w167IsmEnq)) Interlocked.Exchange(ref _w167IsmEnd, Interlocked.Read(ref _w167IsmEnq));   // never below zero running
                Volatile.Write(ref _w167ProgressMs, now);
                if (Interlocked.Read(ref _w167IsmEnd) >= Interlocked.Read(ref _w167IsmEnq)) Volatile.Write(ref _w167DlgEndMs, 0);   // nothing runs: done
                if (Volatile.Read(ref _w167FaderHeldMs) > 0) _ = Wo167FaderRestoreAsync("the scene ended");
                Wo170PushScenes();
                break;
            case "IsmCleanup":
            case "FaderDone":
                Volatile.Write(ref _w167ProgressMs, now);
                break;
        }
    }

    /// <summary>T4: a dialogue with this player ended / started (Wo137OnQuestLine): the scene's clock.</summary>
    private void Wo167OnDudeDialogue(bool ended)
    {
        Wo179OnDudeDialogue(ended);   // WO-179: a conversation is a scene (A3's twin bound, C2's relocation context)
        long now = W167NowMs();
        if (ended) Volatile.Write(ref _w167DlgEndMs, now);
        else { Volatile.Write(ref _w167DlgEndMs, 0); Volatile.Write(ref _w167ProgressMs, now); }
    }

    /// <summary>A load: the engine interrupts every scene; nothing counted before it means anything after.</summary>
    private void Wo167SceneReset()
    {
        Interlocked.Exchange(ref _w167IsmEnq, 0);
        Interlocked.Exchange(ref _w167IsmEnd, 0);
        Volatile.Write(ref _w167DlgEndMs, 0);
        Wo170PushScenes();
    }

    /// <summary>Once a second (the WO-151 loop).</summary>
    private async Task Wo167TickAsync()
    {
        try { if (!W167Off("stand-in-pulse")) await Wo167StandInTickAsync(); } catch (Exception ex) { Wo167Fault("c1-pulse", "stand-in-pulse", ex); }
        try { await Wo167PushConfigAsync(); } catch (Exception ex) { Wo167Fault("c3-config", "swing-watch", ex); }
        try { await Wo184TickAsync(); } catch (Exception ex) { Console.WriteLine($"WO184-SNAP tick failed: {ex.GetType().Name}: {ex.Message}"); }   // WO-184: the owed snapshot, the health, the clock
        try { await Wo189TickAsync(); } catch (Exception ex) { Console.WriteLine($"WO189 tick failed: {ex.GetType().Name}: {ex.Message}"); }   // WO-189: the watchdog, the own snapshot, the reminder, C, D
        long now = W167NowMs();
        long held = Volatile.Read(ref _w167FaderHeldMs);
        if (held > 0 && now - held >= Wo167Rules.FaderHoldMaxMs) await Wo167FaderRestoreAsync("60 s after the black was lifted");
        if (!_w167SceneRelease || !W137Joiner) return;
        if (!_w151Scenes.IsEmpty) return;   // a cutscene runs: the WO-151 guard's own business
        long dlgEnd = Volatile.Read(ref _w167DlgEndMs);
        if (!Wo167Rules.SceneStuck(Interlocked.Read(ref _w167IsmEnq), Interlocked.Read(ref _w167IsmEnd), dlgEnd,
                                   Volatile.Read(ref _w167ProgressMs), Volatile.Read(ref _w167ReleasedForMs), now,
                                   _w151SceneGuard ? Wo179Rules.ConversationSceneBoundMs : Wo167Rules.SceneStuckMs)) return;   // WO-179 A: 2 s, was 20 s
        if (Interlocked.CompareExchange(ref _w167ReleaseBusy, 1, 0) != 0) return;
        Volatile.Write(ref _w167ReleasedForMs, dlgEnd);
        _ = Wo167ReleaseSceneAsync(dlgEnd, now);
    }

    private async Task Wo167ReleaseSceneAsync(long dlgEnd, long now)
    {
        try
        {
            Interlocked.Increment(ref _w167SceneReleases);
            long endsBefore = Interlocked.Read(ref _w167IsmEnd);
            Console.WriteLine(FormattableString.Invariant(
                $"WO167-SCENE stuck dialogue_ended_s={(now - dlgEnd) / 1000.0:F0} scenes_running={Interlocked.Read(ref _w167IsmEnq) - endsBefore} -- the conversation's scene never ended (its NPCs are the host's copies): released without a save request (WO-183 3a: the engine's own end waited 1 s, then the black lifted)"));
            // WO-183 3a: no save request (it stayed pending mid-arrest and every later save of that machine was refused); the end is waited 1 s
            long until = W167NowMs() + (_w151SceneGuard ? Wo179Rules.ConversationEndWaitMs : Wo167Rules.SceneEndWaitMs);
            while (W167NowMs() < until && Interlocked.Read(ref _w167IsmEnd) <= endsBefore) await Task.Delay(250);
            bool ended = Interlocked.Read(ref _w167IsmEnd) > endsBefore;
            if (ended) Interlocked.Increment(ref _w167SceneEnded);
            else { Interlocked.Increment(ref _w167SceneNotEnded); Volatile.Write(ref _w167FaderHeldMs, W167NowMs()); }
            Console.WriteLine($"WO167-SCENE {(ended ? "ended" : "not-ended")} -- {(ended ? "the engine ended the scene; the faders back, the player stood up and told" : "the engine did not end it: the black is lifted (wh_ui_FaderSuspend 1, given back when it ends or in 60 s), the player stood up and told")}");
            await ExecLuaAsync($"if KCD2MP_W167SceneReleased then KCD2MP_W167SceneReleased({(ended ? "true" : "false")}, \"stuck-20s\") end");
        }
        catch (Exception ex) { Wo167Fault("t4-release", "scene-release", ex); }
        finally { Volatile.Write(ref _w167ReleaseBusy, 0); }
    }

    private async Task Wo167FaderRestoreAsync(string why)
    {
        if (Interlocked.Exchange(ref _w167FaderHeldMs, 0) == 0) return;
        Interlocked.Increment(ref _w167FaderRestores);
        Console.WriteLine($"WO167-SCENE faders given back ({why})");
        await ExecLuaAsync($"if KCD2MP_W167FaderRestore then KCD2MP_W167FaderRestore(\"{EscapeLua(why)}\") end");
    }

    // ---------------------------------------------------------------- C1-C4

    /// <summary>C4: the row to play for a host attack row on <paramref name="npc"/>'s copy -- a sync row it cannot play becomes its plain attack.</summary>
    private ActionRowCatalog.Row Wo167RowFor(string npc, Guid rowGuid, ActionRowCatalog.Row row, ActionRowCatalog catalog)
    {
        if (!Wo167Rules.IsSyncFragment(row.Fragment)) return row;
        bool noSync = _w167StandinSet.ContainsKey(npc) || _w167NoSync.ContainsKey(npc);
        if (!_w167SyncFallback || !noSync) { _w167SyncSentAt[npc] = W167NowMs(); return row; }
        if (catalog.PlainForSync(row) is { } plain)
        {
            long n = Interlocked.Increment(ref _w167SyncFallbacks);
            if (n <= 30 || n % 50 == 0)
                Console.WriteLine($"WO167-SYNCFALLBACK npc={npc} row={rowGuid} plain={plain.Guid} why={(_w167StandinSet.ContainsKey(npc) ? "stand-in" : "engine-said-no-sync-data")} -- a paired attack it cannot play here is shown as its plain attack [n={n}]");
            return plain.Row;
        }
        Interlocked.Increment(ref _w167SyncNoPlain);
        _w167SyncSentAt[npc] = W167NowMs();
        return row;
    }

    // C3: every swing of a copy whose watch has not decided yet -- a verdict for that copy waits for all of them (Wo161JudgeApplyAsync).
    // All, not the last: live (the second soak), two rows queued on one copy in the same instant both never animated, and a verdict that
    // resumed on the later watch's answer still found the earlier row in the played rows (judged shown, no lunge) -- 4 of 9 such swings.
    private readonly ConcurrentDictionary<string, List<(long At, TaskCompletionSource Done)>> _w167Watching = new(StringComparer.Ordinal);
    private long _w167VerdictWaits;

    private List<(long At, TaskCompletionSource Done)> W167OpenWatches(string npc, long nowMs)
    {
        if (!_w167Watching.TryGetValue(npc, out var l)) return [];
        lock (l) return l.Where(w => !w.Done.Task.IsCompleted && nowMs - w.At < Wo167Rules.WatchVerdictCapMs).ToList();
    }

    /// <summary>C3: a swing of <paramref name="npc"/>'s copy is still watched (played under WatchVerdictCapMs ago, not decided).</summary>
    private bool W167WatchPending(string npc) => _w167SwingWatch && W167OpenWatches(npc, Environment.TickCount64).Count > 0;

    /// <summary>C3: wait for every open watch of <paramref name="npc"/>'s swings (each at most until WatchVerdictCapMs after it was played).</summary>
    private async Task Wo167AwaitWatchAsync(string npc)
    {
        long now = Environment.TickCount64;
        var open = W167OpenWatches(npc, now);
        if (open.Count == 0) return;
        long left = open.Max(w => w.At) + Wo167Rules.WatchVerdictCapMs - now;
        if (left <= 0) return;
        Interlocked.Increment(ref _w167VerdictWaits);
        await Task.WhenAny(Task.WhenAll(open.Select(w => w.Done.Task)), Task.Delay((int)left));
    }

    /// <summary>C3: was the swing just queued on this copy animated? A row that never was is taken out of the played rows (the lunge plays).</summary>
    private async Task Wo167CheckShownAsync(string npc, uint eid, Guid rowGuid, long playedAt)
    {
        if (!_w167SwingWatch) return;
        var watched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var list = _w167Watching.GetOrAdd(npc, _ => new());
        lock (list) { list.RemoveAll(w => w.Done.Task.IsCompleted); list.Add((playedAt, watched)); }
        try
        {
            await Task.Delay(Wo167Rules.ShownFirstLookMs);
            var s = await _combat.Wo167SwingSeenAsync(eid);
            if (s is { Seen: Wo167Rules.SeenPending }) { await Task.Delay(Wo167Rules.ShownSecondLookMs - Wo167Rules.ShownFirstLookMs); s = await _combat.Wo167SwingSeenAsync(eid); }
            if (s is { Seen: Wo167Rules.SeenPending }) { await Task.Delay(250); s = await _combat.Wo167SwingSeenAsync(eid); }
            byte seen = s?.Seen ?? Wo167Rules.SeenOff;
            if (seen == Wo167Rules.SeenInstalled) { Interlocked.Increment(ref _w167Shown); return; }
            if (seen != Wo167Rules.SeenNoAnim) { Interlocked.Increment(ref _w167ShownUnknown); return; }   // off / none / still pending: as 0.48.2
            long n = Interlocked.Increment(ref _w167NoAnim);
            bool taken = _w161Played.TryGetValue(npc, out var pl) && pl.Retract(playedAt);
            Wo161NoteRowRefused(npc, Wo167Rules.ReasonNoAnim);
            if (n <= 30 || n % 50 == 0)
                Console.WriteLine($"WO167-SHOWN npc={npc} row={rowGuid} shown=no reason=no-anim wait_ms={s?.WaitMs} taken_out={(taken ? "yes" : "no")} -- the copy never animated this swing: its blow comes with the lunge [n={n}]");
        }
        catch (Exception ex) { Wo167Fault("c3-shown", "swing-watch", ex); }
        finally { watched.TrySetResult(); }   // a verdict waiting for this swing goes on (shown, taken out, or not known)
    }

    /// <summary>C2: the host NPC's hand row (WO-143): the weapon class in its hand; in a fight the copy is told at once.</summary>
    private async Task Wo167OnHostHandsAsync(ExtraRow r)
    {
        if (!_w167CopyWeapons || !W137Joiner) return;
        string right = ExtraRow.ClassText(r.Right);
        string w = "-";
        if (right != "-")
        {
            var idx = await SoulIndexAsync();
            if (idx is null) return;                                   // the tables are not read: nothing is told
            if (!(Guid.TryParse(right, out var g) && idx.TryGetItem(g, out var it) && Wo167Rules.IsWeaponKind(it.Kind))) return;   // a tool: WO-143's
            w = right;
        }
        string was = _w167HostWeapon.TryGetValue(r.Name, out var pw) ? pw : "";
        _w167HostWeapon[r.Name] = w;
        if (w == was) return;
        if (!(W151InFight(r.Name) || W167HostFought(r.Name))) return;   // out of a fight the stream's drawn flag decides, once
        Interlocked.Increment(ref _w167WeaponCalls);
        await ExecLuaAsync($"if KCD2MP_W167Weapon then KCD2MP_W167Weapon(\"{r.Name}\", \"{w}\", \"hands\") end");
    }

    /// <summary>C2: a host attack row played on a copy says armed or unarmed -- the copy follows (with the host's class when known).</summary>
    private void Wo167RowWeapon(string npc, ActionRowCatalog.Row row)
    {
        if (!_w167CopyWeapons) return;
        bool? armed = Wo167Rules.RowArmed(row.Tags);
        if (armed is null) return;
        string cls = armed == false ? "-" : (_w167HostWeapon.TryGetValue(npc, out var w) && w != "-" ? w : "?");
        Interlocked.Increment(ref _w167WeaponCalls);
        _ = ExecLuaAsync($"if KCD2MP_W167Weapon then KCD2MP_W167Weapon(\"{npc}\", \"{cls}\", \"row\") end");
    }

    /// <summary>C2: the host reported its NPC in combat in the last 10 s (WO-164's own record of the host's combat events).</summary>
    private bool W167HostFought(string npc) => _w164HostCombatTold.TryGetValue(npc, out long at) && Environment.TickCount64 - at < 10_000;

    /// <summary>C1, once a second: a stand-in that is bound now gets its walk pulse (asked once).</summary>
    private async Task Wo167StandInTickAsync()
    {
        if (_w167StandinPulse.IsEmpty) return;
        long now = W167NowMs();
        foreach (var (name, at) in _w167StandinPulse.ToArray())
        {
            if (now - at > Wo167Rules.StandInPulseWaitMs) { _w167StandinPulse.TryRemove(name, out _); Console.WriteLine($"WO167-PULSE npc={name} not bound within 10 s -- no pulse asked"); continue; }
            if (!_npcEntityIds.TryGetValue(name, out uint eid) || eid == 0) continue;
            _w167StandinPulse.TryRemove(name, out _);
            var r = await _combat.AvatarEventAsync(3, eid);
            Interlocked.Increment(ref _w167Pulses);
            Console.WriteLine($"WO167-PULSE npc={name} eid=0x{eid:X} asked={(r.Ok ? "ok" : r.ReasonTag)} -- a stand-in (a fresh spawn) gets the walk pulse a fresh avatar gets (no T-pose)");
        }
    }

    /// <summary>C3: the DLL's switch, every 10 s; a fault that switched the watch off is said once.</summary>
    private async Task Wo167PushConfigAsync()
    {
        if (++_w167CfgTickN % 10 != 0) return;
        var c = await _combat.Wo167ConfigAsync((byte)(_w167SwingWatch ? 1 : 0));
        if (c is { Faulted: true } && !_w167WatchFaultTold)
        {
            _w167WatchFaultTold = true;
            Console.WriteLine("WO167-SHOWN the DLL switched the swing check off for this session (a game error): every swing counts as shown, as in 0.48.2 -- told on screen once");
            await ExecLuaAsync("if KCD2MP_ShowNativeToast then KCD2MP_ShowNativeToast(\"Swing check is off this session (a game error).\") end");
        }
    }

    // ---------------------------------------------------------------- no silent guards

    private readonly ConcurrentDictionary<string, byte> _w167OffFeatures = new(StringComparer.Ordinal);
    private long _w167Faults;

    /// <summary>
    /// The standing rule: a caught fault is counted, its site logged, its feature switched off for this session and said on screen once
    /// (Wo167Rules.FaultText). scene-release and swing-watch are the switches' own flags (the DLL's watch follows at the next push).
    /// </summary>
    private void Wo167Fault(string site, string feature, Exception ex)
    {
        long n = Interlocked.Increment(ref _w167Faults);
        bool first = _w167OffFeatures.TryAdd(feature, 0);
        if (feature == "scene-release") _w167SceneRelease = false;
        if (feature == "swing-watch") _w167SwingWatch = false;
        Console.WriteLine($"WO167-FAULT site={site} feature={feature} {ex.GetType().Name}: {ex.Message} -- {feature} is off for this session [faults={n}]");
        if (first) _ = ExecLuaAsync($"if KCD2MP_ShowNativeToast then KCD2MP_ShowNativeToast(\"{Wo167Rules.FaultText(feature)}\") end");
    }

    private bool W167Off(string feature) => _w167OffFeatures.ContainsKey(feature);

    /// <summary>Work that waits (the mod's answer, the engine's start) runs here, never in the relay's reader loop; its fault is the rule's.</summary>
    private void Wo167Background(string site, string feature, Func<Task> work) =>
        _ = Task.Run(async () => { try { await work(); } catch (Exception ex) { Wo167Fault(site, feature, ex); } });

    // ---------------------------------------------------------------- FT1-FT4: one shared fast travel

    /// <summary>The session's fast travel: the host's own mp_fast_travel on the host, the host's value on a joiner (WO-154).</summary>
    private bool W167SessionTravelOn => W137Host ? _w154mFtPref : (_w154mHostFtKnown ? _w154mHostFt : Wo154MenuRules.FastTravelDefault);

    // host
    private volatile bool _w167FtActive;
    private uint _w167FtSeq;
    private string _w167FtDest = "-";
    private volatile bool _w167FtInterrupted;
    private (string Dest, long AtMs) _w167FtPick = ("-", 0);
    private (string Dest, long AtMs, byte From, uint Seq) _w167FtAsked = ("-", 0, 0, 0);
    // joiner
    private volatile bool _w167MirrorActive, _w167LocalTravel;
    private long _w167MirrorSinceMs;
    private uint _w167MirrorSeq, _w167ReqSeq;
    private long _w167FtStarts, _w167FtEnds, _w167FtInterrupts, _w167FtRequests, _w167FtRefusals, _w167FtMirrors, _w167FtMirrorsBlind, _w167FtPicks;

    /// <summary>FT2, joiner: the shared travel's own run holds the engine's switch open (WO-114's re-assert waits) until it ended, 3 min at most.</summary>
    private bool W167MirrorHoldsSwitch => _w167MirrorActive && W167NowMs() - _w167MirrorSinceMs < Wo167Rules.MirrorMaxMs;

    /// <summary>The mod's w167_ftpick: a pick on this player's travel map (dest "@name" / "x,y,z" / "-", how = the element event).</summary>
    private void Wo167OnFtPick(string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string dest = f.Length >= 1 && W164Text.IsTravelDest(f[0]) ? f[0] : "-";
        Interlocked.Increment(ref _w167FtPicks);
        if (W137Host)
        {
            _w167FtPick = (dest, W167NowMs());
            Console.WriteLine($"WO167-FT host pick dest={dest} -- the destination of the travel this game starts now (its partners run it too)");
            return;
        }
        if (!W137Joiner) return;
        if (!W167SessionTravelOn)
        {
            Console.WriteLine($"WO167-FT joiner pick dest={dest}: the session's fast travel is off (the host's switch) -- not asked; the off message is said");
            return;
        }
        uint seq = unchecked(++_w167ReqSeq);
        _ = W164SendAsync(Protocol.JoinTargetHost, Protocol.W164Travel, W164Text.TravelRequest(seq, dest));
        Console.WriteLine($"WO167-FT joiner pick dest={dest} seq={seq} -- asked of the host (one travel for both: the host's world runs it)");
    }

    /// <summary>W164 kind 6 in.</summary>
    private async Task Wo167OnTravelAsync(byte src, string text)
    {
        if (!W164Text.TryParseTravel(text, out char verb, out uint seq, out string arg, out bool interrupted)) { Interlocked.Increment(ref _w164Malformed); return; }
        switch (verb)
        {
            case 'r' when W137Host: Wo167Background("ft-request", "shared-travel", () => Wo167FtRequestAsync(src, seq, arg)); return;   // never waited on in the relay's reader (WO-131)
            case 'n' when !W137Host:
                Interlocked.Increment(ref _w167FtRefusals);
                Console.WriteLine($"WO167-FT the host refused the pick seq={seq} why={arg} -- nothing moves");
                await ExecLuaAsync($"if KCD2MP_W167FtRefused then KCD2MP_W167FtRefused(\"{arg}\") end");
                return;
            case 's' when !W137Host: Wo167Background("ft-mirror", "shared-travel", () => Wo167FtMirrorAsync(seq, arg)); return;
            case 'e' when !W137Host:
                Console.WriteLine($"WO167-FT the host's travel seq={seq} ended at {arg}{(interrupted ? " INTERRUPTED midway (a road encounter): this player is placed beside the host there" : "")}{(_w167LocalTravel ? " -- this game's own run is still under way: placed beside the host when it ends" : "")}");
                await ExecLuaAsync("if KCD2MP_W172AfterHostTravel then KCD2MP_W172AfterHostTravel(\"host-travel-ended\") end");   // WO-172 FT1: a refused quest travel's black lifted
                return;
            default: Interlocked.Increment(ref _w164Malformed); return;
        }
    }

    /// <summary>FT2, host: a joiner's pick -- the host's world travels there when its own rules let it; else the joiner is told why.</summary>
    private async Task Wo167FtRequestAsync(byte src, uint seq, string dest)
    {
        Interlocked.Increment(ref _w167FtRequests);
        string? why = null;
        if (W167Off("shared-travel")) why = "fault";
        else if (!W167SessionTravelOn) why = "off";
        else if (_w167FtActive) why = "travelling";
        else if (dest == "-") why = "unknown-destination";
        else
        {
            string r = (await AskModAsync("KCD2MP_W167FtCanTravel", 3000)).Trim();
            if (r != "ok") why = Wo167Rules.RefusalWord(r);
        }
        if (why is null)
        {
            _w167FtAsked = (dest, W167NowMs(), src, seq);
            await ExecLuaAsync($"if KCD2MP_W167FtGo then KCD2MP_W167FtGo(\"{dest}\", {src}) end");
            long until = W167NowMs() + Wo167Rules.TravelStartWaitMs;
            while (W167NowMs() < until && !_w167FtActive) await Task.Delay(200);
            if (!_w167FtActive)
            {
                why = "not-started";   // the engine's own rules said no (over-encumbered, outside the navmesh ...): its map may be open here
                await ExecLuaAsync("if KCD2MP_W167FtNotStarted then KCD2MP_W167FtNotStarted(\"host\") end");
            }
        }
        if (why is not null)
        {
            Interlocked.Increment(ref _w167FtRefusals);
            Console.WriteLine($"WO167-FT host refused player {src}'s pick seq={seq} dest={dest} why={why} -- told; nothing moves");
            await W164SendAsync(src, Protocol.W164Travel, W164Text.TravelRefused(seq, why));
            return;
        }
        Console.WriteLine($"WO167-FT host travels for player {src}'s pick seq={seq} dest={dest} -- one travel for both");
    }

    /// <summary>FT1-FT4, host: this game's own travel started / ended ("FastTravel: started / ended", WO-114's tail).</summary>
    private void Wo167OnHostTravel(bool active)
    {
        if (!W137Host || W167Off("shared-travel")) return;   // off: the joiners come along by the leash's pull alone (0.48.2)
        long now = W167NowMs();
        if (active)
        {
            _w167FtActive = true;
            _w167FtInterrupted = false;
            uint seq = unchecked(++_w167FtSeq);
            string dest = now - _w167FtAsked.AtMs <= Wo167Rules.PickFreshMs ? _w167FtAsked.Dest
                        : now - _w167FtPick.AtMs <= Wo167Rules.PickFreshMs ? _w167FtPick.Dest : "-";
            bool forJoiner = now - _w167FtAsked.AtMs <= Wo167Rules.PickFreshMs;
            _w167FtDest = dest;
            Interlocked.Increment(ref _w167FtStarts);
            var peers = Wo134Peers().ToList();
            Console.WriteLine($"WO167-FT host travel seq={seq} started dest={dest} by={(forJoiner ? $"player{_w167FtAsked.From}" : dest == "-" ? "the-game (a quest's or the console's)" : "this player")} -> {peers.Count} joiner(s) run it too");
            foreach (byte j in peers) _ = W164SendAsync(j, Protocol.W164Travel, W164Text.TravelStart(seq, dest));
            _ = ExecLuaAsync($"if KCD2MP_W167FtTravelling then KCD2MP_W167FtTravelling(true, {(forJoiner ? "true" : "false")}) end");
            return;
        }
        if (!_w167FtActive) return;
        _w167FtActive = false;
        Interlocked.Increment(ref _w167FtEnds);
        if (_w167FtInterrupted) Interlocked.Increment(ref _w167FtInterrupts);
        Console.WriteLine(FormattableString.Invariant($"WO167-FT host travel seq={_w167FtSeq} ended at ({_lastX:F1},{_lastY:F1},{_lastZ:F1}){(_w167FtInterrupted ? " INTERRUPTED midway" : "")} -- the joiners come along beside the host (WO-114's pull, after their own run ends)"));
        foreach (byte j in Wo134Peers()) _ = W164SendAsync(j, Protocol.W164Travel, W164Text.TravelEnd(_w167FtSeq, _lastX, _lastY, _lastZ, _w167FtInterrupted));
        _ = ExecLuaAsync("if KCD2MP_W167FtTravelling then KCD2MP_W167FtTravelling(false, false) end");
    }

    /// <summary>FT2, joiner: the host's travel started -- this game runs the game's own travel to the same destination.</summary>
    private async Task Wo167FtMirrorAsync(uint seq, string dest)
    {
        if (W167Off("shared-travel")) { Console.WriteLine($"WO167-FT the host's travel seq={seq} -- shared travel is off this session (a fault): the leash brings this player along"); return; }
        string crime = W183CrimeProcess();
        if (crime != "") { Console.WriteLine($"WO167-FT the host's travel seq={seq} -- NOT run here: {crime} (WO-183 3d: nothing of the mod moves a player in an arrest, a crime stop or a punishment)"); return; }
        _w167MirrorSeq = seq;
        _w167MirrorActive = true;
        _w167MirrorSinceMs = W167NowMs();
        Interlocked.Increment(ref _w167FtMirrors);
        if (dest == "-") Interlocked.Increment(ref _w167FtMirrorsBlind);
        Console.WriteLine($"WO167-FT the host's travel seq={seq} started dest={dest} -- {(dest == "-" ? "its destination is not known here (a quest's travel): told, and placed beside the host when it arrives" : "this game runs the same travel (the game's own map and route), its switch held open until it ends")}");
        await ExecLuaAsync($"if KCD2MP_W167FtMirror then KCD2MP_W167FtMirror({seq}, \"{dest}\") end");
        if (dest == "-") { _w167MirrorActive = false; return; }
        // the travel must start here within a few seconds, or the mirror is over (the leash still brings this player along)
        long until = W167NowMs() + Wo167Rules.TravelStartWaitMs;
        while (W167NowMs() < until && !_w167LocalTravel) await Task.Delay(200);
        if (!_w167LocalTravel)
        {
            _w167MirrorActive = false;
            Console.WriteLine($"WO167-FT this game's run of the shared travel seq={seq} did not start -- placed beside the host when it arrives (the leash)");
            await ExecLuaAsync("if KCD2MP_W167FtNotStarted then KCD2MP_W167FtNotStarted(\"joiner\") end");
        }
    }

    private long _w167LocalTravelEndedMs = -1_000_000;

    /// <summary>FT2, joiner: this game's run of the shared travel moves the player now, or ended under 3 s ago (WO-147's motion check waits).</summary>
    private bool W167OwnRunMoving => _w167LocalTravel || W167NowMs() - _w167LocalTravelEndedMs < 3000;

    /// <summary>FT2, joiner: this game's own travel started / ended (WO-114's tail). True = it is the shared travel's run (no warning).</summary>
    private bool Wo167OnJoinerLocalTravel(bool active)
    {
        bool shared = _w167MirrorActive;
        _w167LocalTravel = active;
        if (!active) _w167LocalTravelEndedMs = W167NowMs();
        if (!active && shared)
        {
            _w167MirrorActive = false;
            Console.WriteLine($"WO167-FT this game's run of the shared travel seq={_w167MirrorSeq} ended -- the host places this player beside it (WO-114's pull)");
        }
        return shared;
    }

    /// <summary>FT3, host: the engine stopped this game's travel midway ("Interrupting FastTravel...", or a road event's "stopping FastTravel: 'Y'").</summary>
    private void Wo167OnTravelInterrupt()
    {
        if (W137Host && _w167FtActive) { _w167FtInterrupted = true; Console.WriteLine($"WO167-FT host travel seq={_w167FtSeq} INTERRUPTED by the game (a road encounter)"); }
    }

    private string Wo167FtStatsText() =>
        $"ft_session={(W167SessionTravelOn ? "on" : "off")} ft_starts={Interlocked.Read(ref _w167FtStarts)} ft_ends={Interlocked.Read(ref _w167FtEnds)} ft_interrupts={Interlocked.Read(ref _w167FtInterrupts)} ft_picks={Interlocked.Read(ref _w167FtPicks)} ft_requests={Interlocked.Read(ref _w167FtRequests)} ft_refusals={Interlocked.Read(ref _w167FtRefusals)} ft_mirrors={Interlocked.Read(ref _w167FtMirrors)} ft_mirrors_blind={Interlocked.Read(ref _w167FtMirrorsBlind)}";

    private string Wo167StatsText() =>
        $"faults={Interlocked.Read(ref _w167Faults)} off={(_w167OffFeatures.IsEmpty ? "-" : string.Join(',', _w167OffFeatures.Keys.OrderBy(k => k, StringComparer.Ordinal)))} "
        + $"scene_release={(_w167SceneRelease ? "on" : "off")} talk_fallback={(_w167TalkFallback ? "on" : "off")} scene_releases={Interlocked.Read(ref _w167SceneReleases)} ended={Interlocked.Read(ref _w167SceneEnded)} not_ended={Interlocked.Read(ref _w167SceneNotEnded)} fader_restores={Interlocked.Read(ref _w167FaderRestores)} bumps={Interlocked.Read(ref _w167Bumps)} clears={Interlocked.Read(ref _w167Clears)} bt_lines={Interlocked.Read(ref _w167BtLines)}"
        + " | " + Wo167FtStatsText()
        + $" | copy_weapons={(_w167CopyWeapons ? "on" : "off")} swing_watch={(_w167SwingWatch ? "on" : "off")} sync_fallback={(_w167SyncFallback ? "on" : "off")} shown={Interlocked.Read(ref _w167Shown)} no_anim={Interlocked.Read(ref _w167NoAnim)} verdict_waits={Interlocked.Read(ref _w167VerdictWaits)} shown_unknown={Interlocked.Read(ref _w167ShownUnknown)} sync_fallbacks={Interlocked.Read(ref _w167SyncFallbacks)} sync_no_plain={Interlocked.Read(ref _w167SyncNoPlain)} syncguard_after_row={Interlocked.Read(ref _w167SyncGuardAfterRow)} syncguard_other={Interlocked.Read(ref _w167SyncGuardOther)} pulses={Interlocked.Read(ref _w167Pulses)} weapon_calls={Interlocked.Read(ref _w167WeaponCalls)}";
}
