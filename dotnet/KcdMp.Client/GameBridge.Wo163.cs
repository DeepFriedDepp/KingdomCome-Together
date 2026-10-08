// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-163 (docs/WO-163-findings.md): shared combat, built on the WO-162 contracts (research/WO-162/combat-RE.md).
//
// This file is the agent's half of Stage A's capture fixes; the pairing rule and the generic swing live with the verdict
// path (Wo161.cs holds the pure rules, GameBridge.Wo161.cs the bridge).
//
//   A1  a sync attack's row GUID is read by the DLL at descriptor +0x7C (WO-162 Q3.1: 16 of 16 field dumps); the frame may
//       carry what the legacy offset (+0x84) holds as a 16-byte tail, and BOTH hitting the game's tables is logged -- it
//       must never happen (WO163-SYNCGUID).
//   A2  a perfect-block-class row of an NPC reaches the DLL's hooked EnterImpl (flags bit 0); only a master strike (the
//       blocker's counter: action types 55 / 62 of combat_action_perfect_block) is a swing, sent as an NpcAttack row. The
//       others (the perfect block itself, its sync half) are the victim's half of an exchange: dropped, counted.
public partial class GameBridge
{
    private long _w163SyncOut, _w163SyncUnknown, _w163MasterOut, _w163PerfectDropped, _w163BothOffsets;
    private long _w163HostileAsked, _w163HostileYes, _w163HostileNo, _w163HostileUnanswered;

    /// <summary>
    /// A7 (host): the Lua judge's question "is this NPC in a skirmish fight with the host?" (<c>w163_hostile &lt;id&gt; &lt;npc&gt;</c>) goes to the
    /// DLL's read-only relation call; the answer goes back to Lua (KCD2MP_W163HostileAnswer), which judges the assault as before when the
    /// answer is "not hostile" or "could not be made". The payload is an id and an authored name -- anything else is dropped.
    /// </summary>
    private void Wo163OnEvent(string name, string? arg)
    {
        if (name != "w163_hostile") return;
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 2 || !uint.TryParse(f[0], out uint id) || !CarryText.IsName(f[1])) return;
        _ = Wo163HostileAsync(id, f[1]);
    }

    private async Task Wo163HostileAsync(uint id, string npc)
    {
        Interlocked.Increment(ref _w163HostileAsked);
        (bool Answered, bool Hostile)? r = null;
        try { r = await _combat.Wo163SkirmishHostileAsync(npc); }
        catch (Exception ex) { Console.WriteLine($"MP-WO163 hostile? npc={npc} failed ({ex.GetType().Name}: {ex.Message}) -- judged as before"); }
        bool answered = r is { Answered: true }, hostile = r is { Answered: true, Hostile: true };
        Interlocked.Increment(ref answered ? ref (hostile ? ref _w163HostileYes : ref _w163HostileNo) : ref _w163HostileUnanswered);
        Console.WriteLine($"MP-WO163 hostile? id={id} npc={npc} -> {(answered ? (hostile ? "hostile opponents in one skirmish" : "not hostile") : "no answer (the call could not be made)")}");
        await ExecLuaAsync($"if KCD2MP_W163HostileAnswer then KCD2MP_W163HostileAnswer({id}, {B(answered)}, {B(hostile)}) end");
    }

    /// <summary>Host: may this NPC row be sent as a swing? (A1's check, A2's filter.) False = drop it.</summary>
    private bool Wo163OutboundRowOk(LocalActionFrame f)
    {
        ActionRowCatalog? cat = _rowCatalog.IsCompletedSuccessfully ? _rowCatalog.Result : null;
        bool known = cat is not null && cat.TryGet(f.Row, out _);
        ActionRowCatalog.Row row = default;
        if (cat is not null) cat.TryGet(f.Row, out row);
        bool sync = known && row.Table == "combat_action_sync_attack";
        if (sync) Interlocked.Increment(ref _w163SyncOut);
        if (f.AltRow != Guid.Empty)
        {
            if (cat is not null && cat.BothResolve(f.Row, f.AltRow))
            {
                // never expected: the two reads name two different rows. The +0x7C one is used; this line is the alarm.
                if (Interlocked.Increment(ref _w163BothOffsets) <= 5)
                    Console.WriteLine($"WO163-SYNCGUID both descriptor offsets hit the catalog (+0x7C row={f.Row}, legacy +0x84 row={f.AltRow}) -- the +0x7C read is used; a build whose layout moved?");
            }
        }
        if ((f.Flags & 0x01) != 0)
        {
            // A2: a perfect-block-class action of an NPC. Only the counter is a swing.
            if (known && ActionRowCatalog.IsMasterStrike(row.ActionType))
            {
                Interlocked.Increment(ref _w163MasterOut);
                return true;
            }
            Interlocked.Increment(ref _w163PerfectDropped);
            return false;
        }
        if (!known && f.Row != Guid.Empty && cat is not null) Interlocked.Increment(ref _w163SyncUnknown);   // a row of no table at all: the joiner would drop it too
        return true;
    }

    private void Wo163WriteStats()
    {
        long sync = Interlocked.Read(ref _w163SyncOut), unk = Interlocked.Read(ref _w163SyncUnknown), mas = Interlocked.Read(ref _w163MasterOut),
             drop = Interlocked.Read(ref _w163PerfectDropped), both = Interlocked.Read(ref _w163BothOffsets);
        long ask = Interlocked.Read(ref _w163HostileAsked), yes = Interlocked.Read(ref _w163HostileYes), no = Interlocked.Read(ref _w163HostileNo), un = Interlocked.Read(ref _w163HostileUnanswered);
        if (sync == 0 && unk == 0 && mas == 0 && drop == 0 && both == 0 && ask == 0) return;
        Console.WriteLine(FormattableString.Invariant(
            $"MP-WO163-STATS rows_out_sync={sync} rows_out_unknown_table={unk} master_strikes_out={mas} perfect_block_rows_dropped={drop} sync_both_offsets={both} hostile_asked={ask} hostile={yes} not_hostile={no} unanswered={un}"));
    }
}
