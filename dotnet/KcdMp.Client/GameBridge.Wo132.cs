using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-132: damage safety and combat engagement -- the agent's half
// (docs/WO-132-findings.md).
//
// HOST
//   * an NPC's hit on an avatar is measured at the DLL's hit chokepoint and
//     put back there (0x9A); only that goes to the avatar's owner (0x21). The
//     Lua health sampler's ghost_hit (which counted bleed ticks) is superseded;
//   * nothing is forwarded to a peer who is down (the downed flag, or a death)
//     or less than 5 s awake;
//   * at the peer's down: the avatar leaves its skirmish (the fight goes on,
//     the host's with it), its bleeding and health are reset and it is hidden
//     until the peer's new position has streamed in after the wake;
//   * a LocalHit on an avatar never goes out on the guid route (one path per hit);
//   * every drawn NPC it streams is watched natively; its combat state goes to
//     the joiners as NpcCombat.
// JOINER
//   * a host NpcCombat for a guarded, bound copy near this player engages it:
//     a skirmish against this player and the host NPC's combat state held on
//     the copy, so the game's own combat mode (the directional indicator,
//     block) comes up against it. Its local hits on this player are put back;
//   * a forwarded hit (0x22) while this player is down or waking is refused.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<byte, LifeGate> _w132PeerGates = new();
    private readonly LifeGate _w132LocalGate = new();
    private readonly ConcurrentDictionary<byte, byte> _w132PeerFlags = new();
    private readonly ConcurrentDictionary<string, long> _w132Watched = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (DateTime At, uint Eid)> _w132Engaged = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, NpcCombatEvent> _w132LastSent = new(StringComparer.Ordinal);
    private volatile bool _w132NativeHitWatch;
    private long _w132HitsFwd, _w132HitsBlocked, _w132TicksDropped, _w132SamplerSuperseded, _w132GhostGuidDropped,
                 _w132CombatOut, _w132CombatIn, _w132EngageOn, _w132EngageOff, _w132EngageFar, _w132Discarded, _w132LocalRefused,
                 _w132LeaveOk, _w132LeaveFail;

    private LifeGate W132Gate(byte ghost) => _w132PeerGates.GetOrAdd(ghost, _ => new LifeGate());

    private void Wo132OnConnect(CancellationToken ct)
    {
        _combat.OnNpcAvatarHit = OnNpcAvatarHitAsync;
        _combat.OnNpcCombat = OnNpcCombatStateAsync;
        _combat.OnDiscardedHit = (eid, st, hp) =>
        {
            Interlocked.Increment(ref _w132Discarded);
            Console.WriteLine(FormattableString.Invariant(
                $"MP-W132 local hit on me by the engaged copy eid=0x{eid:X} discarded (hp -{hp:F1} st -{st:F1} put back) -- the host's world decides my damage"));
            return Task.CompletedTask;
        };
        _ = Wo132LoopAsync(ct);
    }

    private void Wo132OnDisconnect()
    {
        _combat.OnNpcAvatarHit = null;
        _combat.OnNpcCombat = null;
        _combat.OnDiscardedHit = null;
        _w132Watched.Clear();
        _w132LastSent.Clear();
        foreach (var (name, e) in _w132Engaged.ToArray())
        {
            _w132Engaged.TryRemove(name, out _);
            _ = _combat.Wo132EngageAsync(false, e.Eid, default);
        }
    }

    private async Task Wo132LoopAsync(CancellationToken ct)
    {
        long lastStats = Environment.TickCount64;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); } catch { return; }
            try
            {
                // The DLL's hit watch decides whether the Lua sampler is superseded.
                if (_dllWo121Status.Length > 0)
                {
                    bool armed = _dllWo121Status.Contains("npc_watch=armed", StringComparison.Ordinal);
                    if (armed != _w132NativeHitWatch)
                        Console.WriteLine($"MP-W132 native NPC-hit watch {(armed ? "armed -- the Lua health sampler's ghost_hit is superseded (a bleed tick is never a hit)" : "OFF -- ghost_hit forwards only drops of 1 hp or more")}");
                    _w132NativeHitWatch = armed;
                }
                // Joiner: an engaged copy with no fresh host state lets go.
                var now = DateTime.UtcNow;
                foreach (var (name, e) in _w132Engaged.ToArray())
                    if (now - e.At > Wo132Rules.EngageStale && _w132Engaged.TryRemove(name, out _))
                    {
                        Interlocked.Increment(ref _w132EngageOff);
                        var r = await _combat.Wo132EngageAsync(false, e.Eid, default, ct);
                        Console.WriteLine($"MP-W132 engage off {name}: no host combat state for {Wo132Rules.EngageStale.TotalSeconds:F0} s -> released, still bound and paused ({(r.Ok ? "ok" : "reason " + r.Reason)})");
                    }
                if (Environment.TickCount64 - lastStats >= 60_000)
                {
                    lastStats = Environment.TickCount64;
                    string? nat = await _combat.Wo132StatusAsync(ct);
                    Console.WriteLine(FormattableString.Invariant(
                        $"MP-W132-STATS hits_fwd={_w132HitsFwd} hits_blocked={_w132HitsBlocked} ticks_dropped={_w132TicksDropped} sampler_superseded={_w132SamplerSuperseded} ghost_guid_dropped={_w132GhostGuidDropped} leave_ok={_w132LeaveOk} leave_fail={_w132LeaveFail} combat_out={_w132CombatOut} combat_in={_w132CombatIn} engage_on={_w132EngageOn} engage_off={_w132EngageOff} engage_far={_w132EngageFar} discarded={_w132Discarded} local_refused={_w132LocalRefused} native_watch={(_w132NativeHitWatch ? "armed" : "off")} | {nat ?? "native: no answer"}"));
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Console.WriteLine($"MP-W132 tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private byte? GhostOfEid(uint eid)
    {
        foreach (var kv in _ghostEntityIds)
            if (kv.Value == eid && byte.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g)) return g;
        return null;
    }

    // =====================================================================
    // Host: hits on avatars
    // =====================================================================

    /// <summary>0x9A: an NPC's hit on an avatar, measured and already put back by the DLL.</summary>
    private async Task OnNpcAvatarHitAsync(uint victimEid, float stamina, float health, uint attackerEid, byte flags, string attacker)
    {
        if (!_isDamageAuthority) return;
        byte? gid = GhostOfEid(victimEid);
        if (gid is not byte g)
        {
            Console.WriteLine(FormattableString.Invariant($"MP-W132 npc hit on unknown avatar eid=0x{victimEid:X} by {attacker} hp -{health:F1} -- dropped"));
            return;
        }
        var why = W132Gate(g).Check(DateTime.UtcNow);
        if (why != LifeGate.Why.Open)
        {
            Interlocked.Increment(ref _w132HitsBlocked);
            Console.WriteLine(FormattableString.Invariant(
                $"MP-W132 npc hit on avatar {g} by {attacker} hp -{health:F1} st -{stamina:F1} NOT forwarded: its player is {LifeGate.Tag(why)} (nobody is hit while dead or waking)"));
            return;
        }
        var send = _sendPlayerHit;
        if (send is null) return;
        await send(g, health, stamina);
        Interlocked.Increment(ref _w132HitsFwd);
        Wo132KeepAvatarUp(g, force: true);
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W132 npc hit on avatar {g} by {attacker} (eid 0x{attackerEid:X}{((flags & 0x02) != 0 ? ", missile" : "")}): measured hp -{health:F1} st -{stamina:F1} at the hit, forwarded (the avatar's health was put back)"));
    }

    /// <summary>The Lua sampler's ghost_hit: superseded by the DLL's watch; otherwise only a real drop outside a down.</summary>
    private bool Wo132AllowGhostHit(byte ghost, float loss)
    {
        var v = Wo132Rules.JudgeGhostHit(_w132NativeHitWatch, loss, W132Gate(ghost).Check(DateTime.UtcNow));
        // Whatever the verdict, a drop the host does not forward still leaves the
        // avatar lower: its health and bleeding are put back (throttled), so it
        // never sits at the imm floor where the next real hit measures nothing.
        if (v != Wo132Rules.GhostHitVerdict.Forward) Wo132KeepAvatarUp(ghost);
        switch (v)
        {
            case Wo132Rules.GhostHitVerdict.Forward: return true;
            case Wo132Rules.GhostHitVerdict.SupersededByNative: Interlocked.Increment(ref _w132SamplerSuperseded); return false;
            case Wo132Rules.GhostHitVerdict.Tick:
                if (Interlocked.Increment(ref _w132TicksDropped) <= 5)
                    Console.WriteLine(FormattableString.Invariant($"MP-W132 avatar {ghost} health drop {loss:F2} not forwarded: a tick (bleeding), not a hit"));
                return false;
            default:
                Interlocked.Increment(ref _w132HitsBlocked);
                Console.WriteLine(FormattableString.Invariant($"MP-W132 avatar {ghost} health drop {loss:F2} not forwarded: its player is down or waking"));
                return false;
        }
    }

    private readonly ConcurrentDictionary<byte, long> _w132KeptUpAt = new();

    /// <summary>The avatar's bleeding healed and its health put back (at most every 2 s unless forced).</summary>
    private void Wo132KeepAvatarUp(byte ghost, bool force = false)
    {
        if (!_isDamageAuthority) return;
        long now = Environment.TickCount64;
        if (!force && _w132KeptUpAt.TryGetValue(ghost, out long at) && now - at < 2000) return;
        _w132KeptUpAt[ghost] = now;
        _ = Task.Run(async () =>
        {
            await ExecLuaAsync($"if KCD2MP_W132AvatarHeal then KCD2MP_W132AvatarHeal(\"{ghost}\") end");
            await Wo131RestoreAvatarAsync(ghost);
        });
    }

    /// <summary>One path per hit: a LocalHit on an avatar never goes out on the guid route.</summary>
    private bool Wo132DropGhostGuidRoute(Guid soul, string? npcName, float health)
    {
        if (Wo132Rules.GuidRouteAllowed(npcName)) return false;
        if (Interlocked.Increment(ref _w132GhostGuidDropped) <= 10)
            Console.WriteLine(FormattableString.Invariant(
                $"MP-DMG dir=drop route=guid-ghost soul={soul} npc={npcName} hp={health:F1} reason=wo132-one-path (an avatar's hits go by 0x21 and friendly fire, never a second time here)"));
        return true;
    }

    // =====================================================================
    // Host: a peer goes down and wakes
    // =====================================================================

    /// <summary>Every inbound vitals sample: the downed bit's edges are the peer's down and (knockdown) wake.</summary>
    private void Wo132OnPeerVitals(byte ghost, float health, byte flags)
    {
        bool down = (flags & Protocol.PlayerStateFlagUnconscious) != 0;
        bool was = _w132PeerFlags.TryGetValue(ghost, out byte f0) && (f0 & Protocol.PlayerStateFlagUnconscious) != 0;
        _w132PeerFlags[ghost] = flags;
        if (down && !was) _ = Wo132OnPeerDownAsync(ghost, "downed");
        else if (!down && was) _ = Wo132OnPeerUpAsync(ghost, "back up");
    }

    private async Task Wo132OnPeerDownAsync(byte ghost, string why)
    {
        if (!W132Gate(ghost).Down(DateTime.UtcNow)) return;
        Console.WriteLine($"MP-W132 peer {ghost} {why}: nothing is forwarded to it until 5 s after it wakes");
        if (!_isDamageAuthority) return;
        await Wo139OnPeerDownAsync(ghost, why);   // WO-139: the guards fighting its avatar stop
        await Wo132AvatarLeaveFightAsync(ghost, why);
        // Its state on the host: bleeding and health reset, hidden at the death spot.
        await ExecLuaAsync($"if KCD2MP_W132AvatarDown then KCD2MP_W132AvatarDown(\"{ghost}\", true) end");
        await Wo131RestoreAvatarAsync(ghost);
    }

    private async Task Wo132OnPeerUpAsync(byte ghost, string why)
    {
        bool ended = W132Gate(ghost).Up(DateTime.UtcNow);
        Console.WriteLine($"MP-W132 peer {ghost} {why}: {(ended ? "awake" : "(again)")} -- forwards resume in 5 s");
        if (!_isDamageAuthority) return;
        await Wo132AvatarLeaveFightAsync(ghost, why);
        // Shown again once its new position has streamed in (the interpolator
        // snaps any jump over 5 m, and the stream follows the announcement).
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            await ExecLuaAsync($"if KCD2MP_W132AvatarDown then KCD2MP_W132AvatarDown(\"{ghost}\", false) end");
            await Wo131RestoreAvatarAsync(ghost);
        });
    }

    /// <summary>The avatar leaves its skirmish; the enemies drop it and keep fighting the host.</summary>
    private async Task Wo132AvatarLeaveFightAsync(byte ghost, string what)
    {
        if (_w121Engaged.TryRemove(ghost, out _)) await Wo121EngageAsync(ghost, false);
        if (!_ghostEntityIds.TryGetValue(ghost.ToString(CultureInfo.InvariantCulture), out uint eid)) return;
        var (ok, reason) = await _combat.Wo132LeaveFightAsync(eid);
        if (ok) Interlocked.Increment(ref _w132LeaveOk); else Interlocked.Increment(ref _w132LeaveFail);
        Console.WriteLine($"MP-W132 peer {ghost} {what}: its avatar left its skirmish -> {(ok ? "removed" : $"FAILED (reason {reason})")} -- the fight goes on without it (the host's too); crime is untouched");
    }

    // =====================================================================
    // Host: NPC combat state out
    // =====================================================================

    /// <summary>Every npc_state the host sends: a drawn, live NPC is watched natively.</summary>
    private void Wo132NoteNpcState(string npcName, byte flags)
    {
        if (!_isDamageAuthority || !_sharedWorld) return;
        bool want = Wo132Rules.HostWatches(flags);
        long now = Environment.TickCount64;
        if (want)
        {
            if (_w132Watched.TryGetValue(npcName, out long at) && now - at < 30_000) return;
            _w132Watched[npcName] = now;
            _ = Task.Run(async () =>
            {
                uint? eid = await _combat.Wo132WatchAsync(true, npcName);
                if (eid is null) _w132Watched.TryRemove(npcName, out _);
                else if (_w132Watched.Count <= 20) Console.WriteLine($"MP-W132 watching {npcName} (eid 0x{eid:X}): its combat state goes to the joiners");
            });
        }
        else if (_w132Watched.TryRemove(npcName, out _))
            _ = _combat.Wo132WatchAsync(false, npcName);
    }

    /// <summary>0x9B: a watched NPC's combat state -> NpcCombat on the action channel.</summary>
    private async Task OnNpcCombatStateAsync(NpcCombatState s)
    {
        if (!_isDamageAuthority || _wo121Stream is not Stream stream || s.Name.Length == 0) return;
        if (!NpcNamePattern.IsMatch(s.Name) || Protocol.IsNeverSyncedNpcName(s.Name)) return;
        var ev = Wo132Rules.ToEvent(s, SenderMsNow(), GhostOfEid);
        bool changed = !_w132LastSent.TryGetValue(s.Name, out var last) || last.State != ev.State || last.Target != ev.Target || last.TargetGhost != ev.TargetGhost;
        _w132LastSent[s.Name] = ev;
        await WritePacketAsync(stream, _actionOut.Build(ActionKind.NpcCombat, ActionPhase.Commit, ev.ToBytes()), _wo121Ct);
        Interlocked.Increment(ref _w132CombatOut);
        if (changed) Console.WriteLine($"MP-W132 npc-combat out {ev} (native: {s})");
    }

    // =====================================================================
    // Joiner: engagement
    // =====================================================================

    /// <summary>ActionKind.NpcCombat from the host.</summary>
    private async Task Wo132OnNpcCombatInAsync(InboundAction a, CancellationToken ct)
    {
        if (!NpcCombatEvent.TryFromBytes(a.Payload, out var ev)) { Console.WriteLine($"MP-W132 npc-combat in malformed len={a.Payload.Length}"); return; }
        Interlocked.Increment(ref _w132CombatIn);
        bool guarded = _w131Guarded.TryGetValue(ev.Name, out uint geid);
        bool bound = _nativeBound.ContainsKey(ev.Name);
        uint eid = guarded ? geid : _npcEntityIds.TryGetValue(ev.Name, out uint pe) ? pe : 0;
        bool engaged = _w132Engaged.TryGetValue(ev.Name, out var cur);
        var v = Wo132Rules.JudgeEngage(ev, Wo131JoinerActive, guarded, bound, engaged);
        // WO-136 Phase 4: a knockout (or a death) beats the engagement -- never engaged while down.
        if (!Wo136Rules.MayEngage(Wo136HostNpcDown(ev.Name)) && v != Wo132Rules.EngageVerdict.Ignore)
            v = engaged ? Wo132Rules.EngageVerdict.Release : Wo132Rules.EngageVerdict.Ignore;
        if (v == Wo132Rules.EngageVerdict.Ignore) return;
        if (v == Wo132Rules.EngageVerdict.Release)
        {
            _w132Engaged.TryRemove(ev.Name, out _);
            Interlocked.Increment(ref _w132EngageOff);
            var rr = await _combat.Wo132EngageAsync(false, cur.Eid, default, ct);
            Console.WriteLine($"MP-W132 engage off {ev.Name}: {(ev.InCombat ? "no longer the host's bound copy" : "the host's NPC left combat")} -> released, bound and paused ({(rr.Ok ? "ok" : "reason " + rr.Reason)})");
            return;
        }
        if (eid == 0) return;
        var r = await _combat.Wo132EngageAsync(true, eid, ev.State, ct);
        if (r.Reason == 6)   // kRFar: more than 15 m from this player
        {
            if (_w132Engaged.TryRemove(ev.Name, out _))
            {
                Interlocked.Increment(ref _w132EngageOff);
                Console.WriteLine(FormattableString.Invariant($"MP-W132 engage off {ev.Name}: {r.DistM:F1} m away -> released"));
            }
            else Interlocked.Increment(ref _w132EngageFar);
            return;
        }
        if (!r.Ok) { Console.WriteLine($"MP-W132 engage {ev.Name} eid=0x{eid:X} FAILED (reason {r.Reason})"); return; }
        _w132Engaged[ev.Name] = (DateTime.UtcNow, eid);
        if (r.First)
        {
            Interlocked.Increment(ref _w132EngageOn);
            Console.WriteLine(FormattableString.Invariant(
                $"MP-W132 engage on {ev.Name} (eid 0x{eid:X}, {r.DistM:F1} m): skirmish vs me {(r.Skirmish ? "added" : "NOT added")}, the host's combat state held on it ({ev.State}, target {ev.Target}) -- still bound and paused; its local hits on me are discarded"));
        }
    }

    /// <summary>Guarded copies: their local hits on this player are discarded whether or not they are engaged.</summary>
    private async Task Wo132DiscardAsync(bool on, uint eid)
    {
        try { await _combat.Wo132DiscardAsync(on, eid); } catch { }
    }

    /// <summary>Live checks from the console (w132_check): "block on|off", "read me|&lt;npc&gt;".</summary>
    private async Task Wo132CheckAsync(string arg)
    {
        var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 2 && p[0] == "block")
        {
            var r = await _combat.Wo132Async(7, [(byte)(p[1] == "on" ? 1 : 0)]);
            Console.WriteLine($"MP-W132 check: player block {p[1]} -> {(r is { Ok: true } ? "set (SetBlockMode, no input)" : "FAILED")}");
        }
        else if (p.Length == 2 && p[0] == "hostile" && p[1].StartsWith("wo132_", StringComparison.Ordinal))
        {
            // Live checks only, test NPCs only (the wo132_ prefix): the WO-17 hostile-donor faction attach
            // on a spawned test soul, so it attacks the player's faction unprovoked (a real hostile).
            Guid? g = await ResolveLocalSoulGuidAsync(p[1], CancellationToken.None);
            (bool Ok, byte Reason) r = g is Guid gg ? await _combat.Wo131FactionAsync(gg, 1) : (false, (byte)255);
            Console.WriteLine($"MP-W132 check: {p[1]} -> the hostile faction: {(r.Ok ? "attached" : $"FAILED (reason {r.Reason})")}");
        }
        else if (p.Length >= 3 && p[0] == "fight" && p[1].StartsWith("wo132_", StringComparison.Ordinal))
        {
            // fight <wo132_npc> host|avatar:N [override]: a test NPC (never a world NPC) fights that body.
            uint npcEid = await _combat.Wo132WatchAsync(true, p[1]) ?? 0;
            uint tgt = 0;
            if (p[2].StartsWith("avatar:", StringComparison.Ordinal) && !_ghostEntityIds.TryGetValue(p[2][7..], out tgt)) { Console.WriteLine($"MP-W132 check: no avatar {p[2]}"); return; }
            byte ovr = p.Length > 3 ? byte.Parse(p[3], CultureInfo.InvariantCulture) : (byte)1;
            var a = new byte[9];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a, npcEid);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(4), tgt);
            a[8] = ovr;
            var r = npcEid == 0 ? null : await _combat.Wo132Async(8, a);
            Console.WriteLine($"MP-W132 check: {p[1]} fights {p[2]} (override {ovr}) -> {(r is { Ok: true } ? "on" : $"FAILED (reason {r?.Reason})")}");
        }
        else if (p.Length == 2 && p[0] == "read")
        {
            uint eid = 0;
            if (p[1] != "me" && !_w131Guarded.TryGetValue(p[1], out eid) && !_npcEntityIds.TryGetValue(p[1], out eid)) { Console.WriteLine($"MP-W132 check: no body for {p[1]}"); return; }
            var s = await _combat.Wo132ReadAsync(eid);
            Console.WriteLine($"MP-W132 check: read {p[1]} -> {(s is NpcCombatState v ? v.ToString() + (v.HasCombatActor ? "" : " (no combat actor)") : "no answer")}");
        }
    }

    /// <summary>The joiner's own belt and braces: a forwarded hit while this player is down or waking is refused.</summary>
    private bool Wo132RefuseLocal(float healthLoss)
    {
        var why = _w132LocalGate.Check(DateTime.UtcNow);
        if (why == LifeGate.Why.Open) return false;
        Interlocked.Increment(ref _w132LocalRefused);
        Console.WriteLine(FormattableString.Invariant($"[playerhit] {healthLoss:F1} damage from the host REFUSED: I am {LifeGate.Tag(why)} (nobody is hit while dead or waking)"));
        return true;
    }
}
