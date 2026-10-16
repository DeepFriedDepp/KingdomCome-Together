// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-190: animation parity -- both players see the same thing. The agent's half (the rules are Wo190Rules in Wo190.cs; the native half
// wo190.cpp, with A6 in wo166.cpp and A3/A4 in motion.cpp).
//
// BOTH
//   * 0.1 / A9: the DLL's animation trace -- mp_anim_trace (every body, off) or mp_anim_scope_log auto (the default: the bodies in a fight);
//   * A1 (mp_anim_idle_watch): the DLL's idle watch; (mp_anim_standin_weapon) a reaction stand-in for the weapon the body really holds;
//   * A3 / A4 (mp_anim_requeue_gate, mp_anim_gait_hold): the DLL's gait rules; the switches go to the DLL every 10 s (op 51).
// JOINER
//   * A2 (mp_copy_weapon_hold): a fighting copy is told its host NPC's weapon at every engage (and, as before, every swing) -- never a light;
//   * A6 (mp_copy_swing_takeover): the DLL's swing takeover;
//   * A7 (mp_engage_hold): a copy whose host NPC turns to the host stays engaged 3 s;
//   * A8 (mp_sync_fallback): a paired row whose half the DLL's clinch gate refused, and that did not show (WO-187's move watcher), is played again
//     as its plain attack at once (the gate is asked only inside the move's sync distance; a refused row that shows anyway is kept, and the
//     copy's next paired rows try their own half again).
public partial class GameBridge
{
    private volatile bool _w190Trace = Wo190Rules.DefaultAnimTrace, _w190IdleWatch = Wo190Rules.DefaultIdleWatch,
                          _w190StandinWeapon = Wo190Rules.DefaultStandinWeapon, _w190CopyWeaponHold = Wo190Rules.DefaultCopyWeaponHold,
                          _w190RequeueGate = Wo190Rules.DefaultRequeueGate, _w190GaitHold = Wo190Rules.DefaultGaitHold,
                          _w190SwingTakeover = Wo190Rules.DefaultSwingTakeover, _w190EngageHold = Wo190Rules.DefaultEngageHold;
    private int _w190ScopeLog = Wo190Rules.DefaultScopeLog;
    private long _w190TickN, _w190HandsAsked, _w190HandsArmed, _w190HandsBare, _w190HandsUnknown, _w190StandinFiltered, _w190WeaponTold,
                 _w190EngageHolds, _w190EngageHoldReleases, _w190ClinchReported, _w190ClinchReplayed, _w190ClinchKept;
    // A7: when the copy's host NPC turned to the host (name -> ms), while it is held engaged
    private readonly ConcurrentDictionary<string, long> _w190HostTurn = new(StringComparer.Ordinal);
    // A2: when the copy was last told its weapon at an engage (name -> ms)
    private readonly ConcurrentDictionary<string, long> _w190WeaponAt = new(StringComparer.Ordinal);

    // ---------------------------------------------------------------- switches

    private void Wo190OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w190_cfg":
                if (!Wo190Rules.TryParseCfg(arg, out string cfg, out bool on)) { Console.WriteLine($"MP-W190 cfg refused: '{arg}'"); return; }
                switch (cfg)
                {
                    case "anim_trace": _w190Trace = on; break;
                    case "anim_idle_watch": _w190IdleWatch = on; break;
                    case "anim_standin_weapon": _w190StandinWeapon = on; break;
                    case "copy_weapon_hold": _w190CopyWeaponHold = on; break;
                    case "anim_requeue_gate": _w190RequeueGate = on; break;
                    case "anim_gait_hold": _w190GaitHold = on; break;
                    case "copy_swing_takeover": _w190SwingTakeover = on; break;
                    case "engage_hold": _w190EngageHold = on; break;
                }
                Console.WriteLine($"MP-W190 cfg {cfg}={(on ? "on" : "off")}");
                _ = Wo190PushConfigAsync();
                return;
            case "w190_status":
                _ = Wo190StatusAsync();
                return;
        }
    }

    /// <summary>A9: mp_anim_scope_log's word (WO-187's w187_cfg carries it): off / auto / on.</summary>
    private void Wo190ScopeLogWord(byte mode)
    {
        Interlocked.Exchange(ref _w190ScopeLog, mode);
        Console.WriteLine($"MP-W190 cfg anim_scope_log={Wo190Rules.ScopeLogName(mode)}");
        _ = Wo190PushConfigAsync();
    }

    private async Task Wo190PushConfigAsync()
    {
        try
        {
            byte B(bool v) => (byte)(v ? 1 : 0);
            byte mode = Wo190Rules.TraceMode(_w190Trace, (byte)Volatile.Read(ref _w190ScopeLog));
            var r = await _combat.Wo163Async(Protocol.Wo190OpConfig, [mode, B(_w190IdleWatch), B(_w190SwingTakeover), B(_w190RequeueGate), B(_w190GaitHold)]);
            if (r is { Ok: true, Payload.Length: >= 5 } x && (x.Payload[0] != mode || x.Payload[1] != B(_w190IdleWatch)))
                Console.WriteLine($"MP-W190 the DLL holds trace={x.Payload[0]} idle_watch={x.Payload[1]}");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W190 config not pushed ({ex.GetType().Name})"); }
    }

    private async Task Wo190StatusAsync()
    {
        string? n = null;
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo190OpStatus, []);
            if (r is { Ok: true } x) n = Encoding.ASCII.GetString(x.Payload);
        }
        catch { }
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W190-STATS anim_trace={On(_w190Trace)} anim_scope_log={Wo190Rules.ScopeLogName((byte)Volatile.Read(ref _w190ScopeLog))} anim_idle_watch={On(_w190IdleWatch)} anim_standin_weapon={On(_w190StandinWeapon)} copy_weapon_hold={On(_w190CopyWeaponHold)} anim_requeue_gate={On(_w190RequeueGate)} anim_gait_hold={On(_w190GaitHold)} copy_swing_takeover={On(_w190SwingTakeover)} engage_hold={On(_w190EngageHold)} hands_asked={Interlocked.Read(ref _w190HandsAsked)} hands_armed={Interlocked.Read(ref _w190HandsArmed)} hands_bare={Interlocked.Read(ref _w190HandsBare)} hands_unknown={Interlocked.Read(ref _w190HandsUnknown)} standin_filtered={Interlocked.Read(ref _w190StandinFiltered)} weapon_told={Interlocked.Read(ref _w190WeaponTold)} engage_holds={Interlocked.Read(ref _w190EngageHolds)} engage_hold_releases={Interlocked.Read(ref _w190EngageHoldReleases)} clinch_reported={Interlocked.Read(ref _w190ClinchReported)} clinch_replayed={Interlocked.Read(ref _w190ClinchReplayed)} clinch_kept={Interlocked.Read(ref _w190ClinchKept)} | native: {n ?? "no answer"}"));
    }

    /// <summary>Once a second (the WO-151 loop): the switches every 10 s, the stats every minute.</summary>
    private async Task Wo190TickAsync(CancellationToken ct)
    {
        long n = ++_w190TickN;
        if (n % 10 == 2) await Wo190PushConfigAsync();
        if (n % 60 == 30) await Wo190StatusAsync();
        long now = Environment.TickCount64;
        foreach (var (k, v) in _w190WeaponAt) if (now - v > 60_000) _w190WeaponAt.TryRemove(k, out _);
        _ = ct;
    }

    // ---------------------------------------------------------------- A1: what the body holds

    /// <summary>A1 (mp_anim_standin_weapon): the body's hands as the DLL reads them (op 52) -- (hands, the reaction rows' hand tags).</summary>
    private async Task<(byte Hands, string Tags)> Wo190HandsAsync(uint eid)
    {
        if (!_w190StandinWeapon || eid == 0) return (Wo190Rules.HandsUnknown, "");
        Interlocked.Increment(ref _w190HandsAsked);
        try
        {
            var a = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
            var r = await _combat.Wo163Async(Protocol.Wo190OpWeapon, a);
            if (r is { Ok: true, Payload.Length: >= 4 } x && x.Payload.Length >= 4 + x.Payload[3])
            {
                byte hands = x.Payload[2];
                string tags = Encoding.ASCII.GetString(x.Payload, 4, x.Payload[3]);
                if (hands == Wo190Rules.HandsArmed) Interlocked.Increment(ref _w190HandsArmed);
                else if (hands == Wo190Rules.HandsBare) Interlocked.Increment(ref _w190HandsBare);
                else Interlocked.Increment(ref _w190HandsUnknown);
                return (hands, tags);
            }
        }
        catch { }
        Interlocked.Increment(ref _w190HandsUnknown);
        return (Wo190Rules.HandsUnknown, "");
    }

    // ---------------------------------------------------------------- A2: the weapon at every engage

    /// <summary>A2 (mp_copy_weapon_hold, joiner): an engaged copy is told its host NPC's weapon when the host's hand rows named one (never a light)
    /// -- at most every 1.5 s per copy; the Lua puts a light it holds away and draws the weapon again.</summary>
    private void Wo190HoldWeapon(string npc, string why)
    {
        if (!_w190CopyWeaponHold || !_w167CopyWeapons) return;
        long now = Environment.TickCount64;
        if (_w190WeaponAt.TryGetValue(npc, out long at) && now - at < Wo190Rules.WeaponRefreshMs) return;
        _w190WeaponAt[npc] = now;
        string cls = Wo190Rules.WeaponToHold(_w167HostWeapon.TryGetValue(npc, out var w) ? w : null);
        // only a weapon the host's hand rows named: "?" would draw the copy's own weapon (live: a bare-handed villager drew one at its engage,
        // and its bare-handed clinch rows then died at ~45 ms); "-" is the rows' own business (Wo167RowWeapon holsters it)
        if (cls is "?" or "-") return;
        Interlocked.Increment(ref _w190WeaponTold);
        _ = ExecLuaAsync($"if KCD2MP_W167Weapon then KCD2MP_W167Weapon(\"{npc}\", \"{cls}\", \"{why}\") end");
    }

    // ---------------------------------------------------------------- A7: a copy turning between the two players

    /// <summary>A7 (mp_engage_hold, joiner): an engaged copy whose host NPC now fights the host -- true = keep it engaged (the caller refreshes the
    /// engagement); false = release it as before (the hold is off, or 3 s have passed since the turn).</summary>
    private bool Wo190HoldEngagement(string npc, NpcCombatEvent ev, bool engagedNow)
    {
        long now = Environment.TickCount64;
        if (ev.Target != NpcCombatTarget.Host) { _w190HostTurn.TryRemove(npc, out _); return false; }
        long turned = _w190HostTurn.GetOrAdd(npc, now);
        if (Wo190Rules.HoldEngaged(_w190EngageHold, engagedNow, ev.Target, turned, now))
        {
            if (turned == now)
            {
                long n = Interlocked.Increment(ref _w190EngageHolds);
                if (n <= 30 || n % 50 == 0)
                    Console.WriteLine($"MP-W190 engage hold {npc}: its host NPC turned to the host -- it stays engaged and animating {Wo190Rules.EngageHoldMs / 1000.0:F0} s, its opponent the host's figure (A7) [n={n}]");
            }
            return true;
        }
        if (_w190HostTurn.TryRemove(npc, out _) && _w190EngageHold) Interlocked.Increment(ref _w190EngageHoldReleases);
        return false;
    }

    /// <summary>A7: the host's NPC targets this player (or left the fight): its turn clock is gone.</summary>
    private void Wo190ForgetTurn(string npc) => _w190HostTurn.TryRemove(npc, out _);

    // ---------------------------------------------------------------- A8: the paired attack the copy cannot take its half of

    /// <summary>A8: after a paired row was played on a copy, the DLL's clinch refusals (op 53): a refusal of this copy since the row, and the row
    /// not shown (WO-187's move watcher) -> the copy plays the row's plain attack now.</summary>
    private async Task Wo190ClinchCheckAsync(string npc, uint eid, Guid rowGuid, ActionRowCatalog.Row sync, ActionRowCatalog catalog, CancellationToken ct)
    {
        try
        {
            await Task.Delay(Wo190Rules.ClinchLookMs, ct);
            var a = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(a, Wo190Rules.ClinchWithinMs);
            var r = await _combat.Wo163Async(Protocol.Wo190OpClinchRefused, a, ct);
            if (r is not { Ok: true, Payload.Length: >= 1 } x) return;
            int n = x.Payload[0];
            bool mine = false;
            for (int i = 0; i < n && 1 + 8 * i + 8 <= x.Payload.Length; i++)
                if (BinaryPrimitives.ReadUInt32LittleEndian(x.Payload.AsSpan(1 + 8 * i)) == eid
                    && BinaryPrimitives.ReadUInt32LittleEndian(x.Payload.AsSpan(5 + 8 * i)) <= Wo190Rules.ClinchLookMs + 200) { mine = true; break; }
            if (!mine) return;
            Interlocked.Increment(ref _w190ClinchReported);
            // shown anyway? (live, WO-190: inside the sync distance the gate refused every ask, yet the copy's own half played 1.3-2.7 s, while
            // its plain fallback rows were cut after ~45 ms) -- the plain row only for a sync row that did not show (WO-187's move watcher)
            byte shown = await Wo187MoveShownAsync(eid, ct);
            if (shown == Wo187Rules.MovePending) { await Task.Delay(Wo187Rules.MoveSecondLookMs - Wo190Rules.ClinchLookMs, ct); shown = await Wo187MoveShownAsync(eid, ct); }
            if (shown == Wo187Rules.MoveShown)
            {
                Interlocked.Increment(ref _w190ClinchKept);
                Console.WriteLine($"WO190-CLINCH npc={npc} row={rowGuid} the clinch gate refused its half, but its own row shows -- kept (no plain fallback)");
                return;
            }
            // only this row: the copy's next paired rows try their own half again (live: after a copy was put on the no-sync list its plain
            // rows were cut after ~45 ms three times of four, where its own paired rows had played 1.3-2.7 s)
            if (!_w167SyncFallback || catalog.PlainForSync(sync) is not { } plain) return;
            var pr = await _combat.GhostSwingForResultAsync(eid, plain.Row.Spec, ct);
            if (pr.Ok) { Interlocked.Increment(ref _w190ClinchReplayed); _ = Wo166StrikeAsync(npc, plain.Row, ct); }
            Console.WriteLine($"WO190-CLINCH npc={npc} row={rowGuid} the clinch gate refused its half and it did not show -> its plain attack {plain.Guid} played ({pr.ReasonTag})");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Console.WriteLine($"WO190-CLINCH npc={npc} check failed ({ex.GetType().Name})"); }
    }
}
