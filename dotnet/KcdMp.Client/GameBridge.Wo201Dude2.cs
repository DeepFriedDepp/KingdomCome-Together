// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;

namespace KcdMp.Client;

// WO-201 V.3 figure_is_player (the host only): Dude2. The partner's figure in the host's world carries the engine's own per-soul
// IsPlayer bit (WO-199 §7: the bit the game's player checks read), so the engine's own rules treat it as a player where they ask that
// bit -- never IsLocalPlayer, never the PlayerSoul slot, never a copy (the DLL refuses those; op 79).
//   * the save gate: the DLL's detour on the per-soul save step writes Dude2 with IsPlayer 0 on every save path; the bit is set again
//     right after. The first set arms it; no set without it.
//   * the watchdog: every second the DLL reads the PlayerSoul slot and Dude2's flags (op 80); the player's soul gone, or Dude2 with
//     IsLocalPlayer, trips it -- the bit is cleared and the part stays off for this session ("WO201-DUDE2 tripped ...").
//   * the save-decode check: after a load, the census (op 81) counts the souls the engine decoded with IsPlayer: the player's own only.
public partial class GameBridge
{
    private uint _w201Dude2Eid;
    private bool _w201Dude2Tripped;
    private long _w201Dude2Sets, _w201Dude2Checks;

    private void Wo201Dude2Tick(long n)
    {
        if (!_w201Connected) return;
        bool want = W137Host && W201("figure_is_player") && !_w201Dude2Tripped;
        if (!want)
        {
            if (_w201Dude2Eid != 0) _ = Wo201Dude2SetAsync(0, false, W137Host ? "the switch is off" : "not the host");
            return;
        }
        if (n % 4 == 2)
        {
            uint fig = 0;
            foreach (var kv in _ghostEntityIds) { if (kv.Value != 0) { fig = kv.Value; break; } }
            if (fig != 0 && fig != _w201Dude2Eid) _ = Wo201Dude2SetAsync(fig, true, "the partner's figure");
            else if (fig == 0 && _w201Dude2Eid != 0) _ = Wo201Dude2SetAsync(0, false, "the figure is gone");
        }
        if (n % 4 == 3 && _w201Dude2Eid != 0) _ = Wo201Dude2CheckAsync();   // the watchdog: every second
    }

    private async Task Wo201Dude2SetAsync(uint eid, bool on, string why)
    {
        var a = new byte[5];
        BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
        a[4] = (byte)(on ? 1 : 0);
        var r = await _combat.Wo163Async(Protocol.Wo201OpDude2, a);
        if (r is { Ok: true } rr && rr.Payload.Length >= 9)
        {
            bool ok = rr.Payload[0] == 1;
            uint before = BinaryPrimitives.ReadUInt32LittleEndian(rr.Payload.AsSpan(1)), after = BinaryPrimitives.ReadUInt32LittleEndian(rr.Payload.AsSpan(5));
            if (on && ok) { _w201Dude2Eid = eid; Interlocked.Increment(ref _w201Dude2Sets); }
            if (!on) _w201Dude2Eid = 0;
            if (on && !ok) _w201Dude2Tripped = true;   // refused (a copy, the player's own, a local player): never asked again this session
            Console.WriteLine($"WO201-DUDE2 {(on ? "set" : "clear")} eid=0x{eid:X} ({why}) -> {(ok ? "ok" : "REFUSED")} flags 0x{before:X} -> 0x{after:X}");
        }
        else
        {
            if (!on) _w201Dude2Eid = 0;
            else _w201Dude2Tripped = true;   // no resolver, no save gate: the bit is never set (one line, then off for the session)
            Console.WriteLine($"WO201-DUDE2 {(on ? "set" : "clear")} eid=0x{eid:X} ({why}) refused by the DLL ({(r is { } x ? x.Reason : 255)}) -- {(on ? "the figure stays a non-player this session" : "nothing held")}");
        }
    }

    private async Task Wo201Dude2CheckAsync()
    {
        var r = await _combat.Wo163Async(Protocol.Wo201OpDude2Check, Array.Empty<byte>());
        if (r is not { Ok: true } rr || rr.Payload.Length < 12) return;
        Interlocked.Increment(ref _w201Dude2Checks);
        bool armed = rr.Payload[0] == 1, psOk = rr.Payload[1] == 1, dPlayer = rr.Payload[2] == 1, dLocal = rr.Payload[3] == 1;
        if (!Wo201Rules.Dude2Trips(armed, psOk, dLocal, out string why))
        {
            if (!dPlayer) { Console.WriteLine($"WO201-DUDE2 check: the figure's soul no longer holds the bit (a load replaced it) -- set again"); _w201Dude2Eid = 0; }
            return;
        }
        _w201Dude2Tripped = true;
        Console.WriteLine($"WO201-DUDE2 tripped ({why}) -- the bit is cleared and the figure stays a non-player for this session");
        await Wo201Dude2SetAsync(0, false, "the watchdog");
    }

    private async Task Wo201Dude2CensusAsync()
    {
        var r = await _combat.Wo163Async(Protocol.Wo201OpDude2Census, Array.Empty<byte>());
        if (r is not { Ok: true } rr || rr.Payload.Length < 17) { Console.WriteLine($"WO201-DUDE2 census refused ({(r is { } x ? x.Reason : 255)})"); return; }
        uint souls = BinaryPrimitives.ReadUInt32LittleEndian(rr.Payload), pl = BinaryPrimitives.ReadUInt32LittleEndian(rr.Payload.AsSpan(4));
        uint lo = BinaryPrimitives.ReadUInt32LittleEndian(rr.Payload.AsSpan(8)), other = BinaryPrimitives.ReadUInt32LittleEndian(rr.Payload.AsSpan(13));
        bool psBit = rr.Payload[12] == 1;
        string verdict = Wo201Rules.CensusVerdict(pl, lo, psBit, _w201Dude2Eid != 0);
        Console.WriteLine($"WO201-DUDE2 census souls={souls} is_player={pl} is_local={lo} player_soul_bit={(psBit ? 1 : 0)} other=0x{other:X} dude2_held={(_w201Dude2Eid != 0 ? 1 : 0)} -> {verdict}");
    }

    private void Wo201Dude2Reset()
    {
        if (_w201Dude2Eid != 0) _ = Wo201Dude2SetAsync(0, false, "the session ended");   // the bit never outlives the session
    }

    private string Wo201Dude2Stats() =>
        $"dude2_eid=0x{_w201Dude2Eid:X} dude2_tripped={(_w201Dude2Tripped ? 1 : 0)} dude2_sets={Interlocked.Read(ref _w201Dude2Sets)} dude2_checks={Interlocked.Read(ref _w201Dude2Checks)}";
}
