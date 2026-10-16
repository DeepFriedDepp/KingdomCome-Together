// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-187: every fight move visible to the watcher, the joiner's shield, fight positions -- the agent's half
// (the rules are Wo187Rules in Wo187.cs; the native half wo187.cpp).
//
// JOINER
//   * A1 (mp_engage_intent): a copy whose host NPC targets this player is engaged within range by the host's position too (the copy may lag);
//   * A2 (mp_opp_logical): every copy in a host fight gets its wished opponent (this player, or the figure of the one its host NPC fights) the
//     engine's way, refreshed while the fight lasts (op 36);
//   * C2 (mp_anim_guard): every copy in a host fight holds its guard (op 39); (mp_anim_defence) its host NPC's own block / dodge rows play on it;
//   * B1 (mp_fight_pos): once a second per copy in a fight, its position here, the host's stream position and this player's.
// HOST
//   * B1: once a second per NPC fighting a player, its position and its target's (the figure's, or the host's own).
// BOTH
//   * the switches go to the DLL every 10 s (op 38); MP-W187-STATS once a minute.
public partial class GameBridge
{
    private volatile bool _w187OppLogical = Wo187Rules.DefaultOppLogical, _w187OppPin = Wo187Rules.DefaultOppPin,
                          _w187EngageIntent = Wo187Rules.DefaultEngageIntent, _w187Guard = Wo187Rules.DefaultAnimGuard,
                          _w187Idle = Wo187Rules.DefaultAnimIdle, _w187Defence = Wo187Rules.DefaultAnimDefence,
                          _w187Combo = Wo187Rules.DefaultAnimCombo, _w187ScopeLog = Wo187Rules.DefaultAnimScopeLog,
                          _w187FightPos = Wo187Rules.DefaultFightPos, _w187ChaseFollow = Wo187Rules.DefaultChaseFollow,
                          _w187HitFallback = Wo187Rules.DefaultAnimHitFallback, _w187AvatarHit = Wo187Rules.DefaultAnimAvatarHit;
    private long _w187TickN, _w187OppSent, _w187OppApplied, _w187GuardSent, _w187IntentEngages, _w187DefenceOut, _w187DefenceIn,
                 _w187DefencePlayed, _w187PosLines, _w187RecoilNoRowSaid, _w187HitFallbacks, _w187HitFallbackPlayed, _w187HitFallbackNone,
                 _w187AvatarHitOut, _w187AvatarHitIn, _w187AvatarHitPlayed;
    private readonly ConcurrentDictionary<string, (byte Kind, byte Ghost, long SentMs)> _w187OppLast = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w187GuardLast = new(StringComparer.Ordinal);
    // the copies in a host fight (joiner) / the NPCs fighting a player (host): name -> (eid, target, target ghost, last report)
    private readonly ConcurrentDictionary<string, (uint Eid, NpcCombatTarget Target, byte Ghost, long AtMs)> _w187Fights = new(StringComparer.Ordinal);
    // the host's stream as read (joiner): name -> (x, y, z, sender ms, read ms, samples in the current second, last second's count)
    private readonly ConcurrentDictionary<string, (float X, float Y, float Z, uint SenderMs, long ReadMs, int N, int Pps, long SecMs)> _w187Stream = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w187RecoilSaid = new(StringComparer.Ordinal);
    // C1: a partner's figure's weapon, as the last weapon row he played on it showed it (kcd2mp_<ghost> -> row)
    private readonly ConcurrentDictionary<string, Guid> _w187FigureRow = new(StringComparer.Ordinal);

    // ---------------------------------------------------------------- switches

    private void Wo187OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w187_cfg":
                if (arg is { } sa && sa.StartsWith("anim_scope_log=", StringComparison.Ordinal) && Wo190Rules.ScopeLogMode(sa[15..]) is { } sm)
                {
                    // WO-190 A9: off / auto / on -- auto (the default) traces the bodies in a fight; on also logs WO-187's lines around every move
                    _w187ScopeLog = sm == Wo190Rules.ScopeLogOn;
                    Wo190ScopeLogWord(sm);
                    _ = Wo187PushConfigAsync();
                    return;
                }
                if (!Wo187Rules.TryParseCfg(arg, out string cfg, out bool on)) { Console.WriteLine($"MP-W187 cfg refused: '{arg}'"); return; }
                switch (cfg)
                {
                    case "opp_logical": _w187OppLogical = on; break;
                    case "opp_pin": _w187OppPin = on; break;
                    case "engage_intent": _w187EngageIntent = on; break;
                    case "anim_guard": _w187Guard = on; break;
                    case "anim_idle": _w187Idle = on; break;
                    case "anim_defence": _w187Defence = on; break;
                    case "anim_combo": _w187Combo = on; break;
                    case "anim_scope_log": _w187ScopeLog = on; break;
                    case "fight_pos": _w187FightPos = on; break;
                    case "chase_follow": _w187ChaseFollow = on; break;
                    case "anim_hit_fallback": _w187HitFallback = on; break;
                    case "anim_avatar_hit": _w187AvatarHit = on; break;
                }
                Console.WriteLine($"MP-W187 cfg {cfg}={(on ? "on" : "off")}");
                _ = Wo187PushConfigAsync();
                return;
            case "w187_status":
                _ = Wo187StatusAsync();
                return;
        }
    }

    private async Task Wo187PushConfigAsync()
    {
        try
        {
            byte B(bool v) => (byte)(v ? 1 : 0);
            var r = await _combat.Wo163Async(Protocol.Wo187OpConfig, [B(_w187OppLogical), B(_w187OppPin), B(_w187Guard), B(_w187Idle), B(_w187Defence),
                                                                      B(_w187Combo), B(_w187ScopeLog), B(_w187Guard), B(_w187ChaseFollow), B(_w187AvatarHit)]);
            if (r is { Ok: true, Payload.Length: >= 8 } x && (x.Payload[0] != B(_w187OppLogical) || x.Payload[2] != B(_w187Guard)))
                Console.WriteLine($"MP-W187 the DLL holds opp_logical={x.Payload[0]} anim_guard={x.Payload[2]}");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W187 config not pushed ({ex.GetType().Name})"); }
    }

    private async Task Wo187StatusAsync()
    {
        string? n = null;
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo187OpStatus, []);
            if (r is { Ok: true } x) n = Encoding.ASCII.GetString(x.Payload);
        }
        catch { }
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W187-STATS opp_logical={On(_w187OppLogical)} opp_pin={On(_w187OppPin)} engage_intent={On(_w187EngageIntent)} anim_guard={On(_w187Guard)} anim_idle={On(_w187Idle)} anim_defence={On(_w187Defence)} anim_combo={On(_w187Combo)} anim_scope_log={On(_w187ScopeLog)} fight_pos={On(_w187FightPos)} chase_follow={On(_w187ChaseFollow)} anim_hit_fallback={On(_w187HitFallback)} anim_avatar_hit={On(_w187AvatarHit)} opp_sent={Interlocked.Read(ref _w187OppSent)} opp_applied={Interlocked.Read(ref _w187OppApplied)} guard_sent={Interlocked.Read(ref _w187GuardSent)} intent_engages={Interlocked.Read(ref _w187IntentEngages)} defence_out={Interlocked.Read(ref _w187DefenceOut)} defence_in={Interlocked.Read(ref _w187DefenceIn)} defence_played={Interlocked.Read(ref _w187DefencePlayed)} pos_lines={Interlocked.Read(ref _w187PosLines)} hit_fallbacks={Interlocked.Read(ref _w187HitFallbacks)} hit_fallback_played={Interlocked.Read(ref _w187HitFallbackPlayed)} hit_fallback_none={Interlocked.Read(ref _w187HitFallbackNone)} avatar_hit_out={Interlocked.Read(ref _w187AvatarHitOut)} avatar_hit_in={Interlocked.Read(ref _w187AvatarHitIn)} avatar_hit_played={Interlocked.Read(ref _w187AvatarHitPlayed)} fights={_w187Fights.Count} | native: {n ?? "no answer"}"));
    }

    /// <summary>Once a second (the WO-151 loop): the switches every 10 s, the fight positions, the stats every minute.</summary>
    private async Task Wo187TickAsync(CancellationToken ct)
    {
        long n = ++_w187TickN;
        if (n % 10 == 1) await Wo187PushConfigAsync();
        if (_w187FightPos) await Wo187FightPosAsync(ct);
        if (n % 60 == 0) await Wo187StatusAsync();
        long now = Environment.TickCount64;
        foreach (var (k, v) in _w187Fights) if (now - v.AtMs > 10_000) _w187Fights.TryRemove(k, out _);
    }

    // ---------------------------------------------------------------- the host's stream as read (joiner)

    /// <summary>The reader: every host NPC sample's position and sender clock, and its samples per second (B2: a chaser's own rate).</summary>
    private void Wo187NoteStream(byte[] payload, long arrivalMs)
    {
        if (payload.Length < 2 || payload.Length != 2 + payload[1] + Protocol.NpcStateFixedTail) return;
        int o = 2 + payload[1];
        string name = Encoding.UTF8.GetString(payload, 2, payload[1]);
        float x = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(o)), y = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(o + 4)),
              z = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(o + 8));
        uint sms = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(o + Protocol.NpcStateSenderMsOffset));
        _w187Stream.AddOrUpdate(name, _ => (x, y, z, sms, arrivalMs, 1, 0, arrivalMs),
            (_, p) => arrivalMs - p.SecMs >= 1000 ? (x, y, z, sms, arrivalMs, 1, p.N, arrivalMs) : (x, y, z, sms, arrivalMs, p.N + 1, p.Pps, p.SecMs));
    }

    // ---------------------------------------------------------------- joiner: a host npc-combat event

    /// <summary>
    /// Every host NpcCombat for a guarded, bound copy (GameBridge.Wo132): the copy's wished opponent (A2) and its guard hold (C2). Returns
    /// whether the host's own position of the NPC puts it within the engage range of this player (A1's intent).
    /// </summary>
    private bool Wo187OnNpcCombatIn(NpcCombatEvent ev, uint eid, bool guardedBound, byte fromGhost)
    {
        long now = Environment.TickCount64;
        bool fighting = ev.InCombat || ev.Target != NpcCombatTarget.None;
        if (fighting && eid != 0) _w187Fights[ev.Name] = (eid, ev.Target, ev.TargetGhost, now);
        if (!guardedBound || eid == 0 || !Wo131JoinerActive) return false;
        float dHost = _w187Stream.TryGetValue(ev.Name, out var near) ? Wo187Rules.Dist2D(near.X, near.Y, _lastX, _lastY) : -1f;
        if (!Wo187Rules.Near(dHost)) return false;   // a far copy: no wish, no guard hold (each is a main-thread pipe call)
        // the host: the sender of this npc-combat event (only the host speaks for its NPCs), else what the leash / host mode named
        byte hostGhost = fromGhost != 0xFF ? fromGhost : _leashHostId != 0xFF ? _leashHostId : _hostModeFrom;
        // A2: the opponent
        if (_w187OppLogical && Wo187Rules.OppWish(ev, _myGhostId, hostGhost) is { } w)
        {
            uint target = 0;
            bool ok = w.Kind == Wo187Rules.OppLocalPlayer || _ghostEntityIds.TryGetValue(w.Ghost.ToString(CultureInfo.InvariantCulture), out target);
            bool changed = !_w187OppLast.TryGetValue(ev.Name, out var last) || last.Kind != w.Kind || last.Ghost != w.Ghost;
            if (ok && (changed || now - last.SentMs >= Wo187Rules.OppRefreshMs))
            {
                _w187OppLast[ev.Name] = (w.Kind, w.Ghost, now);
                _ = Wo187SendOppAsync(ev.Name, eid, w.Kind, target, w.Why, changed);
            }
        }
        // C2: the guard hold
        if (_w187Guard && Wo187Rules.GuardHoldWants(ev) && !_w132Engaged.ContainsKey(ev.Name)   // an engaged copy holds combat already
            && (!_w187GuardLast.TryGetValue(ev.Name, out long gl) || now - gl >= Wo187Rules.GuardRefreshMs))
        {
            _w187GuardLast[ev.Name] = now;
            _ = Wo187SendGuardAsync(eid, true, ev.State);
        }
        // A1: is the host's own NPC within range of this player?
        bool mine = _myGhostId != 0xFF && ev.Target == NpcCombatTarget.Avatar && ev.TargetGhost == _myGhostId;
        if (!mine || !_w187Stream.TryGetValue(ev.Name, out var sp)) return false;
        float d = Wo187Rules.Dist2D(sp.X, sp.Y, _lastX, _lastY);
        long age = W147StampMs(Stopwatch.GetTimestamp()) - sp.ReadMs;
        return Wo187Rules.IntentInRange(_w187EngageIntent, mine, d, age);
    }

    private async Task Wo187SendOppAsync(string name, uint eid, byte kind, uint target, byte why, bool changed)
    {
        var a = new byte[10];
        BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
        a[4] = kind;
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(5), target);
        a[9] = why;
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo187OpOpponent, a);
            Interlocked.Increment(ref _w187OppSent);
            if (r is { Ok: true, Payload.Length: >= 1 } x && x.Payload[0] == 1) Interlocked.Increment(ref _w187OppApplied);
            if (changed)
                Console.WriteLine($"MP-W187 opponent npc={name} wished={(kind == Wo187Rules.OppLocalPlayer ? "me" : $"figure eid 0x{target:X}")} why={why} -> {(r is null ? "no answer" : r.Value.Ok ? (r.Value.Payload.Length > 0 && r.Value.Payload[0] == 1 ? "applied" : "asked") : $"refused (reason {r.Value.Reason})")}");
        }
        catch { }
    }

    private async Task Wo187SendGuardAsync(uint eid, bool on, BodyState2 st)
    {
        var a = new byte[5 + BodyState2.Len];
        BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
        a[4] = (byte)(on ? 1 : 0);
        st.Write(a.AsSpan(5));
        try { await _combat.Wo163Async(Protocol.Wo187OpGuardHold, a); Interlocked.Increment(ref _w187GuardSent); } catch { }
    }

    /// <summary>Host (OnNpcCombatStateAsync): an NPC fighting someone -- B1's list.</summary>
    private void Wo187NoteHostFight(NpcCombatState s, NpcCombatEvent ev)
    {
        if (ev.Target == NpcCombatTarget.None && !ev.InCombat) return;
        _w187Fights[s.Name] = (s.Eid, ev.Target, ev.TargetGhost, Environment.TickCount64);
    }

    // ---------------------------------------------------------------- C2: the host NPC's own defence on its copy (joiner)

    private async Task Wo187PlayDefenceAsync(string npc, uint eid, Guid rowGuid, ActionRowCatalog.Row row, CancellationToken ct)
    {
        Interlocked.Increment(ref _w187DefenceIn);
        if (!_w187Defence)
        {
            Console.WriteLine($"MP-ACTION section=inbound kind=NpcAttack npc={npc} row={rowGuid} dispatch=off (mp_anim_defence off: the host NPC's defence is not shown)");
            return;
        }
        _ = _combat.NpcHoldAsync(npc, 900, ct);   // the writer's hold for the move (its step back stays its own)
        var r = await _combat.GhostSwingForResultAsync(eid, row.Spec, ct);
        if (r.Ok) Interlocked.Increment(ref _w187DefencePlayed);
        Console.WriteLine($"MP-ACTION section=inbound kind=NpcAttack npc={npc} row={rowGuid} spec=\"{row.Spec}\" table={row.Table} dispatch=native-defence result={r.ReasonTag}");
    }

    // ---------------------------------------------------------------- C1: a hit reaction that did not play

    /// <summary>
    /// Joiner (mp_anim_hit_fallback): the host NPC's reaction row did not play on its copy -- the copy's own weapon's reaction row for the
    /// blow's zone (its weapon from the last row it played), or a weaponless one. Null = no stand-in row (said, nothing played).
    ///   WO187-HITFALLBACK npc=&lt;n&gt; row=&lt;failed&gt; why=&lt;its result&gt; fallback=&lt;row&gt; spec="..." result=&lt;tag&gt;
    /// </summary>
    private async Task<PipeResult?> Wo187HitFallbackAsync(string npc, uint eid, Guid failedGuid, ActionRowCatalog.Row failed, string why, CancellationToken ct,
                                                          bool secondTry = false)
    {
        ActionRowCatalog? cat = _rowCatalog.IsCompletedSuccessfully ? _rowCatalog.Result : null;
        if (cat is null) return null;
        Interlocked.Increment(ref _w187HitFallbacks);
        ActionRowCatalog.Row? body = (_w165LastRow.TryGetValue(npc, out var lg) || _w187FigureRow.TryGetValue(npc, out lg)) && cat.TryGet(lg, out var sr) ? sr : null;
        // WO-190 A1 (mp_anim_standin_weapon): the weapon the body really holds, read from its hands -- the last row it played may be another
        // weapon's (a figure's _w187FigureRow is never cleared), and a bare-handed move on an armed figure was the field's T-pose
        var (hands, handTags) = await Wo190HandsAsync(eid);
        if (Wo190Rules.BodyFromHands(hands, handTags, body?.ActorClass ?? 0) is { } held) body = held;
        // a body that played no weapon row yet: bare hands first (live: on a bare-handed figure a weapon's reaction row is dropped at once, while
        // a body holding any weapon plays any weapon's row); not shown either -> the second try takes the best full-body reaction of any weapon
        bool guessed = body is null && !secondTry;
        if (guessed) body = new ActionRowCatalog.Row("guess", "", Wo187Rules.BareHandsTags, -1, -1, -1);
        if (cat.HitFallbackRow(failedGuid, failed, body, t => { bool ok = Wo190Rules.StandinAllowed(hands, t); if (!ok) Interlocked.Increment(ref _w190StandinFiltered); return ok; }) is not { } fb)
        {
            Interlocked.Increment(ref _w187HitFallbackNone);
            Console.WriteLine($"WO187-HITFALLBACK npc={npc} row={failedGuid} why={why} fallback=none -- no reaction row of this actor class (nothing played)");
            return null;
        }
        var r = await _combat.GhostSwingForResultAsync(eid, fb.Row.Spec, ct);
        if (r.Ok) Interlocked.Increment(ref _w187HitFallbackPlayed);
        string weapon = body is { } b ? (guessed ? "bare-hands(guessed)" : string.Join('+', Wo165Rules.WeaponTags(b.Tags)) + (hands != Wo190Rules.HandsUnknown ? "(its hands)" : "")) : "any(second try)";
        Console.WriteLine($"WO187-HITFALLBACK npc={npc} row={failedGuid} why={why} fallback={fb.Guid} spec=\"{fb.Row.Spec}\" weapon={weapon} result={r.ReasonTag}");
        if (r.Ok && guessed)
        {
            await Task.Delay(Wo187Rules.MoveFirstLookMs, ct);
            byte shown = await Wo187MoveShownAsync(eid, ct);
            if (shown == Wo187Rules.MovePending) { await Task.Delay(Wo187Rules.MoveSecondLookMs - Wo187Rules.MoveFirstLookMs, ct); shown = await Wo187MoveShownAsync(eid, ct); }
            if (shown == Wo187Rules.MoveDropped) return await Wo187HitFallbackAsync(npc, eid, failedGuid, failed, "the bare-hands stand-in not shown either", ct, secondTry: true) ?? r;
        }
        return r;
    }

    /// <summary>
    /// Both roles (mp_anim_avatar_hit): the partner took a blow on his screen -- his figure here plays the reaction his engine committed (the
    /// same combat_action_hit row), unless it lies dead or fell from a knockdown (WO-155). A row that is not shown on the figure gets a stand-in
    /// (mp_anim_hit_fallback; the figure's weapon from the last weapon row the partner played on it, bare hands first when none yet).
    ///   MP-ACTION section=inbound ghost=&lt;g&gt; kind=AvatarHit row=&lt;r&gt; spec="..." dispatch=native-hit result=&lt;tag&gt;
    /// </summary>
    private async Task Wo187OnAvatarHitInAsync(InboundAction a, ActionRowCatalog? catalog, CancellationToken ct)
    {
        if (!RowEvent.TryFromBytes(a.Payload, out var he)) return;
        Interlocked.Increment(ref _w187AvatarHitIn);
        string gid = a.SourceGhostId.ToString();
        if (!_w187AvatarHit) { Console.WriteLine($"MP-ACTION section=inbound ghost={gid} kind=AvatarHit row={he.Row} dispatch=dropped-toggle-off"); return; }
        if (catalog is null || !catalog.TryGet(he.Row, out var row))
        {
            _w121EvNoRow++;
            Console.WriteLine($"MP-ACTION section=inbound ghost={gid} kind=AvatarHit row={he.Row} dispatch=dropped-unknown-row");
            return;
        }
        bool known = _ghostEntityIds.TryGetValue(gid, out uint feid);
        byte g = a.SourceGhostId;
        bool down = _w155Collapsed.ContainsKey(g) || _w155Fell.ContainsKey(g) || (_w154PeerDown.TryGetValue(g, out var de) && de.Down);
        if (!Wo187Rules.AvatarHitPlayable(known, down))
        {
            Console.WriteLine($"MP-ACTION section=inbound ghost={gid} kind=AvatarHit row={he.Row} dispatch=skipped ({(known ? "his figure is down here" : "no figure here yet")})");
            return;
        }
        _ = _combat.NpcHoldAsync("kcd2mp_" + gid, 800, ct);
        var r = await _combat.GhostSwingForResultAsync(feid, row.Spec, ct);
        Console.WriteLine($"MP-ACTION section=inbound ghost={gid} kind=AvatarHit row={he.Row} table={row.Table} spec=\"{row.Spec}\" dispatch=native-hit result={r.ReasonTag}");
        if (!r.Ok && _w187HitFallback) r = await Wo187HitFallbackAsync("kcd2mp_" + gid, feid, he.Row, row, r.ReasonTag, ct) ?? r;
        else if (r.Ok && _w187HitFallback) _ = Wo187HitShownAsync("kcd2mp_" + gid, feid, he.Row, row, ct);
        if (r.Ok)
        {
            Interlocked.Increment(ref _w187AvatarHitPlayed);
            await ExecLuaAsync($"if KCD2MP_GhostNativeSwingHold then KCD2MP_GhostNativeSwingHold(\"{gid}\") end");
        }
    }

    /// <summary>
    /// A reaction row the DLL queued: was it animated? (op 41, the move watcher: its action left the body within 0.1 s, or never came -- a
    /// row of another actor class, a fragment this body's animation set lacks). Not shown: the stand-in plays (mp_anim_hit_fallback).
    /// An older DLL (no op 41) or a pending answer after the second look: left as it is.
    /// </summary>
    private async Task Wo187HitShownAsync(string body, uint eid, Guid rowGuid, ActionRowCatalog.Row row, CancellationToken ct)
    {
        try
        {
            await Task.Delay(Wo187Rules.MoveFirstLookMs, ct);
            byte shown = await Wo187MoveShownAsync(eid, ct);
            if (shown == Wo187Rules.MovePending) { await Task.Delay(Wo187Rules.MoveSecondLookMs - Wo187Rules.MoveFirstLookMs, ct); shown = await Wo187MoveShownAsync(eid, ct); }
            if (shown != Wo187Rules.MoveDropped) return;
            await Wo187HitFallbackAsync(body, eid, rowGuid, row, "not-shown(its action left the body at once)", ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Console.WriteLine($"WO187-HITFALLBACK npc={body} watch failed ({ex.GetType().Name}: {ex.Message})"); }
    }

    private async Task<byte> Wo187MoveShownAsync(uint eid, CancellationToken ct)
    {
        var a = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
        var r = await _combat.Wo163Async(Protocol.Wo187OpMoveShown, a, ct);
        return r is { Ok: true, Payload.Length: >= 1 } x ? x.Payload[0] : Wo187Rules.MoveUnknown;
    }

    // ---------------------------------------------------------------- B3: the recoil that has no row, once per fight

    /// <summary>True the first time in a fight (5 minutes without one) this NPC's blocked blow found no recoil row for its weapon.</summary>
    private bool Wo187RecoilNoRowFirst(string npc)
    {
        long now = Environment.TickCount64;
        bool first = !_w187RecoilSaid.TryGetValue(npc, out long at) || now - at > 300_000;
        _w187RecoilSaid[npc] = now;
        if (first) Interlocked.Increment(ref _w187RecoilNoRowSaid);
        return first;
    }

    // ---------------------------------------------------------------- B1: fight positions

    /// <summary>
    /// Once a second, every NPC in a fight with a player: both positions on this machine and, on a joiner, the host's own position of the NPC
    /// from its stream (with its age and the NPC's samples per second), stamped with the relay's clock -- the same clock on both machines, so
    /// a replay puts the host's line and the joiner's side by side.
    ///   WO187-FIGHTPOS role=host npc=<n> shared=<hh:mm:ss.fff> npc_at=x,y target=<who> target_at=x,y dist_m=<d>
    ///   WO187-FIGHTPOS role=joiner npc=<n> shared=... copy_at=x,y stream_at=x,y stream_age_ms=<a> pps=<p> me_at=x,y dist_copy_m=<d> dist_stream_m=<d> lag_m=<l> target=<who>
    /// </summary>
    private async Task Wo187FightPosAsync(CancellationToken ct)
    {
        if (_w187Fights.IsEmpty || _where is GameWhere.Menu or GameWhere.Loading) return;
        long now = Environment.TickCount64;
        var live = _w187Fights.Where(kv => now - kv.Value.AtMs <= 3000 && kv.Value.Target != NpcCombatTarget.Other).Take(6).ToList();
        if (live.Count == 0) return;
        bool host = _isDamageAuthority;
        var eids = new List<uint> { 0 };   // the local player first
        foreach (var (_, f) in live)
        {
            eids.Add(f.Eid);
            if (host && f.Target == NpcCombatTarget.Avatar && _ghostEntityIds.TryGetValue(f.Ghost.ToString(CultureInfo.InvariantCulture), out uint fe)) eids.Add(fe);
        }
        if (eids.Count > 16) eids = eids.Take(16).ToList();
        var a = new byte[1 + 4 * eids.Count];
        a[0] = (byte)eids.Count;
        for (int i = 0; i < eids.Count; ++i) BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(1 + 4 * i), eids[i]);
        (bool Ok, byte Reason, byte[] Payload)? r;
        try { r = await _combat.Wo163Async(Protocol.Wo187OpPositions, a, ct); } catch { return; }
        if (r is not { Ok: true } x || x.Payload.Length < 13 * eids.Count) return;
        var pos = new Dictionary<uint, (float X, float Y)>();
        for (int i = 0; i < eids.Count; ++i)
            if (x.Payload[13 * i] == 1)
                pos[eids[i]] = (BinaryPrimitives.ReadSingleLittleEndian(x.Payload.AsSpan(13 * i + 1)), BinaryPrimitives.ReadSingleLittleEndian(x.Payload.AsSpan(13 * i + 5)));
        string shared = Wo187Rules.SharedClock(DateTime.UtcNow, ClockOffsetMs);
        if (!pos.TryGetValue(0, out var me)) return;
        foreach (var (name, f) in live)
        {
            if (!pos.TryGetValue(f.Eid, out var np)) continue;
            string tgt = f.Target == NpcCombatTarget.Host ? "host" : f.Target == NpcCombatTarget.Avatar ? $"avatar:{f.Ghost}" : "none";
            if (host)
            {
                (float X, float Y) tp = f.Target == NpcCombatTarget.Host ? me
                    : _ghostEntityIds.TryGetValue(f.Ghost.ToString(CultureInfo.InvariantCulture), out uint fe) && pos.TryGetValue(fe, out var fp) ? fp : (float.NaN, float.NaN);
                Console.WriteLine(Wo187Rules.HostPosLine(name, shared, np.X, np.Y, tgt, tp.X, tp.Y));
            }
            else
            {
                (float, float, long, int, uint)? stream = null;
                if (_w187Stream.TryGetValue(name, out var sp)) stream = (sp.X, sp.Y, W147StampMs(Stopwatch.GetTimestamp()) - sp.ReadMs, sp.Pps, sp.SenderMs);
                Console.WriteLine(Wo187Rules.JoinerPosLine(name, shared, np.X, np.Y, stream, me.X, me.Y, tgt));
            }
            Interlocked.Increment(ref _w187PosLines);
        }
    }
}
