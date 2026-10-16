// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-190 -- animation parity: 0.60.9's native half. The pure rules are wo190_rules.h.
//
//   0.1 the animation trace (read-only): every change of the installed action on every scope of a body's action controller (WO-187's
//       reader: CActionController / CActionScope by RTTI, each scope's vptr and name checked), with the action's class (RTTI), fragment id,
//       priority, flags, the controller's tag state, and the body's rendered speed and stream age. Bodies: the local player, the partners'
//       figures and the host's copies this writer drives (the nearest kMaxBodies within kNearM). Rate-limited per body and per minute.
//         WO190-ANIM utc=<ms> body=<n> role=player|figure|copy scope=<name> action=<rtti>|none frag=<id> prio=<p> flags=0x<f>
//                    was=<rtti>:<frag> held_ms=<the previous action's time> tags=<hex> spd=<m/s> age_ms=<stream age> fight=<0|1>
//       Modes: mp_anim_trace on (every body), mp_anim_scope_log auto (the default: the bodies in a fight only, at a lower rate), off.
//   A1  the guard idle through reactions (mp_anim_idle_watch): a figure whose partner fights, or a copy held in a fight, whose full-body
//       scope is empty 0.25 s gets its guard idle back (WO-187's remedy), at most once a second -- any time, not only after a move it was sent.
//         WO190-IDLE body=<n> role=<r> empty_ms=<ms> combat=<0|1> -> <remedy>
//       The body's weapon (op 52): both hands' weapon classes and the reaction rows' hand tags, for the agent's stand-in.
//   A6  mp_copy_swing_takeover (wo166.cpp asks swing_takeover_on()).
//   A3/A4 mp_anim_requeue_gate / mp_anim_gait_hold (motion.cpp asks).
//   A8  the copies the clinch gate refused (op 53), for the agent's plain-row fallback.
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   50 Status -> text
//   51 Config [traceMode 0|1|2][idleWatch][swingTakeover][requeueGate][gaitHold] (255 = unchanged; fewer bytes = the rest unchanged) -> 5 bytes
//   52 Weapon [eid:4] -> [right:i8][left:i8][kind:1 0 unknown,1 armed,2 bare hands][n:1][hand tags]
//   53 ClinchRefused [withinMs:4] -> [n:1]([eid:4][agoMs:4])*n
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo190 {

constexpr uint8_t kOpStatus = 50, kOpConfig = 51, kOpWeapon = 52, kOpClinchRefused = 53;

bool idle_watch_on();       // mp_anim_idle_watch (A1)
bool swing_takeover_on();   // mp_copy_swing_takeover (A6)
bool requeue_gate_on();     // mp_anim_requeue_gate (A3)
bool gait_hold_on();        // mp_anim_gait_hold (A4)

void tick();   // main thread, every frame
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);

// A3 (motion.cpp, main thread): counters of the requests the gate held back
void note_requeue(bool deduped, bool limited);

} // namespace kcdmp::wo190
