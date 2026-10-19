// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-201 -- every bug from the first 0.61.5 field session, at its root (docs/WO-201-findings.md).
//
// The DLL's ops in the WO-163 family (native/KCDMP/wo201.h): 76 Status, 77 Config [role][joined][bits].
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte Wo201OpStatus = 76, Wo201OpConfig = 77, Wo201OpCombatEnd = 78, Wo201OpDude2 = 79, Wo201OpDude2Check = 80, Wo201OpDude2Census = 81, Wo201OpNear = 82, Wo201OpSpeechPoll = 83, Wo201OpSpeechPlay = 84, Wo201OpVotePoll = 85, Wo201OpRangedPoll = 86;

    // ---- W164 kinds (APPEND-ONLY) ----
    /// <summary>B15, the host -> every joiner: "&lt;load|save&gt; &lt;1|0&gt;" -- the host's game is loading or saving (its pings stall meanwhile).</summary>
    public const byte W164HostBusy = 20;
    /// <summary>Act V F1, both ways: a line the sender's engine voiced near the partner -- "v1|speaker|durMs|ageMs|voice file|text"
    /// (speaker "@me" = the sender's Henry; the text only inside the subtitle radius; under 200 bytes).</summary>
    public const byte W164SpeechLine = 21;
    /// <summary>Act V F2, talker -> partner: "v1|decision|option 1|option 2|..." (the rows the talker's HUD shows, plain text).</summary>
    public const byte W164VoteOptions = 22;
    /// <summary>Act V F2, partner -> talker: "v1|decision|option index (1-based)|voter".</summary>
    public const byte W164VoteCast = 23;
    /// <summary>Act V F2, talker -> partner: "v1|decision|what was chosen" (empty: closed without a known choice).</summary>
    public const byte W164VoteClose = 24;
    /// <summary>B11, both ways: "v1|phase" -- this player's crossbow / bow step (3 firing, 4 reloading, 5 uncharging), shown on his figure.</summary>
    public const byte W164RangedStep = 25;
}
