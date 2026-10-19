// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-201 B4 copy_combat_from_host (joiner): combat is a value. The host's WO-132 NpcCombat events say, per NPC, whether it fights and
// whom (the host, a figure); its copy here fights exactly then. When the host's NPC leaves combat (no combat and no target for WO-183's
// 1.5 s), the copy's fight ends the engine's own way: every mod hold released and wh::rpgmodule::StopFight on its soul (the DLL's op 78).
// A copy never starts a fight by itself: its own combat shout at a figure while the host's NPC is not fighting ends it the same way
// (the field: Semine's copy shouted 16 attacks at the host's figure after the duel, and attacked him whenever he talked to Semine).
//   WO201-COMBAT end npc=<n> why=host-left-combat|local-start ... (agent)  /  WO201-COMBAT end npc=<n> eid=.. held=.. stop_fight=.. (DLL)
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, (bool Fights, NpcCombatTarget Target, long AtMs)> _w201HostFight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w201EndDue = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w201ShoutEndAt = new(StringComparer.Ordinal);
    private long _w201CombatEnds, _w201ShoutsRefused;

    /// <summary>Wo132OnNpcCombatInAsync: the host's word on one NPC.</summary>
    private void Wo201NoteHostCombat(NpcCombatEvent ev)
    {
        if (!W137Joiner) return;
        long now = Environment.TickCount64;
        bool f = Wo201Rules.HostFights(ev.InCombat, ev.Target != NpcCombatTarget.None);
        bool had = _w201HostFight.TryGetValue(ev.Name, out var prev);
        _w201HostFight[ev.Name] = (f, ev.Target, now);
        if (f) { _w201EndDue.TryRemove(ev.Name, out _); return; }
        if (had && prev.Fights) _w201EndDue[ev.Name] = now + Wo201Rules.CombatEndAfterMs;
    }

    /// <summary>The 4 Hz tick: a copy whose host NPC left combat (and stayed out for the debounce) has its fight ended.</summary>
    private void Wo201CombatTick()
    {
        if (!W137Joiner || !W201("copy_combat_from_host")) return;
        long now = Environment.TickCount64;
        foreach (var kv in _w201EndDue)
        {
            if (kv.Value > now) continue;
            _w201EndDue.TryRemove(kv.Key, out _);
            if (_w201HostFight.TryGetValue(kv.Key, out var w) && w.Fights) continue;
            _ = Wo201EndCopyCombatAsync(kv.Key, "host-left-combat");
        }
    }

    /// <summary>Wo170OnEngineLine: the engine's own combat shout of an NPC at a figure (before WO-170's 10 s rate limit).</summary>
    private void Wo201OnCopyShout(string npc)
    {
        if (!W137Joiner || !W201("copy_combat_from_host")) return;
        long now = Environment.TickCount64;
        bool have = _w201HostFight.TryGetValue(npc, out var w);
        if (!Wo201Rules.ShoutRefused(have, have && w.Fights, have ? now - w.AtMs : 0)) return;
        if (_w201ShoutEndAt.TryGetValue(npc, out long at) && now - at < Wo201Rules.LocalShoutEndEveryMs) return;
        _w201ShoutEndAt[npc] = now;
        Interlocked.Increment(ref _w201ShoutsRefused);
        _ = Wo201EndCopyCombatAsync(npc, have ? "local-start (the host's NPC is not fighting)" : "local-start (no word of a fight from the host)");
    }

    private async Task Wo201EndCopyCombatAsync(string npc, string why)
    {
        try
        {
            uint eid = _w131Guarded.TryGetValue(npc, out uint geid) ? geid : _npcEntityIds.TryGetValue(npc, out uint pe) ? pe : 0;
            if (_w132Engaged.TryRemove(npc, out _)) Interlocked.Increment(ref _w132EngageOff);
            // no id known here for that copy (its id event never came): the DLL finds the body of that name itself
            var nb = eid == 0 ? System.Text.Encoding.UTF8.GetBytes(npc) : Array.Empty<byte>();
            if (nb.Length > 63) { Console.WriteLine($"WO201-COMBAT end npc={npc} why={why} -- the name is too long"); return; }
            var a = new byte[eid == 0 ? 5 + nb.Length : 4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
            if (eid == 0) { a[4] = (byte)nb.Length; nb.CopyTo(a, 5); }
            var r = await _combat.Wo163Async(Protocol.Wo201OpCombatEnd, a);
            long n = Interlocked.Increment(ref _w201CombatEnds);
            string res = r is { Ok: true } rr && rr.Payload.Length >= 2
                ? $"held=0x{rr.Payload[0]:X2} stop_fight={(rr.Payload[1] == 1 ? "called" : "no")}"
                : $"refused ({(r is { } x ? x.Reason : 255)})";
            Console.WriteLine($"WO201-COMBAT end npc={npc} eid=0x{eid:X} why={why} -> {res} -- the host's world decides who fights [n={n}]");
        }
        catch (Exception ex) { Console.WriteLine($"WO201-COMBAT end npc={npc} failed ({ex.GetType().Name})"); }
    }

    private void Wo201CombatReset() { _w201HostFight.Clear(); _w201EndDue.Clear(); _w201ShoutEndAt.Clear(); }

    private string Wo201CombatStats() => FormattableString.Invariant(
        $"combat_ends={Interlocked.Read(ref _w201CombatEnds)} shouts_refused={Interlocked.Read(ref _w201ShoutsRefused)}");
}
