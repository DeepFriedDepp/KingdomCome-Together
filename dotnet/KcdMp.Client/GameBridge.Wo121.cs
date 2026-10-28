// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-121 -- movement and combat, the agent half (protocol v8, ProtocolV8.cs).
///
/// Sender: the v8 state block rides the Position packet, change-gated; the
/// DLL's committed-action frames (0x96) become ActionUp events stamped with the
/// Position clock; a local hit on a peer's avatar (0x97) becomes a PlayerHit
/// when friendly fire is on.
///
/// Receiver: the state block goes to the DLL with the ghost's sample (the
/// native gait/crouch/combat applier, NativeNpc.cs FlagState2); events are
/// judged against the sender's newest Position stamp and dispatched -- an
/// attack or block/dodge row plays the row's own fragment on the avatar
/// through the WO-46 cosmetic route; a jump is a native RequestJump; a
/// PlayerHit lands on our own Henry with no attacker attached.
///
/// Policy stays thin on purpose (the maintainer's native-first rule): this file
/// routes and counts; the engine calls are in KCDMP.dll (motion.cpp, hits.cpp).
/// </summary>
public partial class GameBridge
{
    // ---- mirrors of the mod's WO-121 toggles (Lua emits wo121_cfg) --------
    private volatile bool _avatarGait = true, _npcGait = true, _avatarMoves = true, _avatarCombat = true, _npcRows = true;
    private volatile bool _npcAttribution = true;

    /// <summary>
    /// The build's friendly-fire default. WO-121: ON only if the Phase 6 gate
    /// passes; see docs/WO-121-findings.md. The host's mp_friendly_fire
    /// overrides it for the whole session.
    /// </summary>
    internal const bool FriendlyFireDefault = true;

    /// <summary>This machine's own mp_friendly_fire (it only counts on the host).</summary>
    private volatile bool _ffLocalPref = FriendlyFireDefault;
    /// <summary>The session's value: the host's lever (the host's own pref, or the last SessionSetting a joiner got).</summary>
    private volatile bool _ffSession = FriendlyFireDefault;
    private volatile bool _ffFromHost;

    private Stream? _wo121Stream;
    private CancellationToken _wo121Ct;
    private readonly ConcurrentDictionary<byte, uint> _ghostLastSenderMs = new();
    private readonly ConcurrentDictionary<byte, DateTime> _peerV8AttackAt = new();
    private readonly ConcurrentDictionary<byte, DateTime> _peerState2At = new();
    private readonly ConcurrentDictionary<byte, BodyState2> _peerLastState2 = new();
    private readonly ConcurrentDictionary<string, DateTime> _npcRowAt = new();

    // WO-121: authored combat rows by GUID, read from this install's Tables.pak.
    private readonly Task<ActionRowCatalog?> _rowCatalog = Task.Run(() => ActionRowCatalog.TryLoad(Console.WriteLine));

    // The v8 state-block change gate (sender).
    private BodyState2? _st2LastSent;
    private long _st2LastSentTs;

    // Whether the local DLL captures committed attacks (Wo121Status) -- when it
    // does, the Lua "combat swing" cue (0x2C) is not sent: the v8 Attack event
    // is the swing, and sending both would play two.
    private volatile bool _dllAttackCapture;
    private string _dllWo121Status = "";

    private long _w121EvOut, _w121EvIn, _w121EvStale, _w121EvNoRow, _w121EvNoBody, _w121Swings, _w121Jumps;
    private long _w121FfOut, _w121FfOutDropped, _w121FfIn, _w121FfInDropped, _w121AttribIn, _w121AttribOut, _w121NpcRowsOut, _w121NpcRowsIn;

    private void Wo121OnConnect(Stream stream, CancellationToken ct)
    {
        _wo121Stream = stream;
        _wo121Ct = ct;
        _st2LastSent = null;
        _combat.OnLocalAction = OnLocalActionAsync;
        _combat.OnPvpHit = OnPvpHitAsync;
        _ = PushWo121ConfigAsync(ct);
        _ = Wo121HeartbeatAsync(ct);
    }

    // WO-121 Phase 5: peers whose avatar is in an attributed engagement here
    // (not ignorant) -> when it ends (30 s after the last attributed hit).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<byte, DateTime> _w121Engaged = new();
    private static readonly TimeSpan Wo121EngagementHold = TimeSpan.FromSeconds(30);

    private async Task Wo121EngageAsync(byte source, bool on)
    {
        await ExecLuaAsync($"if KCD2MP_Wo121Engage then KCD2MP_Wo121Engage({source}, {(on ? "true" : "false")}) end");
        Console.WriteLine($"MP-ATTRIB engagement ghost={source} {(on ? "started -- the avatar is perceivable until 30 s after the last attributed hit" : "ended -- ignorance back to the session setting")}");
    }

    private void Wo121OnDisconnect()
    {
        _w121Engaged.Clear();
        _wo121Stream = null;
        _combat.OnLocalAction = null;
        _combat.OnPvpHit = null;
        _ffFromHost = false;
        _ffSession = _isDamageAuthority ? _ffLocalPref : FriendlyFireDefault;
    }

    private async Task PushWo121ConfigAsync(CancellationToken ct)
    {
        try
        {
            var m = await _combat.MotionConfigAsync(_avatarGait, _npcGait, _avatarMoves, _avatarCombat, _npcRows, _avatarQuiet, _w154GaitHyst, ct);
            var h = await _combat.HitsConfigAsync(_ffSession, _npcAttribution, true, ct);
            Console.WriteLine(FormattableString.Invariant(
                $"MP-WO121 cfg avatar_gait={On(_avatarGait)} npc_gait={On(_npcGait)} avatar_moves={On(_avatarMoves)} avatar_combat={On(_avatarCombat)} npc_rows={On(_npcRows)} avatar_quiet=0x{_avatarQuiet:X} attribution={On(_npcAttribution)} friendly_fire={On(_ffSession)} ff_from={(_ffFromHost ? "host" : _isDamageAuthority ? "self-host" : "default")} dll_motion={m.ReasonTag} dll_hits={h.ReasonTag}"));
        }
        catch (Exception ex) { Console.WriteLine($"MP-WO121 cfg push failed: {ex.Message}"); }
    }

    private static string On(bool b) => b ? "on" : "off";

    /// <summary>The mod's toggles, one event: <c>wo121_cfg avatar_gait=on npc_gait=on ... ff=on</c>.</summary>
    private void Wo121OnCfgEvent(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return;
        bool ffChanged = false;
        foreach (var kv in arg.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq <= 0) continue;
            string k = kv[..eq]; bool v = kv[(eq + 1)..] == "on";
            switch (k)
            {
                case "avatar_gait": _avatarGait = v; break;
                case "npc_gait": _npcGait = v; break;
                case "avatar_moves": _avatarMoves = v; break;
                case "avatar_combat": _avatarCombat = v; break;
                case "npc_rows": _npcRows = v; break;
                case "attribution": _npcAttribution = v; break;
                case "ff": ffChanged = _ffLocalPref != v; _ffLocalPref = v; break;
            }
        }
        if (_isDamageAuthority)
        {
            _ffSession = _ffLocalPref;
            if (ffChanged) _ = SendSessionSettingAsync("toggle");
        }
        else if (ffChanged)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"MP-FF local mp_friendly_fire={On(_ffLocalPref)} ignored -- the host's lever decides (session={On(_ffSession)})"));
            _ = ExecLuaAsync($"if KCD2MP_FriendlyFireSession then KCD2MP_FriendlyFireSession({(_ffSession ? "true" : "false")}, \"host-controlled\") end");
        }
        _ = PushWo121ConfigAsync(_wo121Ct);
    }

    /// <summary>The host tells every peer the session's friendly-fire value (on change, on a new peer, every 30 s).</summary>
    private async Task SendSessionSettingAsync(string why)
    {
        if (!_isDamageAuthority || _wo121Stream is not Stream s) return;
        try
        {
            var pkt = _actionOut.Build(ActionKind.SessionSetting, ActionPhase.Commit,
                                       [SessionSettingKey.FriendlyFire, _ffSession ? (byte)1 : (byte)0]);
            await WritePacketAsync(s, pkt, _wo121Ct);
            Console.WriteLine($"MP-FF host sent friendly_fire={On(_ffSession)} why={why}");
        }
        catch (Exception ex) { Console.WriteLine($"MP-FF host send failed: {ex.Message}"); }
    }

    private async Task Wo121HeartbeatAsync(CancellationToken ct)
    {
        var lastSetting = DateTime.MinValue;
        var lastStats = DateTime.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); } catch { return; }
            try
            {
                if (_w121Engaged.Count > 0)
                {
                    var now = DateTime.UtcNow;
                    foreach (var (src, until) in _w121Engaged.ToArray())
                        if (now >= until && _w121Engaged.TryRemove(src, out _)) await Wo121EngageAsync(src, false);
                }
                string? st = await _combat.Wo121StatusAsync(ct);
                if (st is not null)
                {
                    _dllWo121Status = st;
                    bool cap = st.Contains("attack_capture=armed", StringComparison.Ordinal);
                    if (cap != _dllAttackCapture)
                        Console.WriteLine($"MP-WO121 dll attack_capture={(cap ? "armed" : "off")} -- the Lua swing cue (0x2C) is {(cap ? "suppressed" : "sent")}");
                    _dllAttackCapture = cap;
                    bool gait = st.Contains("gait=armed", StringComparison.Ordinal);
                    bool combat = st.Contains("combat=armed", StringComparison.Ordinal);
                    await ExecLuaAsync(FormattableString.Invariant(
                        $"if KCD2MP_Wo121Alive then KCD2MP_Wo121Alive({(gait ? "true" : "false")},{(combat ? "true" : "false")},{(_ffSession ? "true" : "false")}) end"));
                }
                if (_isDamageAuthority && (DateTime.UtcNow - lastSetting).TotalSeconds >= 30)
                {
                    lastSetting = DateTime.UtcNow;
                    _ffSession = _ffLocalPref;
                    await SendSessionSettingAsync("heartbeat");
                }
                if ((DateTime.UtcNow - lastStats).TotalSeconds >= 60)
                {
                    lastStats = DateTime.UtcNow;
                    Console.WriteLine(Wo121StatsLine());
                }
            }
            catch (OperationCanceledException) { return; }
            catch { }
        }
    }

    private string Wo121StatsLine() => string.Create(CultureInfo.InvariantCulture,
        $"MP-WO121-STATS ev_out={_w121EvOut} ev_in={_w121EvIn} ev_stale={_w121EvStale} ev_norow={_w121EvNoRow} ev_nobody={_w121EvNoBody} swings={_w121Swings} jumps={_w121Jumps} " +
        $"npc_rows_out={_w121NpcRowsOut} npc_rows_in={_w121NpcRowsIn} ff_out={_w121FfOut} ff_out_dropped={_w121FfOutDropped} ff_in={_w121FfIn} ff_in_dropped={_w121FfInDropped} " +
        $"attrib_out={_w121AttribOut} attrib_in={_w121AttribIn} ff_session={On(_ffSession)} dll=\"{_dllWo121Status}\"");

    // =====================================================================
    // Sender
    // =====================================================================

    /// <summary>
    /// The state block to put on this Position packet, or null. Change-gated:
    /// a field change (speed by 10 cm/s or more, direction by 3 units, any
    /// bit/zone), or the 1 s heartbeat while anything is non-zero.
    /// <paramref name="due"/> is true when a packet should go out for the
    /// state alone (the body stood still but, say, raised a block).
    /// </summary>
    private BodyState2? Wo121State2For(LocalState? nat, long nowTs, bool consume, out bool due)
    {
        due = false;
        if (nat?.State2 is not BodyState2 c) return null;
        c = Wo136WithTorch(c);   // WO-136 Phase 5: the torch in the hand rides the state block
        bool changed = _st2LastSent is not BodyState2 l
            || Math.Abs(l.SpeedCm - c.SpeedCm) >= 10 || Math.Abs(l.MoveDir - c.MoveDir) >= 3
            || l.Bits != c.Bits || l.GuardZone != c.GuardZone || l.GuardStance != c.GuardStance || l.AtkZone != c.AtkZone
            || (c.SpeedCm == 0 && l.SpeedCm != 0);
        bool nonZero = c.SpeedCm != 0 || c.Bits != BodyState2Bits.None;
        bool hb = nonZero && Stopwatch.GetElapsedTime(_st2LastSentTs, nowTs).TotalMilliseconds >= Protocol.BodyState2HeartbeatMs;
        due = changed || hb;
        if (!due) return null;
        if (consume) { _st2LastSent = c; _st2LastSentTs = nowTs; }
        return c;
    }

    /// <summary>The DLL committed an action on this machine (0x96).</summary>
    private async Task OnLocalActionAsync(LocalActionFrame f)
    {
        if (_wo121Stream is not Stream s) return;
        uint ms = SenderMsNow();
        byte[]? pkt = null;
        string what;
        if (f.Eid == 0)
        {
            switch ((ActionKind)f.Kind)
            {
                case ActionKind.Attack:
                    // WO-187 C2: the DLL marks a player's sync attack (a combo step, a throw) 0x04; on the wire it is the v8 FlagCombo
                    byte aflags = (f.Flags & Wo187Rules.NativeFlagPlayerSync) != 0 ? AttackEvent.FlagCombo : f.Flags;
                    if ((f.Flags & Wo187Rules.NativeFlagPlayerSync) != 0 && !_w187Combo) return;
                    var ae = new AttackEvent(ms, f.InputClass, Protocol.ZoneFromTableId(f.ZoneTableId), f.AttackType, aflags, f.Row);
                    pkt = _actionOut.Build(ActionKind.Attack, ActionPhase.Commit, ae.ToBytes());
                    if (_npcTarget is string w191T) Wo191NoteFight(w191T, "a-player-swings-at-it");   // WO-191 2: the copy this player faces
                    what = ae.ToString();
                    break;
                case ActionKind.Jump:
                    var jb = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(jb, ms);
                    pkt = _actionOut.Build(ActionKind.Jump, ActionPhase.Commit, jb);
                    what = "jump";
                    break;
                case ActionKind.BlockImpulse:
                case ActionKind.Dodge:
                    pkt = _actionOut.Build((ActionKind)f.Kind, ActionPhase.Commit, new RowEvent(ms, f.Flags, f.Row, "").ToBytes());
                    what = $"row={f.Row} flags={f.Flags}";
                    break;
                case ActionKind.AvatarHit:
                    // WO-187 C1 (mp_anim_avatar_hit): this player's own hit reaction -- his figure on the partner's screen plays it
                    if (!_w187AvatarHit) return;
                    pkt = _actionOut.Build(ActionKind.AvatarHit, ActionPhase.Commit, new RowEvent(ms, 0, f.Row, "").ToBytes());
                    what = $"row={f.Row}";
                    if (_rowCatalog.IsCompletedSuccessfully && _rowCatalog.Result is { } hcat)
                        what += hcat.TryGet(f.Row, out var hk) ? $" table={hk.Table} spec=\"{hk.Spec}\"" : " table=UNKNOWN";
                    Interlocked.Increment(ref _w187AvatarHitOut);
                    break;
                case ActionKind.AvatarRecoil:   // WO-197 B1
                case ActionKind.AvatarClinch:   // WO-197 B3
                    pkt = Wo197OutboundPacket(f, ms, out what);
                    if (pkt is null) return;
                    break;
                default: return;
            }
        }
        else
        {
            // An NPC's committed attack: the owner streams it so the NPC copies
            // swing that exact row. Only the owner (the host under host
            // authority) speaks for its NPCs; a name must be authored.
            // WO-151: and its hit reaction (NpcHit), so a copy shows the host's reaction, not its own.
            bool npcHit = (ActionKind)f.Kind == ActionKind.NpcHit;
            if (((ActionKind)f.Kind != ActionKind.Attack && !npcHit) || !_npcRows || !_isDamageAuthority) return;
            if (npcHit && !_w151Reactions) return;
            if (!NpcNamePattern.IsMatch(f.Name) || Protocol.IsNeverSyncedNpcName(f.Name)) return;
            if (!Wo163OutboundRowOk(f)) return;
            // WO-210 D.4: an NPC's attack row -- its opponent's word right behind it, not up to a stream tick later (the joiner's copy waits for
            // it before it swings). Off this handler: the pipe's reader awaits it, a pipe call here would wait on that very reader.
            if (!npcHit && W210("opp_with_row") && W137Host) { Interlocked.Increment(ref _w210OppWithRow); _ = Task.Run(Wo209OppTickAsync); }
            pkt = _actionOut.Build(npcHit ? ActionKind.NpcHit : ActionKind.NpcAttack, ActionPhase.Commit, new RowEvent(ms, 0, f.Row, f.Name).ToBytes());
            what = $"npc={f.Name} row={f.Row}";
            // WO-151: the table the row came from (a hit or a paired bite proves the DLL's descriptor read)
            if (_rowCatalog.IsCompletedSuccessfully && _rowCatalog.Result is { } cat)
                what += cat.TryGet(f.Row, out var known) ? $" table={known.Table} spec=\"{known.Spec}\"" : " table=UNKNOWN";
            if (npcHit) Interlocked.Increment(ref _w151HitRowsOut); else _w121NpcRowsOut++;
        }
        await WritePacketAsync(s, pkt, _wo121Ct);
        _w121EvOut++;
        var outKind = f.Eid == 0 ? (ActionKind)f.Kind : (ActionKind)f.Kind == ActionKind.NpcHit ? ActionKind.NpcHit : ActionKind.NpcAttack;
        if (f.Eid != 0 && outKind == ActionKind.NpcAttack) Wo161NoteSwingOut(f.Name, f.Row);   // WO-161: the swing is in the host's ledger (its id goes with the hit it causes)
        Console.WriteLine(FormattableString.Invariant($"MP-ACTION section=outbound kind={outKind} gen={_actionOut.Gen} {what}"));
    }

    /// <summary>The local player hit a peer's avatar (0x97). The avatar kept nothing; the damage goes to its owner.</summary>
    private async Task OnPvpHitAsync(uint victimEid, float stamina, float health, byte flags, byte material)
    {
        byte? victim = null;
        foreach (var kv in _ghostEntityIds)
            if (kv.Value == victimEid && byte.TryParse(kv.Key, out byte gid)) { victim = gid; break; }
        string line = FormattableString.Invariant(
            $"MP-FF dir=out victim_eid=0x{victimEid:X} ghost={(victim is byte v0 ? v0.ToString() : "?")} hp={health:F2} st={stamina:F2} unarmed={((flags & PlayerHitV8.FlagUnarmed) != 0 ? 1 : 0)} missile={((flags & PlayerHitV8.FlagMissile) != 0 ? 1 : 0)} session={On(_ffSession)}");
        if (!_ffSession || victim is not byte vid || _wo121Stream is not Stream s || (health <= 0 && stamina <= 0))
        {
            _w121FfOutDropped++;
            Console.WriteLine(line + $" result=dropped reason={(!_ffSession ? "friendly-fire-off" : victim is null ? "unknown-avatar" : _wo121Stream is null ? "no-relay" : "no-damage")}");
            return;
        }
        await WritePacketAsync(s, new PlayerHitV8(vid, stamina, health, flags, material).BuildUp(), _wo121Ct);
        _w121FfOut++;
        Wo155NoteFfSent(vid);   // WO-155: the knockdown this hit causes on his screen is the one his figure falls for here
        Console.WriteLine(line + " result=sent");
    }

    // =====================================================================
    // Receiver
    // =====================================================================

    /// <summary>
    /// Dispatches one inbound WO-121 action. True when this file owned the kind
    /// (whatever it then did); false leaves it to the older handlers.
    /// </summary>
    private async Task<bool> DispatchWo121ActionAsync(InboundAction a, CancellationToken ct)
    {
        switch (a.Kind)
        {
            case ActionKind.NpcCombat:
                await Wo132OnNpcCombatInAsync(a, ct);   // WO-132: the host's NPC in a fight
                return true;
            case ActionKind.Emote:
                await Wo151OnEmoteInAsync(a);   // WO-151 3.5: the partner's whistle at his avatar
                return true;
            case ActionKind.DoorState:
            case ActionKind.DoorAsk:
                await Wo151OnDoorInAsync(a);   // WO-151 3.9: never stale-dropped (a door's state is not a moment)
                return true;
            case ActionKind.SessionSetting:
                if (a.Payload.Length >= 2 && a.Payload[0] == SessionSettingKey.FriendlyFire)
                {
                    bool v = a.Payload[1] != 0;
                    bool changed = !_ffFromHost || _ffSession != v;
                    _ffSession = v; _ffFromHost = true;
                    if (changed)
                    {
                        Console.WriteLine($"MP-FF session friendly_fire={On(v)} from=host ghost={a.SourceGhostId}");
                        await ExecLuaAsync($"if KCD2MP_FriendlyFireSession then KCD2MP_FriendlyFireSession({(v ? "true" : "false")}, \"host\") end");
                        _ = PushWo121ConfigAsync(ct);
                    }
                }
                else if (a.Payload.Length >= 2) Wo154MenuOnSessionSetting(a.Payload[0], a.Payload[1], a.SourceGhostId);   // WO-154: crime mode, fast travel
                return true;
            case ActionKind.Attack:
            case ActionKind.Jump:
            case ActionKind.BlockImpulse:
            case ActionKind.Dodge:
            case ActionKind.NpcAttack:
            case ActionKind.NpcHit:
            case ActionKind.AvatarHit:
            case ActionKind.AvatarRecoil:   // WO-197 B1
            case ActionKind.AvatarClinch:   // WO-197 B3
                break;
            default:
                return false;
        }
        _w121EvIn++;
        uint evMs = a.Payload.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(a.Payload) : 0;
        if (evMs != 0 && _ghostLastSenderMs.TryGetValue(a.SourceGhostId, out uint posMs)
            && unchecked((int)(posMs - evMs)) > Protocol.EventStaleMs)
        {
            _w121EvStale++;
            Console.WriteLine(FormattableString.Invariant(
                $"MP-ACTION section=inbound ghost={a.SourceGhostId} kind={a.Kind} seq={a.Seq} dispatch=dropped-stale behind_ms={unchecked((int)(posMs - evMs))}"));
            return true;
        }
        var catalog = await _rowCatalog;
        switch (a.Kind)
        {
            case ActionKind.Attack:
            {
                if (!AttackEvent.TryFromBytes(a.Payload, out var ae)) { Console.WriteLine($"MP-ACTION section=inbound ghost={a.SourceGhostId} kind=Attack dispatch=dropped-malformed len={a.Payload.Length}"); return true; }
                _peerV8AttackAt[a.SourceGhostId] = DateTime.UtcNow;
                Wo173OnAvatarAttack(a.SourceGhostId, ae.Row, catalog, ct);   // WO-173 C1: the row his next hit belongs to; his figure strikes for its window
                await PlayAvatarRowAsync(a, ae.Row, catalog, ae.ToString(), ct);
                Wo136OnAvatarAttack(a.SourceGhostId);   // WO-136 Phase 4: the avatar fights what it swings at; the swing threatens it
                return true;
            }
            case ActionKind.BlockImpulse:
            case ActionKind.Dodge:
            {
                if (!RowEvent.TryFromBytes(a.Payload, out var re)) return true;
                await PlayAvatarRowAsync(a, re.Row, catalog, $"row={re.Row} flags={re.Flags}", ct);
                return true;
            }
            case ActionKind.Jump:
            {
                if (!_avatarMoves) { Console.WriteLine($"MP-ACTION section=inbound ghost={a.SourceGhostId} kind=Jump dispatch=dropped-toggle-off"); return true; }
                if (!_ghostEntityIds.TryGetValue(a.SourceGhostId.ToString(), out uint jeid))
                {
                    _w121EvNoBody++;
                    Console.WriteLine($"MP-ACTION section=inbound ghost={a.SourceGhostId} kind=Jump dispatch=dropped-no-entity");
                    return true;
                }
                var r = await _combat.AvatarEventAsync(1, jeid, ct);
                _w121Jumps++;
                Console.WriteLine($"MP-ACTION section=inbound ghost={a.SourceGhostId} kind=Jump seq={a.Seq} dispatch=native-jump result={r.ReasonTag}");
                return true;
            }
            case ActionKind.NpcHit:
                await Wo151OnNpcHitInAsync(a, catalog, ct);   // WO-151 Phase 1.1: the host NPC's own reaction on its copy
                return true;
            case ActionKind.AvatarHit:
                await Wo187OnAvatarHitInAsync(a, catalog, ct);   // WO-187 C1: the partner's own hit reaction on his figure
                return true;
            case ActionKind.AvatarRecoil:
                await Wo197OnRecoilInAsync(a, catalog, ct);      // WO-197 B1: the partner's recoil on his figure
                return true;
            case ActionKind.AvatarClinch:
                await Wo197OnClinchInAsync(a, catalog, ct);      // WO-197 B3: the partner's clinch on his figure and its opponent
                return true;
            case ActionKind.NpcAttack:
            {
                if (!RowEvent.TryFromBytes(a.Payload, out var ne) || ne.Name.Length == 0) return true;
                _w121NpcRowsIn++;
                _npcRowAt[ne.Name] = DateTime.UtcNow;
                Wo161NoteRowIn(ne.Name);   // WO-161: a row of this NPC came in (the victim's check of the next hit)
                if (!_npcRows) { Wo161NoteRowRefused(ne.Name, "rows-off"); return true; }
                if (Wo172RowOnDead(ne.Name)) { Wo161NoteRowRefused(ne.Name, "dead"); return true; }   // WO-172 C1: no row on a corpse
                if (catalog is null || !catalog.TryGet(ne.Row, out var row)) { _w121EvNoRow++; Wo161NoteRowRefused(ne.Name, "unknown-row"); Console.WriteLine($"MP-ACTION section=inbound kind=NpcAttack npc={ne.Name} row={ne.Row} dispatch=dropped-unknown-row"); return true; }
                if (!_npcEntityIds.TryGetValue(ne.Name, out uint neid))
                {
                    _w121EvNoBody++;
                    Wo161NoteRowRefused(ne.Name, "no-entity");
                    Console.WriteLine($"MP-ACTION section=inbound kind=NpcAttack npc={ne.Name} dispatch=dropped-no-entity (not a puppet here)");
                    Wo191AskBody(ne.Name, "attack-row-no-body");   // WO-191 3b
                    return true;
                }
                bool animal = _w141Animals.ContainsKey(ne.Name);
                if (animal && !_w141Bites)
                {
                    Wo161NoteRowRefused(ne.Name, "animal-off");
                    Console.WriteLine($"MP-ACTION section=inbound kind=NpcAttack npc={ne.Name} row={ne.Row} dispatch=off (mp_animal_attacks off: an animal's attack is not animated here; its damage still comes through the host)");
                    return true;
                }
                if (!animal && Wo187Rules.IsDefenceRow(row)) { await Wo187PlayDefenceAsync(ne.Name, neid, ne.Row, row, ct); return true; }   // WO-187 C2: its own block / dodge
                var asSent = row;
                if (!animal) row = Wo167RowFor(ne.Name, ne.Row, row, catalog);   // WO-167 C4: a paired row it cannot play is its plain attack
                _ = _combat.NpcHoldAsync(ne.Name, Wo166Rules.SwingHoldMs(row.HitLagMs, _w166SnapFix), ct);   // WO-166 C4: the hold ends with the swing
                if (!animal && row.Table == "combat_action_sync_attack") await Wo197VictimHalfAsync(ne.Name, neid, row, catalog, ct);   // WO-197 B2: the victim's half first
                var r = await _combat.GhostSwingForResultAsync(neid, row.Spec, ct);
                long w167PlayedAt = Environment.TickCount64;
                if (r.Ok && !animal) _ = Wo166StrikeAsync(ne.Name, row, ct);   // WO-166 C3: the copy counts as striking for the swing's window
                if (animal) Interlocked.Increment(ref _w141Bite);
                if (r.Ok) Wo163NoteRowForWatch(ne.Name, row.Spec);
                if (r.Ok) Wo165NoteRowPlayed(ne.Name, ne.Row);   // WO-165: the replay's attack fields and the recoil's weapon tags
                if (r.Ok) Wo161NoteRowPlayed(ne.Name, row.HitLagMs >= 0 ? row.HitLagMs : Wo161Rules.LagNone); else Wo161NoteRowRefused(ne.Name, r.ReasonTag);   // WO-161 / WO-163: with the row's own lag
                if (r.Ok && !animal) { _ = Wo167CheckShownAsync(ne.Name, neid, ne.Row, w167PlayedAt); Wo167RowWeapon(ne.Name, row); }   // WO-167 C3 / C2
                if (r.Ok && !animal && Wo167Rules.IsSyncFragment(row.Fragment) && row.Equals(asSent)) _ = Wo190ClinchCheckAsync(ne.Name, neid, ne.Row, row, catalog, ct);   // WO-190 A8
                Console.WriteLine($"MP-ACTION section=inbound kind=NpcAttack npc={ne.Name} row={ne.Row} spec=\"{row.Spec}\" dispatch=native-row result={r.ReasonTag}");
                if (r.Ok) await ExecLuaAsync($"if KCD2MP_NpcNativeSwingHold then KCD2MP_NpcNativeSwingHold(\"{ne.Name}\") end");
                return true;
            }
        }
        return true;
    }

    /// <summary>The avatar plays the sender's committed row (attack / block / perfect block / dodge).</summary>
    private async Task PlayAvatarRowAsync(InboundAction a, Guid rowGuid, ActionRowCatalog? catalog, string detail, CancellationToken ct)
    {
        if (!_avatarCombat)
        {
            Console.WriteLine($"MP-ACTION section=inbound ghost={a.SourceGhostId} kind={a.Kind} {detail} dispatch=dropped-toggle-off");
            return;
        }
        if (catalog is null || !catalog.TryGet(rowGuid, out var row))
        {
            _w121EvNoRow++;
            Console.WriteLine($"MP-ACTION section=inbound ghost={a.SourceGhostId} kind={a.Kind} {detail} dispatch=dropped-unknown-row");
            return;
        }
        string gid = a.SourceGhostId.ToString();
        if (!_ghostEntityIds.TryGetValue(gid, out _))
        {
            _w121EvNoBody++;
            Console.WriteLine($"MP-ACTION section=inbound ghost={gid} kind={a.Kind} {detail} dispatch=dropped-no-entity (no entity id for the avatar yet)");
            return;
        }
        if (Wo165Rules.WeaponTags(row.Tags).Count > 0) _w187FigureRow["kcd2mp_" + gid] = rowGuid;   // WO-187 C1: his figure's weapon, for a reaction's stand-in
        long rsid = ++_stats.SwingsRecv;
        _w121Swings++;
        Console.WriteLine($"MP-SWING hop=recv rsid={rsid} ghost={gid} kind={a.Kind} seq={a.Seq} table={row.Table} spec=\"{row.Spec}\" {detail}");
        // WO-170 F2: an inbound Attack is dispatched here, as an MP-SWING (the field's 4 swings of the joiner were all received and queued; the
        // MP-ACTION inbound lines only named the kinds that log a dispatch of their own). Said once, with the swing's id.
        Console.WriteLine($"MP-ACTION section=inbound ghost={gid} kind={a.Kind} seq={a.Seq} dispatch=swing-queued rsid={rsid}");
        if (row.Table is "combat_action_attack" or "combat_action_sync_attack") Wo200FigureSwingNoise(gid);   // WO-200 Act IV: a fight the host's world hears
        // WO-200 II.6: a clinch entered by rows whose "leave" never came (the field dropped two as stale) ends before this swing
        if (_ghostEntityIds.TryGetValue(gid, out uint fe200) && row.Table != "combat_action_sync_attack" && _w200ClinchRows.TryRemove(fe200, out var cr200))
            await Wo200EndClinchAsync(fe200, cr200.Opp, "next-swing", ct);
        _ = _combat.NpcHoldAsync("kcd2mp_" + gid, 900, ct);
        if (row.Table == "combat_action_sync_attack" && _ghostEntityIds.TryGetValue(gid, out uint fe197))
            await Wo197VictimHalfAsync("kcd2mp_" + gid, fe197, row, catalog, ct);   // WO-197 B2: his paired move's victim half first
        EnsureSwingInbox(ct).TryEnqueue(new SwingInbox.Entry(gid, a.Seq, rsid, GhostGeneration(gid), row.Spec, DateTime.UtcNow,
                                                             LuaFallback: a.Kind == ActionKind.Attack || !_w187Defence));
        await ExecLuaAsync($"if KCD2MP_GhostNativeSwingHold then KCD2MP_GhostNativeSwingHold(\"{gid}\") end");
    }

    /// <summary>
    /// WO-121: should an old 0x2C combat cue from this peer be skipped? A peer
    /// that sends v8 attack rows (seen in the last minute) swings through
    /// those; a peer whose state block says it is blocking has its block held
    /// natively.
    /// </summary>
    private bool Wo121SupersedesCombatCue(byte ghost, byte evt)
    {
        if (!_avatarCombat) return false;
        if (evt == Protocol.CombatEventSwing)
            return _peerV8AttackAt.TryGetValue(ghost, out var t) && (DateTime.UtcNow - t).TotalSeconds < 60;
        if (evt == Protocol.CombatEventBlock)
            return _peerState2At.TryGetValue(ghost, out var t2) && (DateTime.UtcNow - t2).TotalSeconds < 5;
        return false;
    }

    /// <summary>WO-121: the owner's NPC swings arrive as rows; the WO-49 heuristic cue flag is then ignored for that NPC.</summary>
    private bool Wo121SupersedesNpcCue(string npc) =>
        _npcRows && _npcRowAt.TryGetValue(npc, out var t) && (DateTime.UtcNow - t).TotalSeconds < 60;

    /// <summary>0x45: a partner's friendly-fire hit on our Henry.</summary>
    private async Task OnPlayerHitV8InAsync(byte[] payload, CancellationToken ct)
    {
        if (!PlayerHitV8.TryDecodeDown(payload, out byte attacker, out var hit))
        {
            _w121FfInDropped++;
            Console.WriteLine($"MP-FF dir=in result=dropped reason=malformed len={payload.Length}");
            return;
        }
        string line = FormattableString.Invariant($"MP-FF dir=in attacker={attacker} {hit} session={On(_ffSession)}");
        if (!_ffSession || hit.Victim != _myGhostId)
        {
            _w121FfInDropped++;
            Console.WriteLine(line + $" result=dropped reason={(!_ffSession ? "friendly-fire-off" : "not-me")}");
            return;
        }
        var r = await _combat.ApplyPvpHitAsync(hit.Stamina, hit.Health, Wo155PvpFlags(hit.Flags), attacker, ct);
        _w121FfIn++;
        Console.WriteLine(line + $" result={(r.Ok ? "applied" : "failed")} reason={r.ReasonTag}");
    }

    /// <summary>
    /// WO-121 Phase 5: an ATTRIBUTED hit from peer <paramref name="source"/> on
    /// a local NPC, on the NPC's authority. Returns null when attribution does
    /// not apply here (the caller applies the plain damage as before).
    /// </summary>
    private async Task<bool?> TryApplyAttributedAsync(byte source, string npcName, Guid localGuid, float stamina, float health,
                                                      byte flags, CancellationToken ct)
    {
        if (!_npcAttribution || !_isDamageAuthority || (flags & Protocol.NpcDamageFlagAttributed) == 0) return null;
        if (!_ghostEntityIds.TryGetValue(source.ToString(), out uint avatarEid)) return null;
        // WO-173 C1: a real hit through the engine (or the capped raw path); an older DLL -> WO-121's path as before
        var r = await Wo173AttributedAsync(source, npcName, localGuid, stamina, health, flags, avatarEid, ct)
                ?? await _combat.AttributedDamageAsync(localGuid, stamina, health, flags, avatarEid, npcName, ct);
        _w121AttribIn++;
        if (Wo176Rules.KeptOut(r.Reason) && !r.Ok)
        {
            // WO-176 A1: a quest's arranged fight -- the DLL routed nothing into it (no damage, history, skirmish or threat); no engage, no
            // brain message, nothing for the crime and fight watchers. The real-hit path already told the joiner; the older path tells him here.
            if (r.Reason == Wo176Rules.AttribReasonKept) await Wo176KeptAsync(source, npcName, "wo121");
            Console.WriteLine(FormattableString.Invariant(
                $"MP-ATTRIB npc={npcName} attacker_ghost={source} attacker_eid=0x{avatarEid:X} hp={health:F1} st={stamina:F1} result=kept-out(arranged-fight) damage=0 history=0 skirmish=0 brain_msg=0 reason={r.Reason}"));
            return false;
        }
        string steps = $"damage={((r.Steps & 1) != 0 ? 1 : 0)} history={((r.Steps & 2) != 0 ? 1 : 0)} skirmish={((r.Steps & 4) != 0 ? 1 : 0)}";
        bool brain = false;
        // WO-174: a scripted brawl's actor keeps its own fight -- the blow's damage stands (the DLL applied it), but the avatar is not engaged
        // against anyone and the actor gets no hit-reaction brain message about him (the DLL says WO174-BRAWL ... figure_kept_out=yes)
        bool scene = Wo174Rules.SceneOwned(npcName);
        if (scene) Interlocked.Increment(ref _w174SceneKept);
        if (!scene && r.Ok && (r.Steps & 1) != 0)
        {
            bool fresh = !_w121Engaged.ContainsKey(source);
            _w121Engaged[source] = DateTime.UtcNow + Wo121EngagementHold;
            if (fresh) await Wo121EngageAsync(source, true);
        }
        // WO-210 C.1: the engine's own ClientHit already sent the brain its hit signal for this blow -- never two
        bool engineSignalled = _w210EngineSignal.TryRemove(npcName, out long sigAt) && Environment.TickCount64 - sigAt < 3000;
        if (!scene && r.Ok && r.AttackerWuid != 0 && !engineSignalled)
        {
            // The brain's hit-reaction message, in the engine's own format
            // (WHGame CGameRules::SendAISignal: "attacker(%lld),hitStrength(%d),
            // hitType(%d),targetOrigMat(%d)") and the engine's own debug-command
            // shape. hitStrength/hitType/targetOrigMat: captured from a vanilla
            // sword hit (below).
            string dec = unchecked((long)r.AttackerWuid).ToString(CultureInfo.InvariantCulture);
            await ExecLuaAsync($"pcall(function() XGenAIModule.SendMessageToEntity(System.GetEntityByName(\"{npcName}\").this.id,\"hitReaction\",\"attacker({dec}),hitStrength({Wo121HitStrength}),hitType({Wo121HitType}),targetOrigMat({Wo121TargetMat})\") end)");
            brain = true;
        }
        Console.WriteLine(FormattableString.Invariant(
            $"MP-ATTRIB npc={npcName} attacker_ghost={source} attacker_eid=0x{avatarEid:X} hp={health:F1} st={stamina:F1} result={(r.Ok ? "applied" : "failed")} {steps} brain_msg={(brain ? 1 : 0)} reason={r.Reason}"));
        if (r.Ok && (r.Steps & 1) != 0) Wo139OnAvatarHit(source, npcName, health);   // WO-139: the joiner's assault (or murder) in this world, if witnessed
        if (r.Ok && (r.Steps & 1) != 0) Wo147WatchFight(npcName, $"avatar {source} hit it");   // WO-147: its fight with the avatar goes to the joiner
        return r.Ok && (r.Steps & 1) != 0;
    }

    // hitReaction values CAPTURED from one vanilla sword hit (WO-121 session 6,
    // the SendAISignal S_HitInfo: hitStrength +0x44 = 5, hitType +0x18 = 1,
    // targetOrigMat +0x34 = 5 on a cloth-wearing farmer). The brain-driven
    // event WO-119 saw is hitType 10; a real weapon hit is 1. The material is
    // the victim's and varies; 5 is the unarmoured case.
    private const int Wo121HitStrength = 5, Wo121HitType = 1, Wo121TargetMat = 5;
}
