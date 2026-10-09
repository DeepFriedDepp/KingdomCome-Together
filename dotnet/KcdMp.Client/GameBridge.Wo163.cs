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
    private volatile string? _w163WatchNpc;                       // P1: a modelwatch is running on this NPC's copy
    private volatile System.Diagnostics.Stopwatch? _w163WatchClock;

    /// <summary>P1 (modelwatch): a swing row was played on the watched copy -- logged on the watch's own clock, between the model's changes.</summary>
    private void Wo163NoteRowForWatch(string npc, string spec)
    {
        if (_w163WatchNpc == npc && _w163WatchClock is { } c)
            Console.WriteLine(FormattableString.Invariant($"WO163-MODEL t={c.ElapsedMilliseconds}ms npc={npc} >>> a host row was played on the copy: {spec}"));
    }

    /// <summary>
    /// A7 (host): the Lua judge's question "is this NPC in a skirmish fight with the host?" (<c>w163_hostile &lt;id&gt; &lt;npc&gt;</c>) goes to the
    /// DLL's read-only relation call; the answer goes back to Lua (KCD2MP_W163HostileAnswer), which judges the assault as before when the
    /// answer is "not hostile" or "could not be made". The payload is an id and an authored name -- anything else is dropped.
    /// </summary>
    private void Wo163OnEvent(string name, string? arg)
    {
        if (name == "w163_probe") { _ = Wo163ProbeAsync(arg ?? ""); return; }
        if (name != "w163_hostile") return;
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 2 || !uint.TryParse(f[0], out uint id) || !CarryText.IsName(f[1])) return;
        _ = Wo163HostileAsync(id, f[1]);
    }

    /// <summary>
    /// Stage B's console surface (mp_w163_probe; maintainer present, one probe at a time -- the instruments, never the verdicts; every answer is
    /// a WO163-PROBE / WO163-MODEL line in this log):
    ///   status | model [npc|me] | relation &lt;npc&gt; | pair &lt;npc&gt; on|off [override] | swing2 &lt;npc&gt; &lt;gap_ms&gt; &lt;spec A&gt; | &lt;spec B&gt;
    ///   | engage &lt;npc&gt; on|off | modelwatch &lt;npc&gt; &lt;secs&gt; [every_ms=40]
    /// engage = the joiner's engagement of a bound copy against the local player (the DLL's own op, WO-132) without a join session; modelwatch
    /// reads the copy's combat model every few ms and logs only what CHANGED, with the rows that played on it in between (P1).
    /// pair joins / leaves the host's soul in the NPC's skirmish (P6); swing2 plays row A on the NPC's copy, then row B after the gap (P8).
    /// </summary>
    private async Task Wo163ProbeAsync(string line)
    {
        try
        {
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "status", rest = parts.Length > 1 ? parts[1] : "";
            switch (verb)
            {
                case "status":
                {
                    string? nat = await _combat.Wo163StatusAsync();
                    Console.WriteLine($"WO163-PROBE status native: {nat ?? "no answer"} | {Wo163StatsLine()}");
                    return;
                }
                case "model":
                {
                    string who = rest.Length == 0 ? "me" : rest;
                    if (who != "me" && !CarryText.IsName(who)) { Console.WriteLine("WO163-PROBE model: not an entity name"); return; }
                    string? t = await _combat.Wo163ModelReadAsync(who == "me" ? null : who);
                    Console.WriteLine($"WO163-MODEL npc={who} {t ?? "no answer"}");
                    return;
                }
                case "relation":
                {
                    if (!CarryText.IsName(rest)) { Console.WriteLine("WO163-PROBE relation: not an entity name"); return; }
                    var r = await _combat.Wo163SkirmishHostileAsync(rest);
                    Console.WriteLine($"WO163-PROBE relation host vs npc={rest}: " + (r is null ? "no answer" : r.Value.Answered ? (r.Value.Hostile ? "HOSTILE (one skirmish)" : "not hostile") : $"could not be asked (reason {_combat.Wo163LastReason}; the DLL's WO163-RELATION line says why)"));
                    return;
                }
                case "pair":
                {
                    var f = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length < 2 || !CarryText.IsName(f[0]) || f[1] is not ("on" or "off")) { Console.WriteLine("WO163-PROBE pair <npc> on|off [override]"); return; }
                    byte ovr = f.Length > 2 && byte.TryParse(f[2], out byte o) ? o : (byte)1;
                    bool? done = await _combat.Wo163PairAsync(f[0], f[1] == "on", ovr);
                    Console.WriteLine($"WO163-PROBE pair host {f[1]} npc={f[0]} override={ovr}: {(done is null ? "no answer" : done.Value ? "done" : "FAILED")}");
                    return;
                }
                case "swing2":
                {
                    int sp = rest.IndexOf(' ');
                    int sp2 = sp < 0 ? -1 : rest.IndexOf(' ', sp + 1);
                    string[] specs = sp2 < 0 ? [] : rest[(sp2 + 1)..].Split('|', 2, StringSplitOptions.TrimEntries);
                    if (sp < 0 || sp2 < 0 || specs.Length != 2 || !CarryText.IsName(rest[..sp]) || !int.TryParse(rest[(sp + 1)..sp2], out int gap) || gap is < 0 or > 5000)
                    { Console.WriteLine("WO163-PROBE swing2 <npc> <gap_ms 0-5000> <FragmentId, tags> | <FragmentId, tags>"); return; }
                    string npc = rest[..sp];
                    if (!_npcEntityIds.TryGetValue(npc, out uint neid)) { Console.WriteLine($"WO163-PROBE swing2: {npc} is not a copy here (a joiner's NPC puppet)"); return; }
                    _ = _combat.NpcHoldAsync(npc, (ushort)(1500 + gap), CancellationToken.None);
                    var a = await _combat.GhostSwingForResultAsync(neid, specs[0], CancellationToken.None);
                    await Task.Delay(gap);
                    var b = await _combat.GhostSwingForResultAsync(neid, specs[1], CancellationToken.None);
                    Console.WriteLine($"WO163-PROBE swing2 npc={npc} A=\"{specs[0]}\" -> {a.ReasonTag}; after {gap} ms B=\"{specs[1]}\" -> {b.ReasonTag}");
                    return;
                }
                case "engage":
                {
                    var f = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length != 2 || !CarryText.IsName(f[0]) || f[1] is not ("on" or "off")) { Console.WriteLine("WO163-PROBE engage <npc> on|off"); return; }
                    if (!_npcEntityIds.TryGetValue(f[0], out uint eid)) { Console.WriteLine($"WO163-PROBE engage: {f[0]} is not a copy here"); return; }
                    var st2 = new BodyState2(0, 0, BodyState2Bits.CombatMode | BodyState2Bits.Locked, Protocol.ZoneFromTableId(3), Protocol.StanceFromTableId(1), Protocol.ZoneFromTableId(3), 0, 0, 0);
                    var r = await _combat.Wo132EngageAsync(f[1] == "on", eid, st2, CancellationToken.None);
                    Console.WriteLine($"WO163-PROBE engage {f[0]} {f[1]}: ok={r.Ok} first={r.First} skirmish={r.Skirmish} dist={r.DistM:F1} m reason={r.Reason}");
                    return;
                }
                case "modelwatch":
                {
                    var f = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length < 2 || !CarryText.IsName(f[0]) || !double.TryParse(f[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double secs) || secs is <= 0 or > 120)
                    { Console.WriteLine("WO163-PROBE modelwatch <npc> <secs 0-120> [every_ms 20-500]"); return; }
                    int every = f.Length > 2 && int.TryParse(f[2], out int e2) ? Math.Clamp(e2, 20, 500) : 40;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _w163WatchNpc = f[0]; _w163WatchClock = sw;
                    string? last = null; int reads = 0, changes = 0;
                    while (sw.Elapsed.TotalSeconds < secs)
                    {
                        string? t = await _combat.Wo163ModelReadAsync(f[0]);
                        reads++;
                        if (t is not null && t != last) { changes++; last = t; Console.WriteLine(FormattableString.Invariant($"WO163-MODEL t={sw.ElapsedMilliseconds}ms npc={f[0]} {t}")); }
                        await Task.Delay(every);
                    }
                    _w163WatchNpc = null; _w163WatchClock = null;
                    Console.WriteLine($"WO163-PROBE modelwatch {f[0]} done: {reads} reads, {changes} changes");
                    return;
                }
                default:
                    Console.WriteLine("WO163-PROBE verbs: status | model [npc|me] | relation <npc> | pair <npc> on|off [override] | swing2 <npc> <gap_ms> <spec A> | <spec B> | engage <npc> on|off | modelwatch <npc> <secs> [every_ms]");
                    return;
            }
        }
        catch (Exception ex) { Console.WriteLine($"WO163-PROBE failed ({ex.GetType().Name}: {ex.Message})"); }
    }

    private string Wo163StatsLine() => FormattableString.Invariant(
        $"hostile_asked={Interlocked.Read(ref _w163HostileAsked)} hostile={Interlocked.Read(ref _w163HostileYes)} not_hostile={Interlocked.Read(ref _w163HostileNo)} unanswered={Interlocked.Read(ref _w163HostileUnanswered)} generic_swings={_w161Stats.GenericShown}");

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
