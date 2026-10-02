// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-151 Phase 0 -- the safeguards' switches, the agent's half (docs/WO-151-findings.md).
//
//   mp_fault_switchoff on|off (default on): the DLL switches a game-code call site off after
//       8 faults in a run (fault_guard.h); off = every site keeps running (the logging is the same).
//   mp_main_cost on|off (default off): the DLL's per-task frame-cost meter (MAIN-COST lines).
//   mp_fault_status / mp_fault_test read|call: the DLL's fault totals; a deliberate fault at a
//       test site (the guard's live check).
//
// The mod says its values in w151_cfg; this pushes them to the DLL on a change and every 10 s
// (a restarted DLL starts from its own defaults), and copies the DLL's fault totals into
// agent.log once a minute while any count is non-zero.
public partial class GameBridge
{
    private volatile bool _w151SwitchOff = true;
    private volatile bool _w151MainCost;
    private volatile bool _w151Connected;
    private int _w151CfgKey = -1;
    private long _w151CfgAtMs;

    // ---- Phase 1.1: a copy in a fight is the host's (mp_copy_fight, joiner) ----------------------
    // A copy is "in a fight" while the host's NPC is in combat (WO-132's NpcCombat), while this
    // joiner holds it in combat mode (WO-132 or WO-147), and for Wo151Rules.FightHoldMs after.
    // In a fight: no activity row and no tool (the field: WO-141 gave tbuk_man_1 a temporary tool
    // mid-fight, 60 applies), and the copy's health goes back to the host's after every local blow.
    private volatile bool _w151CopyFight = true;
    private bool _w151HealthHost => _w151CopyFight;
    private readonly ConcurrentDictionary<string, long> _w151Fight = new(StringComparer.Ordinal);
    private string _w151FightSent = "";
    private long _w151HealthBack, _w151FightStarts, _w151ToolsCleared, _w151HandsHeld;

    // ---- Phase 1.1 stage B: the host NPC's reactions, not the copy's own (mp_npc_reactions) -------
    // Host: an NPC's committed hit reaction (the DLL's C_CombatActorActionHit capture) goes out as
    // NpcHit. Joiner: the copy plays that row, and every guarded copy (WO-131: every live puppet of
    // the host's NPC) runs none of its own reactions -- the game's combat_actorSupressHitreactionAnimation
    // (what its own battles, hostages and tied NPCs use) and the two hit-reaction switches, DLL op 5.
    private volatile bool _w151Reactions = true;

    // ---- Phase 5: joint responsibility (mp_crime_mode joint|individual, default joint) ------------
    // Host: the host's own crime seen by someone (w151_crime own ...) goes into every joiner's record;
    // the host's own crime dialogue ending in a fine or a punishment (w151_crime resolved ...) clears every
    // joiner's record; a joiner's stop that clears his record makes this world forget the host's too.
    private volatile bool _w151CrimeJoint = true;

    // ---- Phase 3.7: the host's live weather ---------------------------------------------------------
    // Host: the DLL records every blend the game runs (EnvironmentModule BlendToProfile); a new one
    // becomes the session's profile and is sent at once (the WO-40 random pick only until the first one).
    // Joiner: the host's profile is applied and gated (only it may blend here), re-applied after a load.
    private uint _w151WeatherCount;
    private volatile bool _w151WeatherNative;      // the host has a reading of its own game's weather
    private string? _w151WeatherGate;              // joiner: the profile the gate lets through
    private long _w151WeatherSends, _w151WeatherGates;
    private long _w151CrimeOwn, _w151CrimeResolved, _w151CrimeForgets;
    private long _w151HitRowsOut, _w151HitRowsIn, _w151HitRowsPlayed, _w151CtxOnCount, _w151CtxOffCount;
    private readonly ConcurrentDictionary<string, uint> _w151CtxOn = new(StringComparer.OrdinalIgnoreCase);

    private bool W151InFight(string name) =>
        _w151CopyFight && _w151Fight.TryGetValue(name, out long at) && Environment.TickCount64 - at < Wo151Rules.FightHoldMs;

    private void Wo151NoteFight(string name, string why)
    {
        if (!_w151CopyFight || string.IsNullOrEmpty(name)) return;
        long now = Environment.TickCount64;
        bool fresh = !_w151Fight.TryGetValue(name, out long at) || now - at >= Wo151Rules.FightHoldMs;
        _w151Fight[name] = now;
        if (fresh)
        {
            long n = Interlocked.Increment(ref _w151FightStarts);
            if (n <= 40) Console.WriteLine($"MP-W151 {name} in a fight ({why}): no activity, no tools, the host's health");
            if (W141Joiner && _w143HostHands.ContainsKey(name))
            {
                // the temporary tools go at once (WO-143 keeps them while a row says so)
                _ = Task.Run(async () =>
                {
                    try { await _combat.Wo143HandsAsync(name, [], []); Interlocked.Increment(ref _w151ToolsCleared); } catch { }
                });
            }
        }
    }

    /// <summary>The 1 s tick: engagements count as a fight; the set goes to the mod when it changes.</summary>
    private async Task Wo151FightTickAsync()
    {
        if (!_w151CopyFight || !W141Joiner) { if (_w151FightSent != "") { _w151FightSent = ""; await ExecLuaAsync("if KCD2MP_W151Fights then KCD2MP_W151Fights(\"\") end"); } return; }
        foreach (var n in _w132Engaged.Keys) Wo151NoteFight(n, "held in combat here (WO-132)");
        foreach (var n in _w147Local.Keys) Wo151NoteFight(n, "held in combat here (WO-147)");
        var names = _w151Fight.Where(kv => Environment.TickCount64 - kv.Value < Wo151Rules.FightHoldMs).Select(kv => kv.Key)
                              .Where(Wo151Rules.IsWireName).OrderBy(s => s, StringComparer.Ordinal).Take(40).ToArray();
        string csv = string.Join(",", names);
        if (csv == _w151FightSent) return;
        _w151FightSent = csv;
        await ExecLuaAsync($"if KCD2MP_W151Fights then KCD2MP_W151Fights(\"{csv}\") end");
    }

    /// <summary>
    /// Phase 1.1 (1 s): every guarded copy holds the "no own hit reaction" contexts; a copy no longer
    /// guarded (released, dead, a load) gives them back. A copy's entity can change under the same name
    /// (a load, a re-spawn): then the old one is released first.
    /// </summary>
    private async Task Wo151CopyContextsAsync()
    {
        var want = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        if (_w151Reactions && _w151CopyFight && Wo131JoinerActive)
            foreach (var kv in _w131Guarded)
                if (kv.Value != 0) want[kv.Key] = kv.Value;
        foreach (var kv in _w151CtxOn.ToArray())
        {
            if (want.TryGetValue(kv.Key, out uint e2) && e2 == kv.Value) continue;
            _w151CtxOn.TryRemove(kv.Key, out _);
            try { await _combat.Wo151CopyFightAsync(kv.Value, false); } catch { }
            Interlocked.Increment(ref _w151CtxOffCount);
        }
        foreach (var kv in want)
        {
            if (_w151CtxOn.ContainsKey(kv.Key)) continue;
            var r = await _combat.Wo151CopyFightAsync(kv.Value, true);
            if (r is null) continue;   // an older DLL, or no soul yet: tried again next second
            _w151CtxOn[kv.Key] = kv.Value;
            long n = Interlocked.Increment(ref _w151CtxOnCount);
            if (n <= 40) Console.WriteLine($"MP-W151 copy {kv.Key} (eid 0x{kv.Value:X}): its own hit reactions off (written {r.Value.Written}, already {r.Value.Already}, failed {r.Value.Failed}) -- the host's reactions play on it");
        }
    }

    /// <summary>Joiner: the host NPC's hit reaction plays on its copy (the native row route, as an NPC swing).</summary>
    private async Task Wo151OnNpcHitInAsync(InboundAction a, ActionRowCatalog? catalog, CancellationToken ct)
    {
        if (!RowEvent.TryFromBytes(a.Payload, out var ne) || ne.Name.Length == 0) return;
        Interlocked.Increment(ref _w151HitRowsIn);
        if (!_w151Reactions || !_npcRows) return;
        if (catalog is null || !catalog.TryGet(ne.Row, out var row))
        {
            Console.WriteLine($"MP-ACTION section=inbound kind=NpcHit npc={ne.Name} row={ne.Row} dispatch=dropped-unknown-row");
            return;
        }
        if (!_npcEntityIds.TryGetValue(ne.Name, out uint neid))
        {
            Console.WriteLine($"MP-ACTION section=inbound kind=NpcHit npc={ne.Name} dispatch=dropped-no-entity (not a puppet here)");
            return;
        }
        Wo151NoteFight(ne.Name, "the host's NPC was hit");
        _ = _combat.NpcHoldAsync(ne.Name, 800, ct);
        var r = await _combat.GhostSwingForResultAsync(neid, row.Spec, ct);
        if (r.Ok) Interlocked.Increment(ref _w151HitRowsPlayed);
        Console.WriteLine($"MP-ACTION section=inbound kind=NpcHit npc={ne.Name} row={ne.Row} table={row.Table} spec=\"{row.Spec}\" dispatch=native-row result={r.ReasonTag}");
        if (r.Ok) await ExecLuaAsync($"if KCD2MP_NpcNativeSwingHold then KCD2MP_NpcNativeSwingHold(\"{ne.Name}\") end");
    }

    private void Wo151OnCfg(string? arg)
    {
        foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (kv.StartsWith("fault_switchoff=", StringComparison.Ordinal)) _w151SwitchOff = kv.EndsWith("=on", StringComparison.Ordinal);
            else if (kv.StartsWith("main_cost=", StringComparison.Ordinal)) _w151MainCost = kv.EndsWith("=on", StringComparison.Ordinal);
            else if (kv.StartsWith("copy_fight=", StringComparison.Ordinal)) _w151CopyFight = kv.EndsWith("=on", StringComparison.Ordinal);
            else if (kv.StartsWith("npc_reactions=", StringComparison.Ordinal)) _w151Reactions = kv.EndsWith("=on", StringComparison.Ordinal);
            else if (kv.StartsWith("crime_mode=", StringComparison.Ordinal)) _w151CrimeJoint = kv.EndsWith("=joint", StringComparison.Ordinal);
            else if (kv.StartsWith("join_hold=", StringComparison.Ordinal)) _w151JoinHold = kv.EndsWith("=on", StringComparison.Ordinal);
            else if (kv.StartsWith("scene_guard=", StringComparison.Ordinal)) _w151SceneGuard = kv.EndsWith("=on", StringComparison.Ordinal);
            else if (kv.StartsWith("minigame_align=", StringComparison.Ordinal)) _w151MinigameAlign = kv.EndsWith("=on", StringComparison.Ordinal);
            else if (kv.StartsWith("quest_catchup=", StringComparison.Ordinal)) _w151CatchUp = kv.EndsWith("=on", StringComparison.Ordinal);
        }
        Console.WriteLine($"MP-W151 cfg fault_switchoff={On(_w151SwitchOff)} main_cost={On(_w151MainCost)} copy_fight={On(_w151CopyFight)} npc_reactions={On(_w151Reactions)} crime_mode={(_w151CrimeJoint ? "joint" : "individual")}");
        _w151CfgKey = -1;   // push at the next tick
        if (_w151Connected) _ = Wo151PushConfigAsync();
    }

    /// <summary>
    /// w151_crime own &lt;kind&gt; &lt;witnesses&gt; &lt;guards&gt; &lt;settlement|-&gt; &lt;guards|-&gt; | resolved &lt;result&gt; (the host, joint).
    /// </summary>
    private async Task Wo151OnCrimeAsync(string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!_w151CrimeJoint || !W139Host || f.Length < 2) return;
        if (f[0] == "own" && f.Length >= 6 && Wo139Text.IsCrime(f[1]) && int.TryParse(f[2], out int wit) && int.TryParse(f[3], out int guards)
            && Wo139Text.IsSettlementOrDash(f[4]) && wit > 0)
        {
            string settlement = f[4] == "-" ? "" : f[4];
            Interlocked.Increment(ref _w151CrimeOwn);
            foreach (byte peer in Wo134Peers())
            {
                var c = new JoinerCrimeRecord.Crime { Id = 0, Kind = f[1], Settlement = settlement, Witnesses = wit, Guards = guards, AtMs = Environment.TickCount64 };
                bool kept = Wo139Record(peer).Add(c);
                Console.WriteLine($"MP-W151 crime joint: the host's own {f[1]} ({wit} witness(es), {guards} guard(s) in {(settlement.Length > 0 ? settlement : "the wilds")}) -- ghost {peer} answers for it too{(kept ? "" : " (merged)")}");
            }
        }
        else if (f[0] == "resolved" && Wo139Text.IsResult(f[1]))
        {
            Interlocked.Increment(ref _w151CrimeResolved);
            foreach (byte peer in Wo134Peers())
            {
                if (!_w139Records.TryGetValue(peer, out var rec)) continue;
                int n = rec.Clear(null);
                foreach (var (g, p) in _w139Pursuits.ToArray()) if (p.Peer == peer) await Wo139EndPursuitAsync(g, "the host's record is cleared (joint)");
                await Wo139SendAsync(Protocol.CrimeHostUp, peer, Protocol.CrimeHostCleared, 0, Wo139Rules.ClearedText("partner-" + f[1], null));
                Console.WriteLine($"MP-W151 crime joint: the host {f[1]} -- ghost {peer}'s record cleared too ({n} crime(s))");
            }
        }
    }

    /// <summary>Host, joint: a joiner's stop cleared his record -- this world forgets the host's Henry's crimes too.</summary>
    private async Task Wo151OnJoinerClearedAsync(byte src, string result)
    {
        if (!_w151CrimeJoint || !W139Host) return;
        Interlocked.Increment(ref _w151CrimeForgets);
        Console.WriteLine($"MP-W151 crime joint: ghost {src} {result} -- this world forgets the host's crimes too");
        await ExecLuaAsync($"if KCD2MP_W151Forget then KCD2MP_W151Forget(300, \"partner-{result}\") end");
    }

    // ---- Phase 3.1 / 3.3: catch-up --------------------------------------------------------------------
    // mp_quest_catchup on|off (default on). Joiner: the mirror holds while the host reloads or a join runs
    // (the field: both host reloads left it on in the world about to be discarded -- requests sent, changes
    // applied there); after it (re)starts, this player's own quest steps wait until the host's checkpoint has
    // been compared in full (every part, every correction applied), then go out only if they still stand;
    // a conversation with a host NPC waits too (a toast). Checkpoint parts that arrive while the queue is busy
    // wait for it (were dropped: 29 of 109 never completed). Host: each State's values since this world's last
    // load -- a joiner's step to a value the host has passed is "already", never applied again; the seen set is
    // re-read after a load (its old values hid exactly the States the reload changed). 3.3: while this
    // player's own scene positions its NPCs (the content is over, the release is not), host changes wait.
    private volatile bool _w151CatchUp = true;
    private volatile bool _w151CaughtUp = true;
    private long _w151CatchUpAskMs, _w151CatchUpFirstPartMs;
    private int _w151CpNParts;
    private readonly HashSet<int> _w151CpDone = new();
    private bool _w151CpComplete;
    private readonly object _w151CpLock = new();
    private readonly ConcurrentDictionary<int, (int NParts, List<Wo137Rules.CheckpointEntry> Entries, long AtMs)> _w151CpStash = new();
    private readonly ConcurrentQueue<QuestChange> _w151HeldAsks = new();
    private readonly ConcurrentDictionary<string, List<int>> _w151Passed = new(StringComparer.Ordinal);
    private long _w151CatchUps, _w151HeldAskSent, _w151HeldAskDropped, _w151PassedVerdicts, _w151StashedParts, _w151SceneDeferred;

    /// <summary>The mirror holds: a world loads (WO-136), or (catch-up on) the host reloads / a join runs.</summary>
    private bool W151MirrorHolding => Wo136Holding || (_w151CatchUp && W137JoinerSession && (_rewinding || _jj is not null));

    /// <summary>Joiner: the mirror (re)started -- nothing of this player's goes out until the checkpoint is compared.</summary>
    private void Wo151CatchUpStart(string why)
    {
        if (!_w151CatchUp) { _w151CaughtUp = true; return; }
        lock (_w151CpLock) { _w151CpDone.Clear(); _w151CpNParts = 0; _w151CpComplete = false; }
        _w151CpStash.Clear();
        _w151CaughtUp = false;
        _w151CatchUpAskMs = Environment.TickCount64; _w151CatchUpFirstPartMs = 0;
        Console.WriteLine($"MP-W151 catch-up: the mirror {why} -- this player's quest steps and conversations with the host's people wait until the host's checkpoint is compared");
        _ = ExecLuaAsync("if KCD2MP_W151CaughtUp then KCD2MP_W151CaughtUp(false) end");
    }

    /// <summary>Joiner: one checkpoint part compared (in full).</summary>
    private void Wo151CatchUpPartDone(int part, int nparts)
    {
        lock (_w151CpLock)
        {
            if (_w151CatchUpFirstPartMs == 0) _w151CatchUpFirstPartMs = Environment.TickCount64;
            if (part == 1 || nparts != _w151CpNParts) { _w151CpDone.Clear(); _w151CpNParts = nparts; }
            _w151CpDone.Add(part);
            if (_w151CpDone.Count >= nparts) _w151CpComplete = true;
        }
    }

    /// <summary>Joiner, 1 s: caught up once the checkpoint is compared in full and every correction applied.</summary>
    private async Task Wo151CatchUpTickAsync()
    {
        if (_w151CaughtUp || !W137Joiner) return;
        long now = Environment.TickCount64;
        bool complete; lock (_w151CpLock) complete = _w151CpComplete;
        string? how = null;
        if (complete && _w137Queue.Count == 0 && _w137Asked.IsEmpty) how = "the host's checkpoint compared in full";
        else if (_w151CatchUpFirstPartMs == 0 && now - _w151CatchUpAskMs > Wo151Rules.CatchUpFirstPartMs) how = "the host sent no checkpoint (nothing changed there to compare)";
        else if (now - _w151CatchUpAskMs > Wo151Rules.CatchUpMaxMs) how = "INCOMPLETE after 60 s (released anyway: the next checkpoint compares the rest)";
        if (how is null) return;
        _w151CaughtUp = true;
        Interlocked.Increment(ref _w151CatchUps);
        int sent = 0, dropped = 0;
        var held = new List<QuestChange>();
        while (_w151HeldAsks.TryDequeue(out var c)) held.Add(c);
        if (held.Count > 0)
        {
            var reads = await _combat.Wo137ReadStatesAsync(held.Select(c => c.Path).ToList());
            for (int i = 0; i < held.Count; i++)
            {
                var c = held[i];
                bool stands = reads is not null && reads.Count == held.Count && reads[i].Found && reads[i].Ok && reads[i].Val == c.New;
                if (stands) { sent++; await Wo137SendRequestAsync(c); }
                else { dropped++; Console.WriteLine(FormattableString.Invariant($"MP-W151 catch-up: {c.Path} {c.Old}->{c.New} not sent -- the catch-up put this copy elsewhere (the host's value stands)")); }
            }
        }
        Interlocked.Add(ref _w151HeldAskSent, sent); Interlocked.Add(ref _w151HeldAskDropped, dropped);
        Console.WriteLine(FormattableString.Invariant($"MP-W151 catch-up: caught up ({how}, {(now - _w151CatchUpAskMs) / 1000.0:F1} s); {sent} held step(s) sent, {dropped} dropped"));
        await ExecLuaAsync("if KCD2MP_W151CaughtUp then KCD2MP_W151CaughtUp(true) end");
    }

    /// <summary>Joiner: a part that arrived while the queue was busy waits for it (the drain loop calls this when it is empty).</summary>
    private async Task Wo151ProcessStashedPartsAsync()
    {
        if (_w151CpStash.IsEmpty || _w137Queue.Count > 0 || !_w137Asked.IsEmpty) return;
        long now = Environment.TickCount64;
        foreach (var part in _w151CpStash.Keys.OrderBy(k => k).ToList())
        {
            if (!_w151CpStash.TryRemove(part, out var p)) continue;
            if (now - p.AtMs > 120_000) continue;   // a newer checkpoint has come since
            await Wo137CompareCheckpointAsync(part, p.NParts, p.Entries);
            if (_w137Queue.Count > 0) break;        // its corrections first; the rest after them
        }
    }

    /// <summary>Host: a State's change since this world's last load (the values it leaves behind).</summary>
    private void Wo151NoteHostValue(string path, int left)
    {
        if (_w151Passed.Count > 4000) _w151Passed.Clear();   // bounded; a fresh history is the old rule (refused)
        var l = _w151Passed.GetOrAdd(path, _ => new List<int>());
        lock (l)
        {
            if (!l.Contains(left)) l.Add(left);
            if (l.Count > 32) l.RemoveAt(0);
        }
    }

    private IReadOnlyCollection<int>? Wo151PassedOf(string path, int current)
    {
        if (!_w151Passed.TryGetValue(path, out var l)) return null;
        lock (l) return l.Where(v => v != current).ToList();
    }

    /// <summary>Host: this world loaded -- each State's history starts again, the seen set takes the loaded values.</summary>
    private async Task Wo151HostRebaseAsync()
    {
        _w151Passed.Clear();
        if (!W137Host || _w137HostSeen.IsEmpty) return;
        var paths = _w137HostSeen.Keys.ToList();
        int changed = 0;
        for (int i = 0; i < paths.Count; i += 500)
        {
            var batch = paths.Skip(i).Take(500).ToList();
            var reads = await _combat.Wo137ReadStatesAsync(batch);
            if (reads is null || reads.Count != batch.Count) continue;
            for (int j = 0; j < batch.Count; j++)
            {
                if (!reads[j].Found || !reads[j].Ok) { _w137HostSeen.TryRemove(batch[j], out _); continue; }
                if (_w137HostSeen.TryGetValue(batch[j], out var s) && s.Val != reads[j].Val)
                {
                    _w137HostSeen[batch[j]] = (reads[j].Val, "", Environment.TickCount64);
                    changed++;
                }
            }
        }
        _w137HostSeenDirty = true;
        Console.WriteLine($"MP-W151 catch-up: this world loaded -- {changed} of {paths.Count} seen quest State(s) take the loaded value; the next checkpoint compares them (the field's reload hid them for 41 min)");
    }

    /// <summary>3.3: this player's own scene positions its NPCs (content over, release not yet): host changes wait.</summary>
    private bool Wo151OwnScenePositioning()
    {
        if (!_w151CatchUp || _w151Scenes.IsEmpty) return false;
        long now = Environment.TickCount64;
        foreach (var s in _w151Scenes.Values)
            if (s.EndAtMs > 0 && !s.Positioned && now - s.EndAtMs < (long)(SceneMaxS * 1000)) return true;
        return false;
    }

    // ---- Phase 3.4: the scene guard ----------------------------------------------------------------
    // The joiner is never left on a black screen. After a scene's content ends the engine positions its NPCs
    // (the end fast-forward); on a joiner they are the host's paused copies and never arrive (the field: 41-88 s,
    // two never). The guard: (1) the end edge at the engine's release; (2) on a joiner, the scene's copies
    // (the paused copies near this player) resumed SceneResumeAfterS after the content's end, held until the
    // release; (3) still not positioned SceneRescueAfterS after it: the engine's own way out -- a save request
    // ("Cancelling FF for all NPCs", which released every field case), through the WO-125 snapshot path, the
    // file moved out at once; (4) SceneMaxS after it: given up, everything handed back, the end edge sent.
    // A load resets all of it. mp_scene_guard on|off (the Lua half also resumes a copy a forced dialogue runs on).
    private volatile bool _w151SceneGuard = true;
    // 4.2/4.3: an avatar's minigame never aligns at the station's object (mp_minigame_align on = WO-143's aligned entry)
    private volatile bool _w151MinigameAlign;
    public const double SceneResumeAfterS = 5, SceneRescueAfterS = 20, SceneMaxS = 90;
    private sealed class W151Scene
    {
        public string Type = "", Name = "";
        public long PlayAtMs, EndAtMs;
        public bool Positioned, Resumed, Rescued;
    }
    private readonly ConcurrentDictionary<string, W151Scene> _w151Scenes = new(StringComparer.Ordinal);
    private long _w151SceneEnds, _w151SceneResumes, _w151SceneRescues, _w151SceneGiveUps, _w151SceneReleases;

    private void Wo151OnSceneStage(string stage, string type, string name)
    {
        long now = Environment.TickCount64;
        switch (stage)
        {
            case "PlayCutscene":
                _w151Scenes[name] = new W151Scene { Type = type, Name = name, PlayAtMs = now };
                break;
            case "OnCutsceneEnd":
            {
                var s = _w151Scenes.GetOrAdd(name, _ => new W151Scene { Type = type, Name = name, PlayAtMs = now });
                s.EndAtMs = now;
                Interlocked.Increment(ref _w151SceneEnds);
                Console.WriteLine($"MP-W151 scene {type} '{name}' content ended -- the engine positions its NPCs now{(_w151SceneGuard ? "; the end edge waits for the release" : "")}");
                break;
            }
            case "OnPositioningFinished":
                if (_w151Scenes.TryGetValue(name, out var ps))
                {
                    ps.Positioned = true;
                    Console.WriteLine(FormattableString.Invariant($"MP-W151 scene {type} '{name}' positioned {(ps.EndAtMs > 0 ? (now - ps.EndAtMs) / 1000.0 : 0):F1} s after its content{(ps.Resumed ? " (its copies resumed)" : "")}{(ps.Rescued ? " (after the rescue save)" : "")}"));
                }
                break;
            case "ReleaseScene":
            case "Interrupt":
                _w151Scenes.TryRemove(name, out var rs);
                Interlocked.Increment(ref _w151SceneReleases);
                if (rs is not null && (rs.Resumed || stage == "Interrupt" || rs.EndAtMs > 0))
                    Console.WriteLine(FormattableString.Invariant($"MP-W151 scene {type} '{name}' {(stage == "Interrupt" ? "interrupted" : "released")}{(rs.EndAtMs > 0 ? $" {(now - rs.EndAtMs) / 1000.0:F1} s after its content" : "")}"));
                if (rs is { Resumed: true }) _ = ExecLuaAsync("if KCD2MP_W151SceneRelease then KCD2MP_W151SceneRelease(\"" + stage + "\") end");
                if (_w151SceneGuard) ApplyLocalCutsceneEdge(false, type, name);   // the end edge, at the engine's release
                break;
            case "PositioningLate":
                Console.WriteLine($"MP-W151 scene: the engine's positioning took {name} s (\"took longer than expected\")");
                break;
            case "FFCancelled":
                Console.WriteLine("MP-W151 scene: the engine cancelled every NPC's fast-forward (a save request) -- the positioning ends now");
                break;
        }
    }

    /// <summary>1 s: a scene's end positioning that does not finish -- resume, rescue, give up (the joiner only).</summary>
    private async Task Wo151SceneTickAsync()
    {
        if (_w151Scenes.IsEmpty) return;
        long now = Environment.TickCount64;
        foreach (var s in _w151Scenes.Values.ToArray())
        {
            if (s.EndAtMs == 0)
            {
                if (now - s.PlayAtMs > 30 * 60_000) _w151Scenes.TryRemove(s.Name, out _);   // a start whose end was never logged
                continue;
            }
            double since = (now - s.EndAtMs) / 1000.0;
            if (since >= SceneMaxS)
            {
                _w151Scenes.TryRemove(s.Name, out _);
                Interlocked.Increment(ref _w151SceneGiveUps);
                Console.WriteLine(FormattableString.Invariant($"MP-W151 scene {s.Type} '{s.Name}' not released {since:F0} s after its content -- the guard gives up: copies handed back, the end edge sent"));
                if (s.Resumed) await ExecLuaAsync("if KCD2MP_W151SceneRelease then KCD2MP_W151SceneRelease(\"gave-up\") end");
                if (_w151SceneGuard) ApplyLocalCutsceneEdge(false, s.Type, s.Name);
                continue;
            }
            if (s.Positioned || !_w151SceneGuard || !W137Joiner) continue;
            if (!s.Resumed && since >= SceneResumeAfterS)
            {
                s.Resumed = true;
                Interlocked.Increment(ref _w151SceneResumes);
                Console.WriteLine(FormattableString.Invariant($"MP-W151 scene {s.Type} '{s.Name}' not positioned {since:F0} s after its content -- its copies (the paused NPCs near this player) are resumed until the release"));
                await ExecLuaAsync($"if KCD2MP_W151SceneResume then KCD2MP_W151SceneResume(\"{s.Name}\", 40) end");
            }
            if (!s.Rescued && since >= SceneRescueAfterS)
            {
                s.Rescued = true;
                Interlocked.Increment(ref _w151SceneRescues);
                Console.WriteLine(FormattableString.Invariant($"MP-W151 scene {s.Type} '{s.Name}' still not positioned {since:F0} s after its content -- the rescue: a save request (the engine cancels every NPC's fast-forward)"));
                _ = Wo151SceneRescueSaveAsync(s.Name);
            }
        }
    }

    /// <summary>
    /// The rescue: a QuickSave through the WO-125 snapshot path (the one save type that passes the joiner's
    /// lock; the engine cancels every NPC's fast-forward for it); the file is a copy of the host's world in
    /// this player's playline and is moved out at once, exactly like a snapshot's. Nothing is paired or stored.
    /// </summary>
    private async Task Wo151SceneRescueSaveAsync(string scene)
    {
        string skip = !_joinedWorld ? "not in the host's world" : _jj is not null ? "a join is running" : _rewinding ? "the host is reloading"
                    : _joinedTag is null ? "the world is not known" : _where != GameWhere.World ? "not in the world" : "";
        if (skip != "") { Console.WriteLine($"MP-W151 scene '{scene}': no rescue save -- {skip}"); return; }
        if (Interlocked.CompareExchange(ref _snapBusy, 1, 0) != 0) { Console.WriteLine($"MP-W151 scene '{scene}': no rescue save -- a snapshot is running (it releases the positioning too)"); return; }
        try
        {
            string? saves = ResolveSavesDirForJoin();
            int pl = _lastWorldPlayline;
            if (saves is null || pl < 0) { Console.WriteLine($"MP-W151 scene '{scene}': no rescue save -- the playline of this world is not known"); return; }
            string dir = Path.Combine(saves, $"playline{pl}");
            int qs = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "quicksave*.whs").Count() : 0;
            if (qs >= SnapshotQuicksaveGuard) { Console.WriteLine($"MP-W151 scene '{scene}': no rescue save -- playline{pl} holds {qs} quicksaves"); return; }
            await _combat.SaveListAsync(1, pl, "-");
            var led = _henry.LedgerAdd(new HenryStore.LedgerEntry { Playline = pl, SinceUtc = DateTime.UtcNow.AddSeconds(-1), WorldTag = _joinedTag!, Kind = "scene-rescue" });
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_snapGate) _snapWait = (pl, DateTime.UtcNow.AddSeconds(-1), tcs);
            string r = await AskModAsync("KCD2MP_Wo125Snapshot", 6000);
            if (!r.StartsWith("ok=true", StringComparison.Ordinal))
            {
                Console.WriteLine($"MP-W151 scene '{scene}': the engine REFUSED the rescue save ({r})");
                _henry.LedgerSet(led.Id, e => e.State = "done");
                return;
            }
            if (await Task.WhenAny(tcs.Task, Task.Delay(20000)) != tcs.Task)
            {
                Console.WriteLine($"MP-W151 scene '{scene}': no rescue save file within 20 s (the ledger sweeps it later)");
                return;
            }
            string path = tcs.Task.Result;
            bool gone = _henry.MoveOut(path, "the scene guard's rescue QuickSave (the host's world)");
            await _combat.SaveListAsync(1, pl, Path.GetFileNameWithoutExtension(path));
            if (gone) _henry.LedgerSet(led.Id, e => e.State = "done");
            Console.WriteLine($"MP-W151 scene '{scene}': rescue save {SaveDisplay(path)} written and moved out={On(gone)}");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W151 scene '{scene}': rescue save failed: {ex.GetType().Name}: {ex.Message}"); }
        finally
        {
            lock (_snapGate) _snapWait = null;
            Volatile.Write(ref _snapBusy, 0);
        }
    }

    /// <summary>A load or the session's end: nothing of a scene survives it (the engine interrupts its scenes).</summary>
    private void Wo151SceneReset(string why)
    {
        int n = _w151Scenes.Count;
        _w151Scenes.Clear();
        bool was = _localCutsceneActive;
        if (_localCutsceneActive) { _localCutsceneActive = false; _localCutsceneName = ""; _ = ExecLuaAsync("if KCD2MP_SetCutscene then KCD2MP_SetCutscene(false, \"\") end"); }
        _ = ExecLuaAsync($"if KCD2MP_W151SceneRelease then KCD2MP_W151SceneRelease(\"{why}\", true) end");
        if (n > 0 || was) Console.WriteLine($"MP-W151 scene reset ({why}): {n} scene(s) forgotten{(was ? ", the local cutscene flag cleared" : "")}");
    }

    // ---- Phase 3.5: the whistle ----------------------------------------------------------------------
    private long _w151EmotesOut, _w151EmotesIn;

    private async Task Wo151OnEmoteLocalAsync(string? arg)
    {
        if (arg?.Trim() != "whistle" || _wo121Stream is not Stream s || LivePartners().Count == 0) return;
        var b = new byte[EmoteId.PayloadLen];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b, SenderMsNow());
        b[4] = EmoteId.Whistle;
        await WritePacketAsync(s, _actionOut.Build(ActionKind.Emote, ActionPhase.Commit, b), _wo121Ct);
        long n = Interlocked.Increment(ref _w151EmotesOut);
        if (n <= 20) Console.WriteLine($"MP-W151 emote out whistle [n={n}]");
    }

    private async Task Wo151OnEmoteInAsync(InboundAction a)
    {
        if (a.Payload.Length < EmoteId.PayloadLen || a.Payload[4] != EmoteId.Whistle) { Console.WriteLine($"MP-W151 emote in from ghost {a.SourceGhostId}: dropped (len={a.Payload.Length})"); return; }
        long n = Interlocked.Increment(ref _w151EmotesIn);
        if (n <= 20) Console.WriteLine($"MP-W151 emote in from ghost {a.SourceGhostId}: {EmoteId.Name(a.Payload[4])} [n={n}]");
        await ExecLuaAsync($"if KCD2MP_W151PlayEmote then KCD2MP_W151PlayEmote({a.SourceGhostId}, \"whistle\") end");
    }

    // ---- Phase 3.9: doors ---------------------------------------------------------------------------
    // The host's world owns every door. Lua emits "w151_door state|ask <name> <dir> <flag> <x> <y> <z>"
    // (state on the host for every real change, ask on a joiner for his own use); they go out as
    // DoorState / DoorAsk. Inbound DoorState on a joiner and DoorAsk on the host go to Lua.
    private long _w151DoorOut, _w151DoorIn, _w151DoorAsksOut, _w151DoorAsksIn, _w151DoorDropped;

    /// <summary>Lua: a door changed (host) or this joiner used one -- DoorState / DoorAsk out.</summary>
    private async Task Wo151OnDoorLocalAsync(string? arg)
    {
        var p = Wo151Rules.ParseDoorEvent(arg, SenderMsNow());
        if (p is not { } d || _wo121Stream is not Stream s) { Interlocked.Increment(ref _w151DoorDropped); return; }
        if (d.Ask ? (_isDamageAuthority || !W137Joiner) : !W137Host) { Interlocked.Increment(ref _w151DoorDropped); return; }
        if (!d.Ask && LivePartners().Count == 0) return;   // nobody to tell (the next join's save carries it)
        await WritePacketAsync(s, _actionOut.Build(d.Ask ? ActionKind.DoorAsk : ActionKind.DoorState, ActionPhase.Commit, d.Ev.ToBytes()), _wo121Ct);
        long n = d.Ask ? Interlocked.Increment(ref _w151DoorAsksOut) : Interlocked.Increment(ref _w151DoorOut);
        if (d.Ask || n <= 20 || n % 100 == 0) Console.WriteLine($"MP-W151 door {(d.Ask ? "ask" : "state")} out {d.Ev} [n={n}]");
    }

    /// <summary>A door from the relay: the host's state (joiner) or a joiner's ask (host).</summary>
    private async Task Wo151OnDoorInAsync(InboundAction a)
    {
        if (!DoorEvent.TryFromBytes(a.Payload, out var e)) { Interlocked.Increment(ref _w151DoorDropped); Console.WriteLine($"MP-W151 door in: dropped-malformed len={a.Payload.Length}"); return; }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string pos = string.Format(inv, "{0:F2}, {1:F2}, {2:F2}", e.X, e.Y, e.Z);
        int flag = (e.Flags & DoorEvent.FlagLocked) != 0 ? 1 : 0;
        if (a.Kind == ActionKind.DoorState)
        {
            if (_isDamageAuthority || !W137Joiner) { Interlocked.Increment(ref _w151DoorDropped); return; }   // a host's door is its own; not in the host's world yet
            long n = Interlocked.Increment(ref _w151DoorIn);
            if (n <= 20 || n % 100 == 0) Console.WriteLine($"MP-W151 door state in {e} [n={n}]");
            await ExecLuaAsync($"if KCD2MP_W151DoorApply then KCD2MP_W151DoorApply(\"{e.Name}\", {e.Dir.ToString(inv)}, {flag}, {pos}) end");
        }
        else
        {
            if (!W137Host) { Interlocked.Increment(ref _w151DoorDropped); return; }
            Interlocked.Increment(ref _w151DoorAsksIn);
            Console.WriteLine($"MP-W151 door ask in from ghost {a.SourceGhostId}: {e}");
            await ExecLuaAsync($"if KCD2MP_W151DoorAsked then KCD2MP_W151DoorAsked({a.SourceGhostId}, \"{e.Name}\", {e.Dir.ToString(inv)}, {flag}, {pos}) end");
        }
    }

    // ---- Phase 3.8: the host's world held for the whole join ----------------------------------------
    private volatile bool _w151JoinHold = true;   // mp_join_hold on|off (mirrors KCD2MP.w151.joinHold)
    private long _w151JoinHolds, _w151JoinHoldFails;

    private async Task Wo151JoinHoldAsync(HostJoin j, bool on)
    {
        if (on && !_w151JoinHold) return;
        if (!on && !j.EngineHeld) return;
        // the DLL's own deadline: the join's timeout and a margin (it releases the world itself after that)
        ushort maxS = (ushort)Math.Clamp(_joinTimeoutS + 60, 60, 900);
        bool? held = null;
        try { held = await _combat.Wo151JoinHoldAsync(on, maxS); } catch { }
        if (on)
        {
            j.EngineHeld = held == true;
            if (j.EngineHeld) Interlocked.Increment(ref _w151JoinHolds); else Interlocked.Increment(ref _w151JoinHoldFails);
            Console.WriteLine($"MP-JOIN host: join 0x{j.JoinId:x8}: the world {(j.EngineHeld ? $"is held by the engine for the join (at most {maxS} s)" : "could NOT be held by the engine (the mod's own freeze only)")}");
        }
        else
        {
            j.EngineHeld = false;
            Console.WriteLine($"MP-JOIN host: join 0x{j.JoinId:x8}: the engine's hold {(held is not null ? "released" : "release unanswered (the DLL's deadline releases it)")}");
        }
    }

    /// <summary>Host (2 s): a new blend of the host's own game is the session's weather.</summary>
    private async Task Wo151WeatherTickAsync()
    {
        if (!config.WeatherSyncEnabled || !_isDamageAuthority) return;
        var r = await _combat.Wo151WeatherReadAsync();
        if (r is not { } w || w.Count == 0 || w.Count == _w151WeatherCount || !WeatherNamePattern.IsMatch(w.Name)) return;
        _w151WeatherCount = w.Count;
        bool first = !_w151WeatherNative;
        _w151WeatherNative = true;
        if (w.Name == _sessionWeatherProfile) return;
        _sessionWeatherProfile = w.Name;
        _lastAppliedWeatherProfile = w.Name;           // the host's game is already there
        _weatherNextHeartbeatUtc = DateTime.MinValue;   // sent at the next arbiter check (5 s)
        Interlocked.Increment(ref _w151WeatherSends);
        Console.WriteLine($"[weather] the host's own game blended to '{w.Name}' -- the session's weather{(first ? " (the first reading: the random pick stops)" : "")}");
    }

    /// <summary>Joiner: the host's weather arrived -- the gate lets only it blend here.</summary>
    private async Task Wo151OnHostWeatherAsync(string profile)
    {
        if (_isDamageAuthority || _w151WeatherGate == profile) return;
        if (await _combat.Wo151WeatherGateAsync(profile))
        {
            _w151WeatherGate = profile;
            Interlocked.Increment(ref _w151WeatherGates);
            Console.WriteLine($"[weather] gate: only the host's '{profile}' may blend here");
        }
    }

    /// <summary>Any world load: the applied memory goes (a load restores the save's own weather); a joiner re-applies the host's.</summary>
    private void Wo151OnGameplayStarted()
    {
        Wo151SceneReset("load");   // 3.4: a load interrupts every scene (the field's two never-released ones)
        if (_w151CatchUp && W137Host) _ = Task.Run(async () => { await Task.Delay(2000); await Wo151HostRebaseAsync(); });   // 3.1
        _lastAppliedWeatherProfile = null;
        if (!_isDamageAuthority && _w151WeatherGate is string g)
            _ = Task.Run(async () => { await Task.Delay(3000); await ApplyWeatherAsync(g, WeatherBlendSeconds); });
    }

    private void Wo151OnConnect(CancellationToken ct)
    {
        _w151Connected = true;
        _w151CfgKey = -1;
        _ = Wo151LoopAsync(ct);
    }

    private async Task Wo151LoopAsync(CancellationToken ct)
    {
        long lastStatus = Environment.TickCount64;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); } catch { break; }
            try
            {
                await Wo151PushConfigAsync();
                await Wo151FightTickAsync();
                await Wo151CopyContextsAsync();
                await Wo151WeatherTickAsync();
                await Wo151SceneTickAsync();
                await Wo151CatchUpTickAsync();
                long now = Environment.TickCount64;
                if (now - lastStatus >= 60_000)
                {
                    lastStatus = now;
                    string? s = await _combat.Wo151StatusAsync(ct);
                    if (s is not null && !s.StartsWith("faults=0 ", StringComparison.Ordinal)) Console.WriteLine($"MP-W151 dll {s}");
                    if (_w151HitRowsOut + _w151HitRowsIn + _w151CtxOnCount > 0)
                        Console.WriteLine($"MP-W151-STATS hit_rows_out={_w151HitRowsOut} hit_rows_in={_w151HitRowsIn} hit_rows_played={_w151HitRowsPlayed} copy_ctx_on={_w151CtxOnCount} copy_ctx_off={_w151CtxOffCount} fights={_w151FightStarts} health_back={_w151HealthBack} tools_cleared={_w151ToolsCleared} hands_held={_w151HandsHeld}");
                }
            }
            catch (Exception ex) { Console.WriteLine($"MP-W151 tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
        _w151Connected = false;
        Wo151SceneReset("disconnect");
        // the session is over: the weather gate opens (the DLL also opens it when the pipe closes)
        if (_w151WeatherGate is not null) { _w151WeatherGate = null; try { await _combat.Wo151WeatherGateAsync(null); } catch { } }
        // the session is over: every copy gets its own reactions back
        foreach (var kv in _w151CtxOn.ToArray())
        {
            _w151CtxOn.TryRemove(kv.Key, out _);
            try { await _combat.Wo151CopyFightAsync(kv.Value, false); } catch { }
        }
    }

    private async Task Wo151PushConfigAsync()
    {
        int key = (_w151SwitchOff ? 1 : 0) | (_w151MainCost ? 2 : 0);
        long now = Environment.TickCount64;
        if (key == _w151CfgKey && now - _w151CfgAtMs < 10_000) return;
        bool changed = key != _w151CfgKey;
        var r = await _combat.Wo151ConfigAsync(_w151SwitchOff, _w151MainCost);
        if (r is null) return;   // an older DLL, or none: nothing to switch
        _w151CfgKey = key; _w151CfgAtMs = now;
        if (changed) Console.WriteLine($"MP-W151 dll fault_switchoff={On(r.Value.SwitchOff)} main_cost={On(r.Value.MainCost)}");
    }

    private async Task Wo151OnEventAsync(string? arg)
    {
        string a = (arg ?? "").Trim();
        if (a == "status")
        {
            string? s = await _combat.Wo151StatusAsync();
            Console.WriteLine($"MP-W151 status {s ?? "(no answer from the DLL)"}");
            _ = ExecLuaAsync($"System.LogAlways('[KCD2-MP] WO151-STATUS dll {Esc(s ?? "no answer")}')");
        }
        else if (a.StartsWith("testfault ", StringComparison.Ordinal))
        {
            byte kind = a.EndsWith("call", StringComparison.Ordinal) ? (byte)1 : (byte)0;
            var r = await _combat.Wo151TestFaultAsync(kind);
            string line = r is { } x
                ? $"testfault {(kind == 1 ? "call" : "read")}: ran={x.Ran} site_faults={x.Faults} switched_off={x.Off}"
                : "testfault: no answer from the DLL";
            Console.WriteLine($"MP-W151 {line}");
            _ = ExecLuaAsync($"System.LogAlways('[KCD2-MP] WO151-TEST {Esc(line)}')");
        }
        else if (a.StartsWith("copyfight ", StringComparison.Ordinal))
        {
            // copyfight <eidHex> on|off: DLL op 5 on any body (a copy's "no own hit reaction" contexts, tested solo)
            var p = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 3 || !uint.TryParse(p[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint eid) || eid == 0)
            { Console.WriteLine($"MP-W151 copyfight: malformed '{a}'"); return; }
            var r = await _combat.Wo151CopyFightAsync(eid, p[2] == "on");
            string line = r is { } x ? $"copyfight 0x{eid:X} {p[2]}: written {x.Written}, already {x.Already}, failed {x.Failed}" : $"copyfight 0x{eid:X}: refused (no soul, the player, or an older DLL)";
            Console.WriteLine($"MP-W151 {line}");
            _ = ExecLuaAsync($"System.LogAlways('[KCD2-MP] WO151-TEST {Esc(line)}')");
        }
        else if (a.StartsWith("row ", StringComparison.Ordinal))
        {
            // row <eidHex> <rowGuid>: a catalog row played on a body by the native row route -- what a
            // joiner's copy does with the host's NpcHit / NpcAttack (the live check of the replay)
            var p = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 3 || !uint.TryParse(p[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint eid) || eid == 0
                || !Guid.TryParse(p[2], out var g))
            { Console.WriteLine($"MP-W151 row: malformed '{a}'"); return; }
            var catalog = await _rowCatalog;
            string line;
            if (catalog is null || !catalog.TryGet(g, out var row)) line = $"row {g}: not in the catalog";
            else
            {
                var r = await _combat.GhostSwingForResultAsync(eid, row.Spec);
                line = $"row {g} ({row.Table}) \"{row.Spec}\" on 0x{eid:X}: {r.ReasonTag}";
            }
            Console.WriteLine($"MP-W151 {line}");
            _ = ExecLuaAsync($"System.LogAlways('[KCD2-MP] WO151-TEST {Esc(line)}')");
        }
        else if (a.StartsWith("takedamage ", StringComparison.Ordinal))
        {
            // takedamage <victimEidHex|0> <attackerEidHex|0> <hp> <st> <mode 0..3>: the live check of which
            // TakeDamage call plays the victim's hit reaction (Phase 1.2 / 1.3)
            var p = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 6 || !uint.TryParse(p[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint veid)
                || !uint.TryParse(p[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint aeid)
                || !float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float hp)
                || !float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float st)
                || !byte.TryParse(p[5], out byte mode) || mode > 3)
            { Console.WriteLine($"MP-W151 takedamage: malformed '{a}'"); return; }
            var r = await _combat.Wo151TestTakeDamageAsync(veid, aeid, hp, st, mode);
            string line = r is { } x
                ? FormattableString.Invariant($"takedamage victim=0x{veid:X} attacker=0x{aeid:X} mode={mode}: {(x.Ok ? "taken" : $"refused (reason {x.Reason})")} health {x.Before:F1} -> {x.After:F1}")
                : "takedamage: no answer from the DLL";
            Console.WriteLine($"MP-W151 {line}");
            _ = ExecLuaAsync($"System.LogAlways('[KCD2-MP] WO151-TEST {Esc(line)}')");
        }

        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
