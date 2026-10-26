// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-208 -- one owner per copy, the joiner's own defence, the conversation's remaining failures (docs/WO-208-findings.md).
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>The DLL's ops (native/KCDMP/wo208.h): 130 Config [bits:4] -> [bits:4] (1 conv_cam_player_view, 2 target_in_fight, 4 defence_local, 8 copy_anim_owner), 131 Status -> text.</summary>
    public const byte Wo208OpConfig = 130, Wo208OpStatus = 131;

    /// <summary>A.2 (the DLL's ops): 132 StreamRead [max:1] (the host: its NPCs' full and upper body), 133 StreamIn [n:1]... (the joiner).</summary>
    public const byte Wo208OpStreamRead = 132, Wo208OpStreamIn = 133;

    /// <summary>
    /// A.2 the host's NPCs as state, not events: [count:1] then per body [nameLen:1][name][cm:1][nsc:1] nsc x a 34-byte scope
    /// ([scope:1][class:1][frag:2][prio:1][status:1][flags:4][tags:20][action:4]) -- the DLL's op 132 payload as it is, host to joiner.
    /// </summary>
    public const byte NpcAnimUp = 0x7A, NpcAnimDown = 0x7B;
    public const int NpcAnimScopeLen = 34;
    public const int NpcAnimMaxBodies = 40;
    public const int NpcAnimBodyMax = 1 + NpcAnimMaxBodies * (1 + MaxNpcNameLen + 2 + 2 * NpcAnimScopeLen);
}
