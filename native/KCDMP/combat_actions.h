// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// The lifetime of the combat animation actions this mod queues (WO-221 A, mp_combat_action_end; docs/WO-221-findings.md).
//
// The fault (0.65.0, the joiner): a C_CombatAnimAction reads its body's combat part (C_Actor +0x300 -> +0x490) in two of its
// callbacks without a check -- "fragment started" (vftable slot 14) and "the fragment is about to end" (slot 17) -- on the animation
// job. The engine drops a body's combat part whenever the body changes its level of detail (C_Actor::SetMonsterLOD), dies or is
// reset, and when its actor is destroyed; all of them go through one virtual, C_Actor::ReleaseCombatActor. An action of ours still
// waiting or running on that body's action controller then faults in the game's own code the next time the controller runs it
// (minutes later, when the player comes back to the body).
//
// The rule: no combat action of ours outlives its body's combat part. Every action we queue is recorded here with its actor; at
// the engine's own release point, before the combat part goes, each one is stopped the engine's own way (Mannequin's
// IAction::Stop) and its two unguarded callbacks are taken out (the object's vftable pointer is set to a copy whose slots 14, 17
// and 18 do nothing), so nothing of it can read the combat part again -- whichever way the body went: an unbind, a hide, the
// joiner's own pool or layer, a death, a rebind, the end of a session.
#include <cstdint>

namespace kcdmp::combatactions {

// Main thread, right after C_CombatAnimActionManager::QueueAction took the action: one reference of ours is added.
void track(uint32_t eid, void* actor, void* action);

// Any thread. Every recorded action of this body (or of every body) is ended; returns how many.
int end_for_entity(uint32_t eid, const char* why);
int end_all(const char* why);

bool armed();

} // namespace kcdmp::combatactions
