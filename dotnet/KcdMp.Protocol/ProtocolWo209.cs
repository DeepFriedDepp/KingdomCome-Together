// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-209 -- each copy's opponent mirrored from the host's NPC's, the joiner's defence through the copy's own combat AI, the owner on every
// copy (docs/WO-209-findings.md).
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>The DLL's ops (native/KCDMP/wo209.h): 134 Config [bits:4] -> [bits:4] (1 copy_opponent_mirror, 2 copy_attack_ai,
    /// 4 copy_paired_engine, 8 copy_oneshot_request), 135 Status -> text.</summary>
    public const byte Wo209OpConfig = 134, Wo209OpStatus = 135;

    /// <summary>A (the DLL's ops): 136 OppRead [max:1] (the host: its NPCs' opponents), 137 OppIn [n:1] n x ([copyEid:4][kind:1][targetEid:4]) (the joiner).</summary>
    public const byte Wo209OpOppRead = 136, Wo209OpOppIn = 137;

    /// <summary>
    /// A: the host's NPCs' opponents, sent on a change and again every 1.5 s while one fights (the same 10 Hz stream as NpcAnim):
    /// [count:1] then per body [nameLen:1][name][kind:1][ghost:1][oppNameLen:1][oppName] -- kind 0 nobody, 1 the host's own player,
    /// 2 a peer's figure (ghost = that peer's id), 3 another NPC (oppName). Host to joiner.
    /// </summary>
    public const byte NpcOppUp = 0x7C, NpcOppDown = 0x7D;
    public const int NpcOppMaxBodies = 40;
    public const int NpcOppBodyMax = 1 + NpcOppMaxBodies * (1 + MaxNpcNameLen + 2 + 1 + MaxNpcNameLen);
}
