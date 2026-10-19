// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-201 Act II: puppets take values, not guesses (docs/WO-201-findings.md).
//
//   B3 rider_from_host (WO-200's switch): the host sends a mount the moment its engine logs "Actor 'X' starts mounting horse 'H'" (and the
//      dismount at "dismounting horse"), besides the Lua's own IsMounted read; a mounted rider's row goes again every 10 s, so a joiner
//      who connected later and a row lost in a load still learn it.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, (string Horse, long SentMs)> _w201Mounts = new(StringComparer.Ordinal);
    private long _w201MountLines, _w201MountReasserts;

    private void Wo201ValuesOnEngineLine(string line)
    {
        if (!W137Host || !W200("rider_from_host")) return;
        if (!Wo201Rules.TryMountLine(line, out string actor, out string horse, out bool mounting)) return;
        Interlocked.Increment(ref _w201MountLines);
        string h = mounting ? horse : "";
        _w201Mounts[actor] = (h, Environment.TickCount64);
        _w200HostVals.AddOrUpdate(actor, (h, -1f, -1f), (_, v) => (h, v.St, v.Hp));
        Console.WriteLine($"WO201-RIDE host: the engine says {actor} {(mounting ? "mounts " + horse : "dismounts " + horse)} -> sent now");
        _ = Wo201SendMountAsync(actor, h);
    }

    private async Task Wo201SendMountAsync(string npc, string horse)
    {
        var peers = Wo134Peers();
        var row = new List<ExtraRow> { new ExtraRow { Name = npc, Target = horse } };
        foreach (byte g in peers) await Wo143SendAsync(g, Protocol.ExtraKindMount, row);
        if (peers.Count > 0) Interlocked.Increment(ref _w200MountOut);
    }

    /// <summary>B3, the host, 1 Hz: every mounted rider's row again each 10 s (the Lua's value or the engine's line, whichever is newer).</summary>
    private async Task Wo201MountReassertAsync()
    {
        if (!W137Host || !W200("rider_from_host")) return;
        long now = Environment.TickCount64;
        foreach (var kv in _w200HostVals)
        {
            if (kv.Value.Horse.Length == 0) continue;
            _w201Mounts.TryGetValue(kv.Key, out var m);
            if (now - m.SentMs < Wo201Rules.MountReassertMs) continue;
            _w201Mounts[kv.Key] = (kv.Value.Horse, now);
            Interlocked.Increment(ref _w201MountReasserts);
            await Wo201SendMountAsync(kv.Key, kv.Value.Horse);
        }
    }

    private void Wo201ValuesReset() => _w201Mounts.Clear();

    private string Wo201ValuesStats() => FormattableString.Invariant(
        $"mount_lines={Interlocked.Read(ref _w201MountLines)} mount_reasserts={Interlocked.Read(ref _w201MountReasserts)}");
}
