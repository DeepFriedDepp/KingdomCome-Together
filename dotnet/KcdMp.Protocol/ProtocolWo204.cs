// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-204 -- sleep and wait stop crashing, every copy's body gets one owner, the conversation feature finished
// (docs/WO-204-findings.md).
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>W164 kind 27 (Act V): "v1|&lt;markId&gt;|&lt;note&gt;" -- the note a player typed with his mark, so the partner's briefing shows it
    /// beside the mark's ping (the note is the mark's own: letters, digits, spaces and plain punctuation, at most 120 characters).</summary>
    public const byte W164MarkNote = 27;

    /// <summary>The DLL's ops (native/KCDMP/wo204.h): 115 Config [bits:4] -> [bits:4] (1 seat_hold), 116 Status -> text.</summary>
    public const byte Wo204OpConfig = 115, Wo204OpStatus = 116;
}
