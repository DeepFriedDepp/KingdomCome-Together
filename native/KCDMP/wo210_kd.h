// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-210 C.3 (docs/WO-210-findings.md, mp_knockdown_sync): a host NPC's knockdown on its copy, the engine's way. The host's NpcAnim stream
// marks a human body down while its physics is no living entity (wo210_rules.h kCmDown); the joiner's copy falls through its own ragdoll
// manager (C_Actor's own slot, RequestRagdoll(0)) on the host's down edge and is stood up (RequestStandUp, what the script's actor:StandUp
// calls) on its up edge. Every address is read from this build's own bytes at install (the hit-death handler's ragdoll call, the script
// bind's StandUp) and checked by RTTI; a fault switches the feature off for the session. Main thread only.
#include <cstdint>

namespace kcdmp::wo210kd {

// The host (op 132's writer): a human body whose FullBody runs a ragdoll / knockdown action, or whose physics is present and no living entity.
bool host_down(uint32_t eid, void* entity, const char* fullBodyClass);   // the FullBody action's class name (null: none)

// The joiner (the owner's tick, per copy with a state one stream delay old): the edge rules (wo210_rules.h kd_step) and their engine calls.
// True while the copy is held down for its host NPC (the owner installs nothing, the position stream writes nothing).
bool copy_tick(uint32_t eid, const char* name, bool hostDown, double now);

// The position writer (npc_drive): this copy lies for its host NPC's knockdown -- leave its body to the engine's ragdoll.
bool held_down(uint32_t eid);

// A copy the joiner's world no longer drives (released, gone): forgotten without a call.
void forget(uint32_t eid);

int status(char* out, int n);

} // namespace kcdmp::wo210kd
