// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-200 -- one world, one story: actions, not results (docs/WO-200-findings.md).
//
// The standing rule: actions go to the authority; values come back. The joiner's conversation is sent as the action it is -- the
// dialogue node's out-port pulse -- and the host replays it on its own node; the host's values (quest States, quest items, an NPC's
// vitals, mount and clock) come back as values.
//
// New kinds on WO-137's join-channel messages (APPEND-ONLY; the relay's gate needs no code: same types, same routing):
//   QuestAsk  kind 5 OutPortReplay "<closure> <seq> <port> <path>"           a joiner -> the host: his conversation's out-port fired
//   QuestHost kind 7 ReplayAck     "<closure> <verdict> <runtime> <path>"    the host -> that joiner: ran | refused-* | off | queued
//   QuestHost kind 8 ItemCount     "<count> <itemClass>"                     the host -> every joiner: the host's count of a quest item
// closure: the joiner's own id of the capture (the DLL's, high bit set: a local closure); seq: his capture order; verdicts are one word.
//
// The DLL's ops in the WO-163 family (native/KCDMP/wo200.h): 70 Status, 71 Config, 72 Poll, 73 Replay, 74 ReadNode, 75 Research.
//
// Extra rows on WO-143's activity-extra channel (APPEND-ONLY kinds):
//   kind 5 Vitals  [nameLen][name][health:4f][stamina:4f]    the host's NPC's own health and stamina (II.1)
//   W164 kind 18 Clock "<worldTime> <ratio x1000>"           the host's clock, every 2 s (II.7)
//   kind 6 Mount   [nameLen][name][horseLen][horse]          the host's NPC is on that horse (empty = on foot) (II.2)
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte Wo200OpStatus = 70, Wo200OpConfig = 71, Wo200OpPoll = 72, Wo200OpReplay = 73, Wo200OpReadNode = 74, Wo200OpResearch = 75;
    /// <summary>WO-207 D.2: the joiner's NPC triggers, quiet like its clocks ([cmd:1] 0 forget all, 1 add [n:1] n x [len:2][path]).</summary>
    public const byte Wo200OpQuietNodes = 125;

    // ---- QuestAsk / QuestHost kinds (APPEND-ONLY) ----
    public const byte QuestAskOutPortReplay = 5;
    public const byte QuestHostReplayAck = 7, QuestHostItemCount = 8;

    // ---- activity-extra kinds (APPEND-ONLY) ----
    public const byte ExtraKindVitals = 5, ExtraKindMount = 6;

    // ---- W164 kinds (APPEND-ONLY) ----
    /// <summary>WO-200 II.7, the host -> every joiner (every 2 s): "&lt;worldTime&gt; &lt;ratio x1000&gt;" -- the host's clock; the joiner slews to it.</summary>
    public const byte W164Clock = 18;
    /// <summary>WO-200 Act IV, a joiner -> the host: "&lt;kind&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt;" (lockpick | door) -- noise of his the host's world must hear.</summary>
    public const byte W164Noise = 19;
}
