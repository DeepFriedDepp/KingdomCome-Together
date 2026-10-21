// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-202 -- crashes, quests that fail on their own, and desync (docs/WO-202-findings.md).
//
// The DLL's ops in the WO-163 family (native/KCDMP/wo202.h): 87 Status, 88 Config [bits:4].
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte Wo202OpStatus = 87, Wo202OpConfig = 88, Wo202OpFailPoll = 89;

    /// <summary>W164 kind 26 (V.3 keys_per_player): "k|&lt;class&gt;" a key this player took; "c|&lt;name&gt;|&lt;x&gt;|&lt;y&gt;|&lt;z&gt;" the host unlocked
    /// that chest.</summary>
    public const byte W164KeyShare = 26;
}
