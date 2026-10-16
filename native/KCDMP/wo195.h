// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-195 -- dead copies fall and end the fight: the native half (docs/WO-195-findings.md). The pure rules are wo195_rules.h.
//
//   The field (0.60.5, the joiner): at the host's death of an engaged copy, WO-187's opponent wish (alive 4 s after the agent's last
//   word) found the engine had cleared the copy's target at the death and wrote it back -- on the dying body, in the death's own frame
//   -- and even pinned combat_forcedTarget on it (WO187-OPP ... pinned ... after 2 overwrites); the guard hold and the engagement ran
//   out on their own stale clocks. With mp_dead_release on (default), a body that reads dead (WO-172's copy_dead: its soul says IsDead,
//   or the mod marked it dead) is never held, wished or engaged again:
//     * the writer's per-frame combat hold (motion.cpp) releases every hold on it at once (WO195-RELEASE ... why=frame-gate);
//     * WO-187's wish is dropped (its pin cleared), never applied or written back; a guard hold or a wish asked for it is refused;
//     * WO-132's engage on it is an engage off (its skirmish left);
//     * op 56 (the agent, at every [npcdeath] apply) runs the same release at once.
//   Nothing of the mod then holds the body. The mod's ApplyDeath kills the soul with no death reaction, so a copy stays upright, frozen
//   in the dead action's first pose (live); the agent's 1 s check (op 57: dead, still a living entity) lays it down with the game's own
//   ragdoll (the Lua's Entity:RagDollize).
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   54 Status -> text
//   55 Config [deadRelease:1] (255 = unchanged) -> [deadRelease:1]
//   56 Release [eid:4] -> [held:1 (wo195rules::kHeld*)][combatMode:1][living:1]   (0/1, 255 = unreadable)
//   57 Check   [eid:4] | [0:4][nameLen:1][name] -> [dead:1][combatMode:1][living:1][fightHeld:1]   (living 1 = still a living entity:
//              a copy killed by the mod's ApplyDeath gets no death reaction and stays upright, frozen in the dead action's first pose)
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo195 {

constexpr uint8_t kOpStatus = 54, kOpConfig = 55, kOpRelease = 56, kOpCheck = 57;

bool on();   // mp_dead_release

// Main thread. True = the switch is on and this body reads dead: the caller must not hold, wish or engage it. `what` names the
// caller ("combat-hold", "wish", "guard-hold", "engage"); the first refusal per body and kind is logged (WO195-DEAD), all are counted.
bool dead_gate(uint32_t eid, const char* what);

// Main thread. Every fight hold the mod has on this body, released now: the engagement (skirmish left), the guard hold, the opponent
// wish and its pin, the writer's combat mode. Returns the wo195rules::kHeld* mask of what it had. Logged (WO195-RELEASE).
uint8_t release(uint32_t eid, const char* why);

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);

} // namespace kcdmp::wo195
