// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-210 -- every copy always animated, clinches, deaths and knockdowns, combat readiness, the shared dialogue, riders and carts
// (docs/WO-210-findings.md).
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>The DLL's ops (native/KCDMP/wo210.h): 138 Config [bits:4] -> [bits:4] (1 copy_state_always, 2 clinch_v2, 4 death_engine,
    /// 8 knockdown_sync, 16 copy_ready, 32 copy_hit_local_none, 64 rider_sync, 128 state_one_request), 139 Status -> text,
    /// 140 Asks [max:1] -> [n:1] n x ([kind:1][nameLen:1][name]) -- what the DLL asks the agent to do the game's own way (kind 1: release a
    /// copy's stance and unstance), 141 StreamForget [n:1] n x ([nameLen:1][name]) (the host: these bodies' NpcAnim state is sent again at
    /// the next poll; n 0 = every body).</summary>
    public const byte Wo210OpConfig = 138, Wo210OpStatus = 139, Wo210OpAsks = 140, Wo210OpStreamForget = 141;
    public const byte Wo210AskRelease = 1;

    /// <summary>C.1 / C.2: 142 FinishBody [eid:4][nameLen:1][name] (eid 0 = by name) -> [result:1][deadState:1] -- a body whose soul is dead
    /// but which never got the engine's dead state (no death reaction) is finished the engine's own way: its ragdoll manager's
    /// RequestRagdoll (when not lying already) and the state expansion's StartDeadAction, so the game's loot test passes. Results:
    /// 0 finished, 1 already in the dead state, 2 the soul is alive (refused), 3 not armed on this build, 4 no such body, 5 faulted.</summary>
    public const byte Wo210OpFinishBody = 142;
    /// <summary>E.2: the NpcDamage flag bit (beside 0x01 suppress, 0x02 fatal, 0x04 attributed) for a joiner's blow on a copy that was not
    /// judged on the joiner's screen (the engine's own "Hit was filtered" there): it carries no numbers and the host's engine judges it --
    /// the host never applies a value from it.</summary>
    public const byte NpcDamageFlagJudgeThere = 0x08;

    public const byte Wo210FinishDone = 0, Wo210FinishAlready = 1, Wo210FinishAlive = 2, Wo210FinishNotArmed = 3, Wo210FinishNoBody = 4, Wo210FinishFaulted = 5;

    /// <summary>
    /// A.3 / A.4: the joiner asks the host for these bodies' whole-body state again (a posture the stream has not confirmed for 2.5 s,
    /// every body after a time skip or a load): [count:1] then per body [nameLen:1][name]; count 0 = every body near the joiner. Joiner to host.
    /// </summary>
    public const byte NpcAnimAskUp = 0x7E, NpcAnimAskDown = 0x7F;
    public const int NpcAnimAskMaxBodies = 32;
    public const int NpcAnimAskBodyMax = 1 + NpcAnimAskMaxBodies * (1 + MaxNpcNameLen);
}
