// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-154: the agent's half of this WO (docs/WO-154-findings.md). The rules are Wo154Rules in Wo154.cs.
//
// Phase 1 -- a joiner's quest steps made by an AI behaviour (a duel, a brawl: the worker flag) on one State are
// merged before the host judges them, and judged as their net result (mp_quest_coalesce, default on).
//
// Phase 2 -- a knocked-down partner falls on this screen too. The partner's DLL sets the Downed bit of his
// body-state block while his own body is down (its physics is no living entity: a knockdown's ragdoll, a
// knockout) and debounces it there. Its edges reach the mod here: the avatar falls where it stands, lies there
// with its writer held, and stands up when he does. A death or an execution still hides the avatar at the
// death spot (WO-113/132): only the PlayerState knockdown flag keeps it visible. mp_avatar_falls on|off
// (default on).
public partial class GameBridge
{
    // ---- Phase 1: a joiner's AI-behaviour steps, merged before the host judges them (Wo154Rules.WorkerCoalescer) ----
    private volatile bool _w154Coalesce = true;   // mp_quest_coalesce on|off
    private readonly Wo154Rules.WorkerCoalescer _w154Coalescer = new();
    private static readonly AsyncLocal<List<uint>?> _w154AlsoToks = new();
    private long _w154Merged;

    /// <summary>Host: a joiner's step made by an AI behaviour waits to be merged with the next ones on its State.</summary>
    private bool Wo154CoalesceWorkerRequest(byte src, uint tok, QuestChange req, string head)
    {
        if (!_w154Coalesce || (req.Flags & QuestChange.FWorker) == 0) return false;
        bool merged = _w154Coalescer.Add(src, tok, req, Environment.TickCount64);
        if (merged) Interlocked.Increment(ref _w154Merged);
        Console.WriteLine($"{head}: an AI behaviour's step -- {(merged ? "merged with the steps before it" : "waits 1.5 s for the steps after it")} (judged as one)");
        return true;
    }

    /// <summary>Host, the 1 s loop: the merged States whose wait is over are judged, in the order they came.</summary>
    private void Wo154FlushCoalesced(bool all)
    {
        foreach (var x in _w154Coalescer.TakeReady(Environment.TickCount64, all))
        {
            var merged = Wo154Rules.WorkerCoalescer.Merged(x);
            var more = x.Toks.Skip(1).ToList();
            Console.WriteLine(FormattableString.Invariant(
                $"MP-W154 host: {x.Path}: {x.Toks.Count} step(s) of an AI behaviour merged -> {x.FirstOld}->{x.LastNew} ({x.LastPort}), judged now"));
            Wo137Post(async () =>
            {
                _w154AlsoToks.Value = more.Count > 0 ? more : null;
                try { await Wo137HostRequestAsync(x.Src, x.Toks[0], merged); }
                finally { _w154AlsoToks.Value = null; }
            });
        }
    }

    private volatile bool _w154AvatarFalls = true;
    private readonly ConcurrentDictionary<byte, Wo154Rules.DownEdge> _w154PeerDown = new();
    private long _w154Falls, _w154Rises;

    /// <summary>Every inbound body-state block of a partner (both roles).</summary>
    private void Wo154OnPeerState2(byte ghost, BodyState2 st)
    {
        var edge = _w154PeerDown.GetOrAdd(ghost, _ => new Wo154Rules.DownEdge());
        bool? change;
        lock (edge) change = edge.Feed((st.Bits & BodyState2Bits.Downed) != 0, Environment.TickCount64);
        if (change is not bool down) return;
        if (!_w154AvatarFalls)
        {
            Console.WriteLine($"MP-W154 peer {ghost} {(down ? "is down" : "is up again")} -- mp_avatar_falls off: its avatar stays as it is");
            return;
        }
        if (down) Interlocked.Increment(ref _w154Falls); else Interlocked.Increment(ref _w154Rises);
        Console.WriteLine($"MP-W154 peer {ghost} {(down ? "is DOWN -> its avatar falls where it stands and lies there" : "is up again -> its avatar stands up")}");
        _ = ExecLuaAsync($"if KCD2MP_W154AvatarDowned then KCD2MP_W154AvatarDowned(\"{ghost}\", {B(down)}) end");
    }

    /// <summary>A partner left: his edge state goes (a rejoin starts from up).</summary>
    private void Wo154ForgetPeer(byte ghost)
    {
        _w154PeerDown.TryRemove(ghost, out _);
        _w154RideFeed.TryRemove(ghost, out _);
        _w154Respite.TryRemove(ghost, out _);
        _w154RespiteTold.TryRemove(ghost, out _);
    }

    // ---- Phase 3: fighting together -----------------------------------------------------------------------------
    // The field's gang-ups were the mod's own: WO-139 held the guards on the avatar with combat_forcedTarget (the
    // host's 11 blows turned none of them), arrested the joiner at his body, judged a stop "fled" while he stood in
    // a loot screen, judged 0-hp hits murders, and the WO-151 scene guard resumed 21 copies so the engine's scene-end
    // fast-forward jumped them. Each fix has its switch (default on; mp_scene_resume default off):
    //   mp_host_target   -- the DLL: a host blow frees an NPC from the mod's locks on an avatar; a guard fighting the
    //                       host is never pulled onto one; WO-136 counts the host's blows on an NPC fighting an avatar
    //   mp_guard_respite -- no stop or pursuit of a partner who is down, or for 2 min after he is up (30 s after his
    //                       mp_unstuck); a resist while he is down is no resist; "fled" needs real movement (Lua)
    //   mp_fair_crime    -- a murder only on the victim's death (not a 0-hp hit); an assault is judged 5 s later and
    //                       is no crime if the victim fights by then (the quest brawl the host's world starts later)
    //   mp_scene_resume  -- on: 0.44.0's copy resume at 5 s; off: no copy resumed, the engine's own rescue (a save
    //                       request) at 5 s instead
    private volatile bool _w154HostTarget = true, _w154GuardRespite = true, _w154FairCrime = true, _w154SceneResume;
    private readonly ConcurrentDictionary<byte, Wo154Rules.GuardRespite> _w154Respite = new();
    private readonly ConcurrentDictionary<byte, string> _w154RespiteTold = new();
    private readonly ConcurrentDictionary<string, long> _w154GuardOnHostAt = new(StringComparer.Ordinal);
    private long _w154RespiteSkips, _w154ResistIgnored, _w154EndFightsIn, _w154EndFightsOut, _w154GuardOnHost;

    private Wo154Rules.GuardRespite W154Respite(byte peer) => _w154Respite.GetOrAdd(peer, _ => new Wo154Rules.GuardRespite());

    /// <summary>Host: a partner went down (a death, a knockout, a knockdown): no guard acts on him.</summary>
    private void Wo154OnPeerDown(byte peer)
    {
        var r = W154Respite(peer);
        lock (r) r.Down(Environment.TickCount64);
    }

    /// <summary>Host: a partner is up again: the respite starts.</summary>
    private void Wo154OnPeerUp(byte peer)
    {
        var r = W154Respite(peer);
        lock (r) r.Up(Environment.TickCount64);
        if (_w154GuardRespite)
            Console.WriteLine($"MP-W154 host: ghost {peer} is up again -- no guard stops or attacks him for {Wo154Rules.GuardRespite.RespiteMs / 1000} s (mp_guard_respite)");
    }

    /// <summary>Host: why the guards may not act on this partner now (null: they may). Told on each change.</summary>
    private string? Wo154GuardsBlocked(byte peer)
    {
        if (!_w154GuardRespite || !_w154Respite.TryGetValue(peer, out var r)) return null;
        string? b;
        lock (r) b = r.Blocked(Environment.TickCount64);
        string tag = b is null ? "" : b == "down" ? "down" : "respite";
        if (_w154RespiteTold.GetValueOrDefault(peer, "") != tag)
        {
            _w154RespiteTold[peer] = tag;
            Console.WriteLine(b is null ? $"MP-W154 host: ghost {peer}'s respite is over -- the guards act on his record again"
                                        : tag == "down" ? $"MP-W154 host: ghost {peer} is down -- no guard stops or attacks him"
                                        : $"MP-W154 host: ghost {peer} is up again ({b[8..]} of respite left) -- no guard stops or attacks him");
        }
        if (b is not null) Interlocked.Increment(ref _w154RespiteSkips);
        return b;
    }

    /// <summary>Host: a guard WO-139 would send at the avatar fights the host (the DLL's answer 4): left to it.</summary>
    private void Wo154NoteGuardOnHost(string guard, byte peer)
    {
        Interlocked.Increment(ref _w154GuardOnHost);
        long now = Environment.TickCount64;
        if (_w154GuardOnHostAt.TryGetValue(guard, out long at) && now - at < 60_000) return;
        _w154GuardOnHostAt[guard] = now;
        Console.WriteLine($"MP-W154 host: {guard} would attack ghost {peer}'s avatar, but it fights the host -- left to that fight (the game splits its enemies)");
    }

    /// <summary>mp_unstuck's end-combat step on this machine (w154_endfights &lt;why&gt;).</summary>
    private async Task Wo154EndFightsLocalAsync(string why)
    {
        Interlocked.Increment(ref _w154EndFightsOut);
        var (ok, reason) = await _combat.Wo131StopFightAsync(0);
        int released = 0;
        foreach (var (name, e) in _w132Engaged.ToArray())   // joiner: copies engaged against this player (WO-132)
            if (_w132Engaged.TryRemove(name, out _)) { released++; await _combat.Wo132EngageAsync(false, e.Eid, default); }
        released += _w147Local.Count;
        if (!_w147Local.IsEmpty) await Wo147ReleaseAllAsync("mp_unstuck");   // joiner: hostile copies (WO-147)
        bool asked = W137JoinerSession;
        if (asked) await Wo139SendAsync(Protocol.CrimeAskUp, Protocol.JoinTargetHost, Protocol.CrimeAskEndFights, 0, Wo154Rules.EndFightsText(why));
        Console.WriteLine($"MP-W154 mp_unstuck: this player's fights end -- the game's own StopFight on this Henry {(ok ? "called" : $"FAILED (reason {reason})")}, {released} engaged cop(ies) let go{(asked ? "; the host is asked to end every fight against my avatar" : "")}");
    }

    /// <summary>Host: a joiner ended his fights (his mp_unstuck): every fight against his avatar ends in this world.</summary>
    private async Task Wo154EndFightsForPeerAsync(byte src, string why)
    {
        Interlocked.Increment(ref _w154EndFightsIn);
        var r = W154Respite(src);
        lock (r) r.Unstuck(Environment.TickCount64);
        if (_w139Records.TryGetValue(src, out var rec)) rec.Calm(null);   // the record stands; nothing fresh or escalated
        int pursuits = 0;
        foreach (var (g, p) in _w139Pursuits.ToArray())
            if (p.Peer == src) { pursuits++; await Wo139EndPursuitAsync(g, $"the joiner ended his fights ({why})"); }
        await Wo132AvatarLeaveFightAsync(src, $"ended his fights ({why})");
        Console.WriteLine($"MP-W154 host: ghost {src} ended his fights ({why}) -- {pursuits} guard pursuit(s) ended, his avatar left its skirmish and the mod's locks on it; no guard acts on him for {Wo154Rules.GuardRespite.UnstuckRespiteMs / 1000} s");
    }

    /// <summary>The native half of mp_host_target, at every connect and on a change.</summary>
    private async Task Wo154PushNativeAsync(string why)
    {
        var r = await _combat.Wo132Async(13, [(byte)(_w154HostTarget ? 1 : 0)]);
        if (r is not { Ok: true }) Console.WriteLine($"MP-W154 mp_host_target {(_w154HostTarget ? "on" : "off")} -> the DLL did not take it ({why}; an older KCDMP.dll?)");
    }

    /// <summary>
    /// mp_w154_check (live checks, test NPCs only -- names starting w154_): hostfight &lt;npc&gt; [secs] | hostfight off |
    /// pursue &lt;npc&gt; &lt;ghost&gt; on|off | status. No input exists: the host's blows come from the engine's own
    /// combat automation on his Henry (DLL op 14), a pursuit's lock from WO-139's own op.
    /// </summary>
    private async Task Wo154CheckAsync(string arg)
    {
        var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        static bool TestNpc(string n) => n.StartsWith("w154_", StringComparison.Ordinal) && NpcNamePattern.IsMatch(n);
        if (p.Length == 2 && p[0] == "hostfight" && p[1] == "off")
        {
            var r = await _combat.Wo132Async(14, [0, 0, 0, 0, 0, 0]);
            Console.WriteLine($"MP-W154 check: hostfight off -> {(r is { Ok: true } ? "the host's automation off" : "FAILED")}");
        }
        else if (p.Length >= 2 && p[0] == "hostfight" && TestNpc(p[1]))
        {
            uint npcEid = await _combat.Wo132WatchAsync(true, p[1]) ?? 0;
            byte secs = p.Length > 2 && byte.TryParse(p[2], out byte s) ? s : (byte)15;
            var a = new byte[6];
            a[0] = 1;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(1), npcEid);
            a[5] = secs;
            var r = npcEid == 0 ? null : await _combat.Wo132Async(14, a);
            Console.WriteLine($"MP-W154 check: the host fights {p[1]} for {secs} s by the engine's own automation -> {(r is { Ok: true } ? "on" : $"FAILED (reason {r?.Reason})")}");
        }
        else if (p.Length == 4 && p[0] == "pursue" && TestNpc(p[1]) && p[3] is "on" or "off")
        {
            if (!_ghostEntityIds.TryGetValue(p[2], out uint avatar)) { Console.WriteLine($"MP-W154 check: no avatar {p[2]}"); return; }
            byte ghost = byte.Parse(p[2], CultureInfo.InvariantCulture);
            var r = await _combat.Wo139PursueAsync(p[3] == "on", avatar, p[1]);
            // tracked like a real pursuit (in a settlement of his record, or WO-139's tick ends it at once), so a
            // partner's down or mp_unstuck ends it the same way
            string st = _w139Records.TryGetValue(ghost, out var rec) ? rec.Settlements.FirstOrDefault() ?? "" : "";
            if (p[3] == "on" && r is 1 or 0) _w139Pursuits[p[1]] = (ghost, avatar, st, Environment.TickCount64);
            else if (p[3] == "off") _w139Pursuits.TryRemove(p[1], out _);
            Console.WriteLine($"MP-W154 check: WO-139's pursuit of avatar {p[2]} by {p[1]} {p[3]} -> {r?.ToString(CultureInfo.InvariantCulture) ?? "no answer"} (1 set, 0 already, 2 no body, 3 refused, 4 it fights the host)");
        }
        else if (p.Length == 1 && p[0] == "where")
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string w = await Wo154WhereNowAsync();
            Console.WriteLine(FormattableString.Invariant($"MP-W154 check: where -> {w} in {sw.ElapsedMilliseconds} ms (the join's probe: \"busy\" = no answer in 4 s)"));
        }
        else if (p.Length == 2 && p[0] == "whereloop" && int.TryParse(p[1], out int loopS) && loopS is > 0 and <= 300)
        {
            // the join's probe every 2 s by the agent itself (a console command cannot run while a load holds the game)
            _ = Task.Run(async () =>
            {
                var end = DateTime.UtcNow.AddSeconds(loopS);
                while (DateTime.UtcNow < end)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string w = await Wo154WhereNowAsync();
                    Console.WriteLine(FormattableString.Invariant($"MP-W154 check: whereloop -> {w} in {sw.ElapsedMilliseconds} ms"));
                    await Task.Delay(2000);
                }
            });
        }
        else if (p.Length == 1 && p[0] == "status")
        {
            Console.WriteLine($"MP-W154 check: {Wo154FightStatsText()} {Wo154JoinStatsText()}");
            Console.WriteLine($"MP-W154 check: dll {await _combat.Wo132StatusAsync() ?? "no answer"}");
        }
        else Console.WriteLine("MP-W154 check: hostfight <w154_npc> [secs] | hostfight off | pursue <w154_npc> <ghost> on|off | where | status");
    }

    private string Wo154FightStatsText() => FormattableString.Invariant(
        $"host_target={(_w154HostTarget ? "on" : "off")} guard_respite={(_w154GuardRespite ? "on" : "off")} fair_crime={(_w154FairCrime ? "on" : "off")} scene_resume={(_w154SceneResume ? "on" : "off")} respite_skips={_w154RespiteSkips} resist_ignored={_w154ResistIgnored} guard_on_host={_w154GuardOnHost} endfights_in={_w154EndFightsIn} endfights_out={_w154EndFightsOut}");

    private void Wo154OnEvent(string name, string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (name)
        {
            case "w154_falls":   // mp_avatar_falls on|off
                if (f.Length >= 1 && f[0] is "on" or "off")
                {
                    _w154AvatarFalls = f[0] == "on";
                    Console.WriteLine($"MP-W154 mp_avatar_falls {(_w154AvatarFalls ? "ON" : "off")} -- a partner who is knocked down {(_w154AvatarFalls ? "falls and lies on this screen" : "is shown as before (a knockout hidden like a death)")}");
                }
                return;
            case "w154_coalesce":   // mp_quest_coalesce on|off
                if (f.Length >= 1 && f[0] is "on" or "off")
                {
                    _w154Coalesce = f[0] == "on";
                    if (!_w154Coalesce) Wo154FlushCoalesced(true);
                    Console.WriteLine($"MP-W154 mp_quest_coalesce {(_w154Coalesce ? "ON" : "off")} -- a joiner's AI-behaviour steps on one State are {(_w154Coalesce ? "judged as their net result" : "judged one by one (0.44.0)")}");
                }
                return;
            case "w154_cfg":   // phase 3's switches: key=on|off ... (mp_host_target, mp_guard_respite, mp_fair_crime, mp_scene_resume)
                foreach (var kv in f)
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0 || kv[(eq + 1)..] is not ("on" or "off")) continue;
                    bool on = kv[(eq + 1)..] == "on";
                    switch (kv[..eq])
                    {
                        case "host_target":
                            if (_w154HostTarget != on) { _w154HostTarget = on; _ = Wo154PushNativeAsync("mp_host_target"); }
                            break;
                        case "guard_respite": _w154GuardRespite = on; break;
                        case "fair_crime": _w154FairCrime = on; break;
                        case "scene_resume": _w154SceneResume = on; break;
                        case "join_patient": _w154JoinPatient = on; break;
                        case "ride_native": _w154RideNative = on; if (!on) _w154RideFeed.Clear(); break;
                        case "gait_hyst":
                            if (_w154GaitHyst != on) { _w154GaitHyst = on; _ = PushWo121ConfigAsync(_wo121Ct); }
                            break;
                        case "join_panel": _w154JoinPanel = on; if (!on) Wo154HostJoinPanelHide("mp_join_panel off"); break;
                    }
                }
                Console.WriteLine($"MP-W154 cfg {Wo154FightStatsText()} {Wo154JoinStatsText()} {Wo154RideStatsText()}");
                return;
            case "w154_ride":        // phase 5: <ghost> <horse> <dz> | <ghost> off
                Wo154OnRide(arg);
                return;
            case "w154_check":       // mp_w154_check (live checks, test NPCs only)
                _ = Wo154CheckAsync(arg ?? "");
                return;
            case "w154_endfights":   // mp_unstuck's end-combat step
                _ = Wo154EndFightsLocalAsync(f.Length > 0 && Wo139Text.IsWord(f[0]) ? f[0] : "unstuck");
                return;
            case "w154_status":
                Console.WriteLine(FormattableString.Invariant($"MP-W154-STATUS falls={(_w154AvatarFalls ? "on" : "off")} fell={_w154Falls} rose={_w154Rises} peers_down={_w154PeerDown.Count(kv => kv.Value.Down)} coalesce={(_w154Coalesce ? "on" : "off")} merged={_w154Merged} waiting={_w154Coalescer.Count} contested={_w154Contest.Count} {Wo154FightStatsText()} {Wo154JoinStatsText()} {Wo154RideStatsText()}"));
                return;
        }
    }
}
