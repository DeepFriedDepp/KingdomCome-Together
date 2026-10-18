// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-197: the animation-sync release -- combat, the rest (WO-190 Part B with WO-196's answers). The rules are Wo197Rules (Wo197.cs); the
// DLL's half is native/KCDMP/wo197.cpp (the recoil and clinch polls, the paired play, StartSyncGuard on a pair, EnsureGuardAction).
//
//   B1 (mp_anim_recoil): this player's recoil (the DLL's poll of his director's scope 4) goes out as AvatarRecoil; a partner's plays on his
//      figure: the engine's own row when it came with one, else the failed-attack row for the weapon of the swing his figure last played.
//   B2 (mp_anim_paired): a paired row (combat_action_sync_attack) on a copy or a figure -- its victim's sync_hit row (Wo197Rules' mirror of
//      the attacker's tags, the victim's own weapon) plays on the victim first, both opponents set to each other, then the attacker's row.
//   B3 (mp_anim_clinch): this player's clinch (enter / move / leave) goes out as AvatarClinch; a partner's: the clinch guard through the engine's
//      StartSyncGuard on his figure and its opponent, each clinch move's two rows in one step.
//   B4 (mp_anim_knockout): the engine-knockout bit in this player's vitals; a partner's: his figure falls with the game's own unconsciousness
//      and stands up with its own stand-up when the bit clears (Lua KCD2MP_W197FigureKnockout).
//   B5 (mp_copy_guard_engine): the DLL's switch for A6's EnsureGuardAction.
//   Part C (mp_anim_seats, mp_anim_work, mp_anim_routines): a changed NPC activity of that kind applied at once (the DLL's mask).
public partial class GameBridge
{
    private volatile bool _w197Recoil = Wo197Rules.DefaultRecoil, _w197Paired = Wo197Rules.DefaultPaired, _w197Clinch = Wo197Rules.DefaultClinch,
                          _w197Knockout = Wo197Rules.DefaultKnockout, _w197GuardEngine = Wo197Rules.DefaultGuardEngine;
    private readonly ConcurrentDictionary<string, bool> _w197Cat = new(StringComparer.Ordinal);
    private long _w197TickN, _w197RecoilOut, _w197RecoilIn, _w197RecoilPlayed, _w197RecoilNoRow, _w197ClinchOut, _w197ClinchIn, _w197ClinchPlayed,
                 _w197PairAsked, _w197PairPlayed, _w197PairNoVictimRow, _w197PairNoVictim, _w197KnockDown, _w197KnockUp;
    private readonly ConcurrentDictionary<byte, byte> _w197PeerFlags = new();
    private readonly ConcurrentDictionary<string, byte> _w197FightFrom = new(StringComparer.Ordinal);   // copy -> the host's ghost that sent its NpcCombat

    private bool Wo197Cat(string name) => !_w197Cat.TryGetValue(name, out bool v) || v;   // every Part C switch ships on

    /// <summary>The DLL's Part C mask (op 66 byte 5): bit 0 doors, 1 seats, 2 items, 3 work, 4 riding, 5 routines, 6 emotes.</summary>
    private byte Wo197CategoryMask()
    {
        byte m = 0;
        string[] order = { "", "anim_seats", "", "anim_work", "", "anim_routines", "" };   // the DLL's bits; the others have no fix (always on)
        for (int i = 0; i < order.Length; i++) if (order[i].Length == 0 || Wo197Cat(order[i])) m |= (byte)(1 << i);
        return m;
    }

    // ---------------------------------------------------------------- switches

    private void Wo197OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w197_cfg":
                if (!Wo197Rules.TryParseCfg(arg, out string cfg, out bool on)) { Console.WriteLine($"MP-W197 cfg refused: '{arg}'"); return; }
                switch (cfg)
                {
                    case "anim_recoil": _w197Recoil = on; break;
                    case "anim_paired": _w197Paired = on; break;
                    case "anim_clinch": _w197Clinch = on; break;
                    case "anim_knockout": _w197Knockout = on; break;
                    case "copy_guard_engine": _w197GuardEngine = on; break;
                    default: _w197Cat[cfg] = on; break;
                }
                Console.WriteLine($"MP-W197 cfg {cfg}={(on ? "on" : "off")}");
                _ = Wo197PushConfigAsync();
                return;
            case "w197_status":
                _ = Wo197StatusAsync();
                return;
            case "w197_check":   // live checks: ensure <npc> | syncguard <type> <master> <slave> (names: a copy or kcd2mp_N)
                _ = Wo197CheckAsync(arg ?? "");
                return;
        }
    }

    private async Task Wo197PushConfigAsync()
    {
        try
        {
            byte B(bool v) => (byte)(v ? 1 : 0);
            await _combat.Wo163Async(Protocol.Wo197OpConfig, [B(_w197Recoil), B(_w197Paired), B(_w197Clinch), B(_w197GuardEngine), Wo197CategoryMask()]);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W197 config not pushed ({ex.GetType().Name})"); }
    }

    private async Task Wo197StatusAsync()
    {
        string? n = null;
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo197OpStatus, []);
            if (r is { Ok: true } x) n = Encoding.ASCII.GetString(x.Payload);
        }
        catch { }
        var cats = string.Join(' ', Wo197Rules.SwitchNames.Skip(5).Select(c => $"{c}={On(Wo197Cat(c))}"));
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W197-STATS anim_recoil={On(_w197Recoil)} anim_paired={On(_w197Paired)} anim_clinch={On(_w197Clinch)} anim_knockout={On(_w197Knockout)} copy_guard_engine={On(_w197GuardEngine)} {cats} recoil_out={Interlocked.Read(ref _w197RecoilOut)} recoil_in={Interlocked.Read(ref _w197RecoilIn)} recoil_played={Interlocked.Read(ref _w197RecoilPlayed)} recoil_no_row={Interlocked.Read(ref _w197RecoilNoRow)} clinch_out={Interlocked.Read(ref _w197ClinchOut)} clinch_in={Interlocked.Read(ref _w197ClinchIn)} clinch_played={Interlocked.Read(ref _w197ClinchPlayed)} pair_asked={Interlocked.Read(ref _w197PairAsked)} pair_played={Interlocked.Read(ref _w197PairPlayed)} pair_no_victim_row={Interlocked.Read(ref _w197PairNoVictimRow)} pair_no_victim={Interlocked.Read(ref _w197PairNoVictim)} knock_down={Interlocked.Read(ref _w197KnockDown)} knock_up={Interlocked.Read(ref _w197KnockUp)} | native: {n ?? "no answer"}"));
    }

    /// <summary>Once a second (the WO-151 loop): the switches every 10 s, the stats every minute.</summary>
    private async Task Wo197TickAsync(CancellationToken ct)
    {
        long n = ++_w197TickN;
        if (n % 10 == 3) await Wo197PushConfigAsync();
        if (n % 60 == 31) await Wo197StatusAsync();
        _ = ct;
    }

    private uint Wo197Eid(string name) =>
        _npcEntityIds.TryGetValue(name, out uint e) ? e : name.StartsWith("kcd2mp_", StringComparison.Ordinal) && _ghostEntityIds.TryGetValue(name[7..], out uint g) ? g : 0;

    private async Task Wo197CheckAsync(string arg)
    {
        var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        try
        {
            if (p.Length == 2 && p[0] == "ensure" && Wo197Eid(p[1]) is uint e and not 0)
            {
                var a = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(a, e);
                var r = await _combat.Wo163Async(Protocol.Wo197OpEnsureGuard, a);
                Console.WriteLine($"MP-W197 check ensure {p[1]} -> {(r is { Ok: true } x ? $"result={x.Payload[0]} before={Encoding.ASCII.GetString(x.Payload, 1, x.Payload.Length - 1)}" : $"refused ({r?.Reason})")}");
            }
            else if (p.Length == 4 && p[0] == "syncguard" && int.TryParse(p[1], out int type) && Wo197Eid(p[2]) is uint m and not 0 && Wo197Eid(p[3]) is uint sl and not 0)
            {
                var a = new byte[12];
                BinaryPrimitives.WriteInt32LittleEndian(a, type); BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(4), m); BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(8), sl);
                var r = await _combat.Wo163Async(Protocol.Wo197OpSyncGuard, a);
                Console.WriteLine($"MP-W197 check syncguard {type} {p[2]} {p[3]} -> {(r is { Ok: true } x ? $"result={x.Payload[0]} master_flag={x.Payload[1]} slave_flag={x.Payload[2]}" : $"refused ({r?.Reason})")}");
            }
            else Console.WriteLine($"MP-W197 check: ensure <npc> | syncguard <type> <master> <slave> (got '{arg}')");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W197 check failed ({ex.GetType().Name})"); }
    }

    // ---------------------------------------------------------------- B1 / B3: out

    /// <summary>The DLL's poll found this player's recoil or clinch (a LocalAction frame, eid 0); null = not one of WO-197's kinds.</summary>
    private byte[]? Wo197OutboundPacket(LocalActionFrame f, uint ms, out string what)
    {
        what = "";
        switch ((ActionKind)f.Kind)
        {
            case ActionKind.AvatarRecoil:
                if (!_w197Recoil) return null;
                Interlocked.Increment(ref _w197RecoilOut);
                what = $"recoil={Wo197Rules.RecoilKindName(f.Flags)} row={(f.Row == Guid.Empty ? "-" : f.Row.ToString())}";
                if (f.Row != Guid.Empty && _rowCatalog.IsCompletedSuccessfully && _rowCatalog.Result is { } rc)
                    what += rc.TryGet(f.Row, out var rk) ? $" table={rk.Table} spec=\"{rk.Spec}\"" : " table=UNKNOWN";
                byte rflags = f.Flags;
                if ((rflags & Wo197Rules.RecoilRow) != 0 && !(_rowCatalog.IsCompletedSuccessfully && _rowCatalog.Result is { } rc2 && rc2.TryGet(f.Row, out _)))
                    rflags = (byte)(rflags & ~Wo197Rules.RecoilRow);   // a GUID the tables do not know is never sent as a row
                return _actionOut.Build(ActionKind.AvatarRecoil, ActionPhase.Commit,
                    new RowEvent(ms, rflags, (rflags & Wo197Rules.RecoilRow) != 0 ? f.Row : Guid.Empty, "").ToBytes());
            case ActionKind.AvatarClinch:
                if (!_w197Clinch) return null;
                Interlocked.Increment(ref _w197ClinchOut);
                byte cflags = f.Flags;
                Guid master = f.Row, slave = f.AltRow;
                if ((cflags & Wo197Rules.ClinchRows) != 0 && !(_rowCatalog.IsCompletedSuccessfully && _rowCatalog.Result is { } cc && cc.TryGet(master, out _)))
                { cflags = (byte)(cflags & ~Wo197Rules.ClinchRows); master = Guid.Empty; slave = Guid.Empty; }
                what = $"clinch={Wo197Rules.ClinchPhaseName(cflags)} role={((cflags & Wo197Rules.ClinchMaster) != 0 ? "master" : "slave")} row={(master == Guid.Empty ? "-" : master.ToString())} slave_row={(slave == Guid.Empty ? "-" : slave.ToString())}";
                return _actionOut.Build(ActionKind.AvatarClinch, ActionPhase.Commit, Wo197Rules.ClinchEvent(ms, cflags, master, slave).ToBytes());
            default:
                return null;
        }
    }

    // ---------------------------------------------------------------- B1: in

    private async Task Wo197OnRecoilInAsync(InboundAction a, ActionRowCatalog? catalog, CancellationToken ct)
    {
        if (!RowEvent.TryFromBytes(a.Payload, out var re)) return;
        Interlocked.Increment(ref _w197RecoilIn);
        string gid = a.SourceGhostId.ToString(CultureInfo.InvariantCulture);
        string head = $"MP-ACTION section=inbound ghost={gid} kind=AvatarRecoil recoil={Wo197Rules.RecoilKindName(re.Flags)}";
        if (!_w197Recoil) { Console.WriteLine($"{head} dispatch=dropped-toggle-off"); return; }
        if (catalog is null || !_ghostEntityIds.TryGetValue(gid, out uint feid)) { Console.WriteLine($"{head} dispatch=dropped-no-figure"); return; }
        ActionRowCatalog.Row row;
        string how;
        if ((re.Flags & Wo197Rules.RecoilRow) != 0 && catalog.TryGet(re.Row, out var own) && own.Table == "combat_action_failed_attack") { row = own; how = "his engine's row"; }
        else
        {
            // the swing his figure last played (WO-173's ledger), its weapon's failed-attack row (WO-165's picker)
            if (!_w173LastRow.TryGetValue(gid, out var last) || Environment.TickCount64 - last.At > Wo197Rules.RecoilSwingFreshMs)
            { Interlocked.Increment(ref _w197RecoilNoRow); Console.WriteLine($"{head} dispatch=skipped (no swing of his on his figure in the last {Wo197Rules.RecoilSwingFreshMs / 1000} s)"); return; }
            var fr = catalog.FailedAttackRow(last.Row, Wo197Rules.RecoilIsPerfect(re.Flags));
            if (fr is null) { Interlocked.Increment(ref _w197RecoilNoRow); Console.WriteLine($"{head} swing=\"{last.Row.Spec}\" dispatch=none (no recoil row for this weapon: blunt and unarmed blows run on)"); return; }
            row = fr.Value; how = "picked for his swing's weapon";
        }
        _ = _combat.NpcHoldAsync("kcd2mp_" + gid, 900, ct);
        var r = await _combat.GhostSwingForResultAsync(feid, row.Spec, ct);
        if (r.Ok) Interlocked.Increment(ref _w197RecoilPlayed);
        Console.WriteLine($"{head} spec=\"{row.Spec}\" ({how}) dispatch=native-row result={r.ReasonTag}");
    }

    // ---------------------------------------------------------------- B2: a paired row's victim half

    /// <summary>
    /// Before a paired attacker row plays on <paramref name="attackerEid"/> (a copy or a figure): its opponent (the DLL reads it; never the
    /// local player) gets the matching sync_hit row first, both opponents set to each other. False = nothing played (the caller's row plays as
    /// before; the 23 paired rows with no victim row keep WO-167 / WO-190's plain fallback).
    /// </summary>
    private async Task<bool> Wo197VictimHalfAsync(string who, uint attackerEid, ActionRowCatalog.Row attack, ActionRowCatalog catalog, CancellationToken ct)
    {
        if (!_w197Paired || attack.Table != "combat_action_sync_attack" || attackerEid == 0) return false;
        Interlocked.Increment(ref _w197PairAsked);
        // 1. the victim: the attacker's opponent (op 67 with no rows = the query); a figure's: the copy fighting him here when his body names none
        var q = await Wo197PairOpAsync(attackerEid, 0, 0, "", "", ct);
        if (q is not { Ok: true } && Wo197FigureGhost(who) is byte fg && Wo197CopyFighting(fg) is uint cf) q = await Wo197PairOpAsync(attackerEid, cf, 0, "", "", ct);
        if (q is not { Ok: true, Payload.Length: >= 4 } qa) { Interlocked.Increment(ref _w197PairNoVictim); Console.WriteLine($"MP-W197 pair attacker={who} row=\"{attack.Spec}\" -> no victim (the attacker's opponent is not a copy or a figure here)"); return false; }
        uint victim = BinaryPrimitives.ReadUInt32LittleEndian(qa.Payload);
        // 2. the victim's own weapon (the reach's weapon lookup, op 52) and its row
        var (hands, handTags) = await Wo190HandsAsync(victim);
        IReadOnlyList<string>? weapon = hands is Wo190Rules.HandsArmed or Wo190Rules.HandsBare && handTags.Length > 0 ? handTags.Split('+', StringSplitOptions.RemoveEmptyEntries) : null;
        var vr = catalog.VictimRow(attack, weapon, Wo197Rules.ManClass);
        if (vr is null) { Interlocked.Increment(ref _w197PairNoVictimRow); Console.WriteLine($"MP-W197 pair attacker={who} row=\"{attack.Spec}\" -> no victim row in the game's tables (the plain fallback stays)"); return false; }
        // 3. the victim's row now (the attacker's follows at once through the caller's own path)
        var r = await Wo197PairOpAsync(attackerEid, victim, Wo197PairVictimFirst, "", vr.Value.Row.Spec, ct);
        bool ok = r is { Ok: true, Payload.Length: >= 7 } p && p.Payload[4] == 0;
        if (ok) Interlocked.Increment(ref _w197PairPlayed);
        Console.WriteLine($"MP-W197 pair attacker={who} row=\"{attack.Spec}\" victim=0x{victim:X} victim_row=\"{vr.Value.Row.Spec}\" -> {(r is null ? "no answer" : r.Value.Ok ? (ok ? "played" : $"refused (swing result {r.Value.Payload[4]})") : $"refused (reason {r.Value.Reason})")} targets={(r is { Ok: true, Payload.Length: >= 7 } t ? t.Payload[6] : 0)}");
        return ok;
    }

    private const byte Wo197PairVictimFirst = 0x01;

    private static byte? Wo197FigureGhost(string who) =>
        who.StartsWith("kcd2mp_", StringComparison.Ordinal) && byte.TryParse(who.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out byte g) ? g : null;

    /// <summary>The copy (its entity id) whose host NPC fights this partner (the host's NpcCombat: the host himself, or his avatar), the freshest
    /// of the last 5 s; null = none.</summary>
    private uint? Wo197CopyFighting(byte ghost)
    {
        byte hostGhost = _leashHostId != 0xFF ? _leashHostId : _hostModeFrom;
        long now = Environment.TickCount64, best = -1;
        uint? eid = null;
        foreach (var (name, f) in _w187Fights)
        {
            if (now - f.AtMs > 5000 || f.Eid == 0) continue;
            byte from = _w197FightFrom.TryGetValue(name, out byte fg) ? fg : hostGhost;
            bool his = (f.Target == NpcCombatTarget.Avatar && f.Ghost == ghost) || (f.Target == NpcCombatTarget.Host && (ghost == hostGhost || ghost == from));
            if (his && f.AtMs > best) { best = f.AtMs; eid = f.Eid; }
        }
        return eid;
    }

    private async Task<(bool Ok, byte Reason, byte[] Payload)?> Wo197PairOpAsync(uint attacker, uint victim, byte flags, string specA, string specV, CancellationToken ct)
    {
        var a = Encoding.ASCII.GetBytes(specA);
        var v = Encoding.ASCII.GetBytes(specV);
        if (a.Length > 191 || v.Length > 191) return null;   // the DLL's spec cap (combat_swing.h)
        var b = new byte[4 + 4 + 1 + 1 + a.Length + 1 + v.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(b, attacker);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), victim);
        b[8] = flags;
        b[9] = (byte)a.Length; a.CopyTo(b, 10);
        b[10 + a.Length] = (byte)v.Length; v.CopyTo(b, 11 + a.Length);
        try { return await _combat.Wo163Async(Protocol.Wo197OpPair, b, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    // ---------------------------------------------------------------- B3: in

    private async Task Wo197OnClinchInAsync(InboundAction a, ActionRowCatalog? catalog, CancellationToken ct)
    {
        if (!RowEvent.TryFromBytes(a.Payload, out var re)) return;
        Interlocked.Increment(ref _w197ClinchIn);
        string gid = a.SourceGhostId.ToString(CultureInfo.InvariantCulture);
        bool master = (re.Flags & Wo197Rules.ClinchMaster) != 0;
        byte phase = (byte)(re.Flags & Wo197Rules.ClinchPhaseMask);
        string head = $"MP-ACTION section=inbound ghost={gid} kind=AvatarClinch clinch={Wo197Rules.ClinchPhaseName(re.Flags)} role={(master ? "master" : "slave")}";
        if (!_w197Clinch) { Console.WriteLine($"{head} dispatch=dropped-toggle-off"); return; }
        if (catalog is null || !_ghostEntityIds.TryGetValue(gid, out uint feid)) { Console.WriteLine($"{head} dispatch=dropped-no-figure"); return; }
        // his figure's opponent here (the DLL's read; a copy -- never this player, whose body is his own input's)
        var q = await Wo197PairOpAsync(feid, 0, 0, "", "", ct);
        if (q is not { Ok: true } && Wo197CopyFighting(a.SourceGhostId) is uint cf) q = await Wo197PairOpAsync(feid, cf, 0, "", "", ct);
        if (q is not { Ok: true, Payload.Length: >= 4 } qa) { Console.WriteLine($"{head} dispatch=skipped (his figure has no opponent body here)"); return; }
        uint opp = BinaryPrimitives.ReadUInt32LittleEndian(qa.Payload);
        uint mEid = master ? feid : opp, sEid = master ? opp : feid;
        if (phase == Wo197Rules.ClinchEnter)
        {
            var (hands, _) = await Wo190HandsAsync(feid);
            int type = Wo197Rules.ClinchGuardType(-1, hands is Wo190Rules.HandsBare or Wo190Rules.HandsUnknown);
            var args = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(args, type);
            BinaryPrimitives.WriteUInt32LittleEndian(args.AsSpan(4), mEid);
            BinaryPrimitives.WriteUInt32LittleEndian(args.AsSpan(8), sEid);
            var r = await _combat.Wo163Async(Protocol.Wo197OpSyncGuard, args, ct);
            bool ok = r is { Ok: true, Payload.Length: >= 1 } x && x.Payload[0] == 1;
            if (ok) Interlocked.Increment(ref _w197ClinchPlayed);
            Console.WriteLine($"{head} master=0x{mEid:X} slave=0x{sEid:X} dispatch=StartSyncGuard type={type} result={(r is null ? "no answer" : ok ? "started" : $"refused ({(r.Value.Ok && r.Value.Payload.Length > 0 ? r.Value.Payload[0] : r.Value.Reason)})")}");
            if (ok) return;
            // WO-200 II.6: the engine refuses StartSyncGuard on two puppets (WO-197 b3clinch). WO-197 then played its own clinch-guard rows
            // (entry, then the loop); with no engine sync to end them they held the figure -- the field's second bout: every later swing of
            // the host's queued "ok" and none started (17 of 17). Live (c6, armed): rows -> 0 of 4 swings, no rows -> 4 of 4. No rows now: the
            // figure keeps fighting; the engine's own guard is asserted again at the clinch's end or its next swing.
            Console.WriteLine($"{head} dispatch=none (StartSyncGuard refused: no clinch rows -- they would hold the figure; it keeps fighting)");
            _w200ClinchRows[feid] = (opp, Environment.TickCount64);   // WO-200 II.6: no sync link holds this clinch -- its end is ours
            return;
        }
        if (phase != Wo197Rules.ClinchMove)
        {
            // WO-200 II.6: a clinch entered by rows (StartSyncGuard refused) has no engine sync to end it, and the figure's next moves cannot
            // start inside its clinch guard (the field: 17 of the host's swings queued "ok" after one, none started) -- the engine's own guard ends it
            if (_w200ClinchRows.TryRemove(feid, out var cr)) { await Wo200EndClinchAsync(feid, cr.Opp, "leave", ct); return; }
            Console.WriteLine($"{head} dispatch=none (the clinch ends with the bodies' next moves)");
            return;
        }
        if ((re.Flags & Wo197Rules.ClinchRows) == 0 || !catalog.TryGet(re.Row, out var mrow) || !catalog.TryGet(Wo197Rules.ClinchSlaveRow(re), out var srow))
        { Console.WriteLine($"{head} dispatch=skipped (the move came without both rows)"); return; }
        var pr = await Wo197PairOpAsync(mEid, sEid, Wo197PairVictimFirst, mrow.Spec, srow.Spec, ct);
        bool both = pr is { Ok: true, Payload.Length: >= 7 } pp && pp.Payload[4] == 0 && pp.Payload[5] == 0;
        if (both) Interlocked.Increment(ref _w197ClinchPlayed);
        Console.WriteLine($"{head} master_row=\"{mrow.Spec}\" slave_row=\"{srow.Spec}\" dispatch=pair result={(pr is null ? "no answer" : pr.Value.Ok ? $"slave={pr.Value.Payload[4]} master={pr.Value.Payload[5]} targets={pr.Value.Payload[6]}" : $"refused ({pr.Value.Reason})")}");
    }

    // ---------------------------------------------------------------- B4

    /// <summary>The sender's vitals flag (SendPlayerStateIfChangedAsync).</summary>
    private byte Wo197KnockoutFlag(bool? isUnconscious) =>
        _w197Knockout && Wo197Rules.EngineKnockout(isUnconscious, _localDowned) ? Protocol.PlayerStateFlagEngineKnockout : (byte)0;

    /// <summary>Every inbound vitals sample (Wo132OnPeerVitals): the engine-knockout bit's edges.</summary>
    private void Wo197OnPeerVitals(byte ghost, byte flags)
    {
        byte was = _w197PeerFlags.TryGetValue(ghost, out byte f0) ? f0 : (byte)0;
        _w197PeerFlags[ghost] = flags;
        var edge = Wo197Rules.KnockoutEdge(was, flags);
        if (edge == Wo197Rules.KnockEdge.None) return;
        if (!_w197Knockout) { Console.WriteLine($"MP-W197 peer {ghost} knockout {(edge == Wo197Rules.KnockEdge.Down ? "down" : "up")} -- mp_anim_knockout off: his figure stays as it is"); return; }
        bool down = edge == Wo197Rules.KnockEdge.Down;
        if (down) Interlocked.Increment(ref _w197KnockDown); else Interlocked.Increment(ref _w197KnockUp);
        Console.WriteLine($"MP-W197 peer {ghost} {(down ? "KNOCKED OUT in his world -> his figure falls with the game's own unconsciousness" : "woke -> his figure stands up the game's way")}");
        _ = ExecLuaAsync($"if KCD2MP_W197FigureKnockout then KCD2MP_W197FigureKnockout(\"{ghost}\", {(down ? "true" : "false")}) end");
    }
}
