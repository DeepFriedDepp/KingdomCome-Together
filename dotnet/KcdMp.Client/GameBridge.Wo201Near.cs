// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;

namespace KcdMp.Client;

// WO-201 V.2 nearest_player_triggers (the host): a quest's distance trigger on "the player" measures from the nearer of the host's Henry
// and the partner's figure -- the escort fails only when both stray, "came near" fires when either does (the DLL's op 82,
// native/KCDMP/wo201_near.cpp). The agent tells the DLL which body is the figure, once a second, and (WO-204 I.3) whether a join or a
// load runs: the DLL then answers with the host's own Henry, exactly as without the mod. WO-204 Act I rebuilt the DLL's half
// (nearest_player_v2): the node is read, never written.
public partial class GameBridge
{
    private uint _w201NearSent = uint.MaxValue;
    private bool _w201NearQuietSent;
    private bool _w201NearRefused;

    private bool Wo201NearQuiet => Wo123HostJoinActive || _where == GameWhere.Loading;

    private void Wo201NearTick(long n)
    {
        if (!_w201Connected || n % 4 != 0 || _w201NearRefused) return;
        uint fig = 0;
        if (W137Host && W201("nearest_player_triggers"))
            foreach (var kv in _ghostEntityIds) { if (kv.Value != 0) { fig = kv.Value; break; } }
        var quiet = fig != 0 && Wo201NearQuiet;
        if (fig == _w201NearSent && quiet == _w201NearQuietSent) return;
        _w201NearSent = fig;
        _w201NearQuietSent = quiet;
        _ = Wo201NearSendAsync(fig, quiet);
    }

    private async Task Wo201NearSendAsync(uint fig, bool quiet = false)
    {
        var a = new byte[6];
        a[0] = (byte)(fig != 0 ? 1 : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(1), fig);
        a[5] = (byte)(quiet ? 1 : 0);
        var r = await _combat.Wo163Async(Protocol.Wo201OpNear, a);
        if (r is { Ok: true } rr && rr.Payload.Length >= 9)
            Console.WriteLine($"WO201-NEAR {(fig != 0 ? $"on: the figure 0x{fig:X} counts as the player where it is nearer{(quiet ? " (quiet: a join or a load)" : "")}" : "off")} (armed={rr.Payload[0]} overrides={BinaryPrimitives.ReadUInt32LittleEndian(rr.Payload.AsSpan(1))})");
        else
        {
            if (fig != 0) _w201NearRefused = true;   // not resolved here: one line, the triggers stay the host's Henry's this session
            Console.WriteLine($"WO201-NEAR refused by the DLL ({(r is { } x ? x.Reason : 255)}) -- the player's distance triggers stay the host's Henry's");
        }
    }

    private void Wo201NearReset() { if (_w201NearSent is not 0 and not uint.MaxValue) _ = Wo201NearSendAsync(0); _w201NearSent = uint.MaxValue; }
}
