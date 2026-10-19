// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-201 B11 figure_ranged, the moves: this player's crossbow / bow steps (the DLL's op 86: charging, aiming, firing, reloading,
// uncharging, leaving) go to the partner (W164 RangedStep, kind 25), whose screen plays the game's own fragment for it on this player's
// figure (WO-143's avatar one-shot): Fire for a shot, Load for a reload, Uncharge for a let-down. Animation only -- the figure never
// shoots itself (no projectile on the partner's machine; the bolt's hit stays the real-hit path, WO-173). The aim hold and the draw
// need tags only the game's own shooting sets: not shown (listed in the findings).   WO201-RANGED out|in ...
public partial class GameBridge
{
    private bool _w201RangedRefused;
    private long _w201RangedOut, _w201RangedIn, _w201RangedShown;

    private void Wo201RangedTick()
    {
        if (!_w201Connected || _w201RangedRefused || !W201("figure_ranged") || !(W137Host || W137Joiner)) return;
        _ = Wo201RangedPollAsync();
    }

    private async Task Wo201RangedPollAsync()
    {
        var r = await _combat.Wo163Async(Protocol.Wo201OpRangedPoll, new byte[] { 1 });
        if (r is not { Ok: true } rr || rr.Payload.Length < 1)
        {
            _w201RangedRefused = true;
            Console.WriteLine($"WO201-RANGED poll refused by the DLL ({(r is { } x ? x.Reason : 255)}) -- this player's shots are not shown on the partner's screen");
            return;
        }
        int n = rr.Payload[0];
        for (int i = 0; i < n && 1 + i < rr.Payload.Length; i++)
        {
            byte phase = rr.Payload[1 + i];
            if (Wo201Rules.RangedFragment(phase).Length == 0) continue;   // a step the partner's screen cannot show is not sent
            foreach (byte g in Wo134Peers()) await W164SendAsync(g, Protocol.W164RangedStep, FormattableString.Invariant($"v1|{phase}"));
            long c = Interlocked.Increment(ref _w201RangedOut);
            if (c <= 20 || c % 50 == 0) Console.WriteLine($"WO201-RANGED out {Wo201Rules.RangedPhaseName(phase)} [n={c}]");
        }
    }

    private async Task Wo201OnRangedStepAsync(byte src, string text)
    {
        if (!W201("figure_ranged")) return;
        var f = text.Split('|');
        if (f.Length != 2 || f[0] != "v1" || !byte.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte phase)) return;
        string frag = Wo201Rules.RangedFragment(phase);
        if (frag.Length == 0) return;
        Interlocked.Increment(ref _w201RangedIn);
        string avatar = Wo141Rules.AvatarName(src);
        bool bow = _w201FigureProp.TryGetValue(src, out var cls) && Wo201Models() is { } m && m.TryGetValue(cls.ToString("D"), out var v)
                   && v.Model.Contains("bow", StringComparison.OrdinalIgnoreCase) && !v.Model.Contains("crossbow", StringComparison.OrdinalIgnoreCase);
        string tags = Wo201Rules.RangedTags(phase, bow);
        int? id = null;
        try { id = await _combat.Wo143OneShotAsync(avatar, frag, tags, 0, 0); } catch { }
        if (id is >= 0) Interlocked.Increment(ref _w201RangedShown);
        Console.WriteLine($"WO201-RANGED in from={src} {Wo201Rules.RangedPhaseName(phase)} -> {avatar} '{frag}' tags='{tags}' {(id is >= 0 ? $"request {id}" : "refused")}");
    }

    private string Wo201RangedStats() =>
        $"ranged_out={Interlocked.Read(ref _w201RangedOut)} ranged_in={Interlocked.Read(ref _w201RangedIn)} ranged_shown={Interlocked.Read(ref _w201RangedShown)}";
}
